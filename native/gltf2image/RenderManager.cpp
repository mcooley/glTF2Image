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

template <typename F>
struct scope_exit
{
    F mCleanup;
    bool mDisarmed = false;

    explicit scope_exit(F cleanup) noexcept : mCleanup(std::move(cleanup)) {}

    void disarm() noexcept {
        mDisarmed = true;
    }

    ~scope_exit() noexcept {
        if (!mDisarmed) {
            mCleanup();
        }
    }
};

// Per-render Filament resources. Owned by the render submission until the readPixels callback fires;
// at that point ownership transfers to the user, who must hand the pointer back to
// RenderManager::destroyRenderResources on the engine thread.
struct RenderResources
{
    filament::View* mView = nullptr;
    filament::RenderTarget* mRenderTarget = nullptr;
    filament::Scene* mScene = nullptr;
    filament::Texture* mTexture = nullptr;

    // Callback to invoke once readPixels has populated the output buffer. Takes ownership of the
    // RenderResources*; the user must call destroyRenderResources on it eventually.
    std::function<void(RenderResources*)> mCompletionCallback;
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
    std::function<void(RenderResources*)> callback) {
    verifyOnEngineThread();

    if (output.size() != backend::PixelBufferDescriptor::computeDataSize(
        backend::PixelDataFormat::RGBA,
        backend::PixelDataType::UBYTE,
        width,
        height,
        1)) {
        throw PixelBufferWrongSizeException();
    }

    // Allocate per-render Filament resources. These must outlive the GPU work that uses them, so
    // ownership is transferred to the readPixels callback on success. If we throw on the way to
    // readPixels, the scope_exit below tears them down synchronously.
    auto* resources = new RenderResources();
    resources->mCompletionCallback = std::move(callback);

    scope_exit resourcesCleanup([this, resources]() {
        destroyRenderResources(resources);
    });

    resources->mView = mEngine->createView();

    resources->mTexture = Texture::Builder()
        .width(width)
        .height(height)
        .levels(1)
        .sampler(Texture::Sampler::SAMPLER_2D)
        .format(Texture::InternalFormat::RGBA8)
        .usage(Texture::Usage::COLOR_ATTACHMENT | Texture::Usage::BLIT_SRC)
        .build(*mEngine);

    resources->mRenderTarget = RenderTarget::Builder()
        .texture(RenderTarget::AttachmentPoint::COLOR, resources->mTexture)
        .build(*mEngine);

    resources->mView->setRenderTarget(resources->mRenderTarget);
    resources->mView->setBlendMode(View::BlendMode::TRANSLUCENT);

    resources->mScene = mEngine->createScene();
    resources->mView->setViewport({ 0, 0, width, height });
    resources->mView->setScene(resources->mScene);

    gltfio::FilamentAsset::Entity cameraEntity;

    for (gltfio::FilamentAsset* asset : assets) {
        resources->mScene->addEntities(asset->getEntities(), asset->getEntityCount());

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
    resources->mView->setCamera(camera);

    mRenderer->renderStandaloneView(resources->mView);

    backend::PixelBufferDescriptor pixelBufferDescriptor(
        output.data(),
        output.size(),
        backend::PixelDataFormat::RGBA,
        backend::PixelDataType::UBYTE,
        &sImmediateCallbackHandler,
        [](void*, size_t, void* user) {
            // Runs on Filament's driver service thread once readback into the caller-provided
            // pixel buffer is complete. Ownership of RenderResources is now the user's; their
            // callback is expected to schedule destroyRenderResources back onto the engine thread.
            auto* pResources = reinterpret_cast<RenderResources*>(user);

            std::function<void(RenderResources*)> callback = std::move(pResources->mCompletionCallback);

            if (callback) {
                callback(pResources);
            }
            else {
                // No completion callback was supplied; destroying engine objects off-thread is
                // unsafe, so we leak rather than crash. In practice the API never allows a null
                // callback.
            }
        },
        resources);

    mRenderer->readPixels(resources->mView->getRenderTarget(), 0, 0, width, height, std::move(pixelBufferDescriptor));

    // Ownership of resources has been handed to the readPixels callback. Disarm the cleanup so we
    // don't destroy them out from under the GPU.
    resourcesCleanup.disarm();

    mEngine->flush();
}

void RenderManager::destroyRenderResources(RenderResources* resources) {
    verifyOnEngineThread();

    if (!resources) {
        return;
    }

    // Order is largely interchangeable since the GPU is guaranteed to be done with these objects by
    // the time we reach here (either readPixels has fired, or we are in the synchronous error path
    // and renderStandaloneView was never called). Destroy from "outer" to "inner" anyway.
    if (resources->mScene) {
        mEngine->destroy(resources->mScene);
    }
    if (resources->mView) {
        mEngine->destroy(resources->mView);
    }
    if (resources->mRenderTarget) {
        mEngine->destroy(resources->mRenderTarget);
    }
    if (resources->mTexture) {
        mEngine->destroy(resources->mTexture);
    }

    delete resources;
}

void RenderManager::verifyOnEngineThread() {
    if (std::this_thread::get_id() != mThreadId) {
        throw WrongThreadException();
    }
}