#include "RenderManager.h"

#include <filament/Camera.h>
#include <filament/Engine.h>
#include <filament/RenderableManager.h>
#include <filament/Renderer.h>
#include <filament/RenderTarget.h>
#include <filament/Scene.h>
#include <filament/Texture.h>
#include <filament/View.h>
#include <filament/Viewport.h>

#include <utils/EntityManager.h>

#include <gltfio/AssetLoader.h>
#include <gltfio/FilamentAsset.h>
#include <gltfio/MaterialProvider.h>
#include <gltfio/ResourceLoader.h>
#include <gltfio/TextureProvider.h>
#include <gltfio/materials/uberarchive.h>

#include <backend/PixelBufferDescriptor.h>

using namespace filament;
using namespace utils;

// By default, Filament dispatches callbacks to the "main thread". Since we are not an interactive application, this
// queuing is not helpful to us. We'll execute callbacks directly on the driver service thread.
struct ImmediateCallbackHandler : public backend::CallbackHandler
{
    void post(void* user, Callback callback) override {
        callback(user);
    }
};
static ImmediateCallbackHandler sImmediateCallbackHandler{};

struct scope_exit
{
    std::function<void()> mCleanup;

    explicit scope_exit(std::function<void()> cleanup) noexcept : mCleanup(std::move(cleanup)) {}

    ~scope_exit() noexcept {
        if (mCleanup) {
            mCleanup();
        }
    }
};

struct RenderResult
{
    RenderResult() = default;
    ~RenderResult() {
        if (mTexture) {
            mEngine->destroy(mTexture);
        }
    }

    void createTexture(filament::Engine* engine, uint32_t width, uint32_t height) {
        mEngine = engine;

        mTexture = Texture::Builder()
            .width(width)
            .height(height)
            .levels(1)
            .sampler(Texture::Sampler::SAMPLER_2D)
            .format(Texture::InternalFormat::RGBA8)
            .usage(Texture::Usage::COLOR_ATTACHMENT)
            .build(*mEngine);
    }

    std::function<void()> mCallback = nullptr;
    filament::Engine* mEngine;
    filament::Texture* mTexture = nullptr;
};

RenderManager::RenderManager()
    : mThreadId(std::this_thread::get_id()) {
    mEngine = Engine::Builder()
        .backend(backend::Backend::VULKAN)
        .build();
    mRenderer = mEngine->createRenderer();
    mRenderer->setClearOptions(Renderer::ClearOptions{
        .clear = true
        });

    // Use pre-built materials.
    mMaterialProvider = filament::gltfio::createUbershaderProvider(mEngine, UBERARCHIVE_DEFAULT_DATA, UBERARCHIVE_DEFAULT_SIZE);

    mTextureProvider = filament::gltfio::createStbProvider(mEngine);
    mResourceLoader = new gltfio::ResourceLoader(gltfio::ResourceConfiguration{ .engine = mEngine });
    mResourceLoader->addTextureProvider("image/png", mTextureProvider);

    mAssetLoader = gltfio::AssetLoader::create({ mEngine, mMaterialProvider });
}

RenderManager::~RenderManager() {
    verifyOnEngineThread();

    mMaterialProvider->destroyMaterials();
    gltfio::AssetLoader::destroy(&mAssetLoader);
    delete mResourceLoader;
    delete mTextureProvider;
    delete mMaterialProvider;

    mEngine->destroy(mRenderer);
    Engine::destroy(&mEngine);
}

gltfio::FilamentAsset* RenderManager::loadGLTFAsset(uint8_t* data, size_t size) {
    verifyOnEngineThread();

    gltfio::FilamentAsset* asset = mAssetLoader->createAsset(data, static_cast<uint32_t>(size));
    if (!asset) {
        return nullptr;
    }

    mResourceLoader->loadResources(asset);
    asset->releaseSourceData();
    return asset;
}

void RenderManager::destroyGLTFAsset(gltfio::FilamentAsset* asset) {
    verifyOnEngineThread();

    mAssetLoader->destroyAsset(asset);
}

void RenderManager::render(
    uint32_t width,
    uint32_t height,
    std::span<filament::gltfio::FilamentAsset*> assets,
    std::span<uint8_t> output,
    std::function<void()> callback) {
    verifyOnEngineThread();

    RenderResult* result = new RenderResult();
    result->mCallback = callback;

    View* view = mEngine->createView();
    scope_exit viewCleanup([this, view]() {
        mEngine->destroy(view);
    });

    result->createTexture(mEngine, width, height);

    RenderTarget* renderTarget = RenderTarget::Builder()
        .texture(RenderTarget::AttachmentPoint::COLOR, result->mTexture)
        .build(*mEngine);
    scope_exit renderTargetCleanup([this, renderTarget]() {
        mEngine->destroy(renderTarget);
    });

    view->setRenderTarget(renderTarget);
    view->setBlendMode(View::BlendMode::TRANSLUCENT);
    Scene* scene = mEngine->createScene();
    scope_exit sceneCleanup([this, scene]() {
        mEngine->destroy(scene);
    });
    view->setViewport({ 0, 0, width, height });
    view->setScene(scene);

    gltfio::FilamentAsset::Entity cameraEntity;

    for (gltfio::FilamentAsset* asset : assets) {
        scene->addEntities(asset->getEntities(), asset->getEntityCount());

        size_t cameraEntityCount = asset->getCameraEntityCount();
        if ((cameraEntityCount > 0 && cameraEntity) || cameraEntityCount > 1) {
            throw TooManyCamerasException(); // There must be exactly one camera defined per render.
        }
        else if (cameraEntityCount == 1) {
            cameraEntity = asset->getCameraEntities()[0];
        }
    }

    if (!cameraEntity) {
        throw NoCamerasFoundException(); // There must be exactly one camera defined per render.
    }

    filament::Camera* camera = mEngine->getCameraComponent(cameraEntity);
    view->setCamera(camera);

    mRenderer->renderStandaloneView(view);

    if (output.size() != backend::PixelBufferDescriptor::computeDataSize(
        backend::PixelDataFormat::RGBA,
        backend::PixelDataType::UBYTE,
        width,
        height,
        1)) {
        throw PixelBufferWrongSizeException();
    }

    backend::PixelBufferDescriptor pixelBufferDescriptor(
        output.data(),
        output.size(),
        backend::PixelDataFormat::RGBA,
        backend::PixelDataType::UBYTE,
        &sImmediateCallbackHandler,
        [](void*, size_t, void* user) {
            auto pResult = reinterpret_cast<RenderResult*>(user);

            std::function<void()> callback = std::move(pResult->mCallback);

            delete pResult;

            if (callback) {
                callback();
            }
        },
        result);

    mRenderer->readPixels(view->getRenderTarget(), 0, 0, width, height, std::move(pixelBufferDescriptor));
    mEngine->flush();
}

void RenderManager::verifyOnEngineThread() {
    if (std::this_thread::get_id() != mThreadId) {
        throw WrongThreadException();
    }
}