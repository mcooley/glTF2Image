#include "GLTFRender.h"

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

RenderJob::RenderJob(Engine* engine, uint32_t width, uint32_t height)
    : mEngine(engine)
    , mWidth(width)
    , mHeight(height) {
}

RenderJob::~RenderJob() {
    destroyView();

    if (mTexture) {
        mEngine->destroy(mTexture);
        mTexture = nullptr;
    }

    if (mRenderTarget) {
        mEngine->destroy(mRenderTarget);
        mRenderTarget = nullptr;
    }
}

uint32_t RenderJob::getWidth() {
    return mWidth;
}

uint32_t RenderJob::getHeight() {
    return mHeight;
}

void RenderJob::addAsset(gltfio::FilamentAsset* asset) {
    mAssets.push_back(asset);
}

View* RenderJob::createView() {
    mTexture = Texture::Builder()
        .width(mWidth)
        .height(mHeight)
        .levels(1)
        .sampler(Texture::Sampler::SAMPLER_2D)
        .format(Texture::InternalFormat::RGBA8)
        .usage(Texture::Usage::COLOR_ATTACHMENT)
        .build(*mEngine);
    mRenderTarget = RenderTarget::Builder()
        .texture(RenderTarget::AttachmentPoint::COLOR, mTexture)
        .build(*mEngine);

    mView = mEngine->createView();
    mView->setRenderTarget(mRenderTarget);
    mView->setBlendMode(View::BlendMode::TRANSLUCENT);
    mScene = mEngine->createScene();
    mView->setViewport({ 0, 0, mWidth, mHeight });
    mView->setScene(mScene);

    gltfio::FilamentAsset::Entity cameraEntity;

    for (gltfio::FilamentAsset* asset : mAssets) {
        mScene->addEntities(asset->getEntities(), asset->getEntityCount());

        size_t cameraEntityCount = asset->getCameraEntityCount();
        if ((cameraEntityCount > 0 && cameraEntity) || cameraEntityCount > 1) {
            throw std::exception(); // There must be exactly one camera defined per render.
        }
        else if (cameraEntityCount == 1) {
            cameraEntity = asset->getCameraEntities()[0];
        }
    }

    if (!cameraEntity) {
        throw std::exception(); // There must be exactly one camera defined per render.
    }

    filament::Camera* camera = mEngine->getCameraComponent(cameraEntity);
    mView->setCamera(camera);

    return mView;
}

void RenderJob::destroyView() {
    if (mScene) {
        mEngine->destroy(mScene);
        mScene = nullptr;
    }

    if (mView) {
        mEngine->destroy(mView);
        mView = nullptr;
    }
}

RenderResult::RenderResult(uint32_t width, uint32_t height)
    : mWidth(width)
    , mHeight(height) {
}

void RenderResult::setCallback(std::function<void(uint8_t*)> callback) {
    mCallback = callback;
}

uint32_t RenderResult::getWidth() {
    return mWidth;
}

uint32_t RenderResult::getHeight() {
    return mHeight;
}

backend::PixelBufferDescriptor RenderResult::createPixelBuffer() {
    mBuffer = std::vector<uint8_t>(backend::PixelBufferDescriptor::computeDataSize(
        backend::PixelDataFormat::RGBA,
        backend::PixelDataType::UBYTE,
        mWidth,
        mHeight,
        1));
    backend::PixelBufferDescriptor bufferDescriptor(
        mBuffer.data(),
        mBuffer.size(),
        backend::PixelDataFormat::RGBA,
        backend::PixelDataType::UBYTE,
        &sImmediateCallbackHandler,
        [](void* buffer, size_t, void* user) {
            auto pResult = reinterpret_cast<RenderResult*>(user);
            pResult->onBufferReady(reinterpret_cast<uint8_t*>(buffer));
        },
        this);
    return bufferDescriptor;
}

void RenderResult::onBufferReady(uint8_t* buffer) {
    if (mCallback) {
        mCallback(buffer);
    }
}

RenderManager::RenderManager() {
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
    mMaterialProvider->destroyMaterials();
    gltfio::AssetLoader::destroy(&mAssetLoader);
    delete mResourceLoader;
    delete mTextureProvider;
    delete mMaterialProvider;

    mEngine->destroy(mRenderer);
    Engine::destroy(&mEngine);
}

gltfio::FilamentAsset* RenderManager::loadGLTFAsset(uint8_t* data, size_t size) {
    gltfio::FilamentAsset* asset = mAssetLoader->createAsset(data, static_cast<uint32_t>(size));
    mResourceLoader->loadResources(asset);
    asset->releaseSourceData();
    return asset;
}

void RenderManager::destroyGLTFAsset(gltfio::FilamentAsset* asset) {
    mAssetLoader->destroyAsset(asset);
}

RenderJob* RenderManager::createJob(uint32_t width, uint32_t height) {
    return new RenderJob(mEngine, width, height);
}

void RenderManager::destroyJob(RenderJob* job) {
    delete job;
}

void RenderManager::render(RenderJob* job, RenderResult* result) {
    View* view = job->createView();

    mRenderer->renderStandaloneView(view);
    mRenderer->readPixels(view->getRenderTarget(), 0, 0, job->getWidth(), job->getHeight(), std::move(result->createPixelBuffer()));
    mEngine->flush();

    job->destroyView();
}
