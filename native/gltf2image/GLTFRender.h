#pragma once

#include <mutex>
#include <functional>
#include <span>
#include <thread>
#include <vector>

namespace filament
{
    class Engine;
    class Renderer;
    class RenderTarget;
    class Scene;
    class Texture;
    class View;

    namespace backend
    {
        class PixelBufferDescriptor;
    }

    namespace gltfio
    {
        class AssetLoader;
        class FilamentAsset;
        class MaterialProvider;
        class ResourceLoader;
        class TextureProvider;
    }
}

struct NoCamerasFoundException {};
struct TooManyCamerasException {};
struct WrongThreadException {};

struct RenderResult
{
    RenderResult(uint32_t width, uint32_t height);
    ~RenderResult();

    void setCallback(std::function<void(std::exception_ptr, uint8_t*)> callback);

    uint32_t getWidth();
    uint32_t getHeight();

    filament::RenderTarget* createRenderTarget(filament::Engine* engine);
    void destroyRenderTarget();

    filament::backend::PixelBufferDescriptor createPixelBuffer();

    void reportException(std::exception_ptr exception);

private:
    uint32_t mWidth;
    uint32_t mHeight;

    filament::Engine* mEngine; // TODO weak_ptr
    filament::Texture* mTexture = nullptr;
    filament::RenderTarget* mRenderTarget = nullptr;

    std::vector<uint8_t> mBuffer;
    std::function<void(std::exception_ptr, uint8_t*)> mCallback = nullptr;
    std::exception_ptr mException;

    void onBufferReady(uint8_t* buffer);
};

struct RenderManager
{
    RenderManager();
    ~RenderManager();

    filament::gltfio::FilamentAsset* loadGLTFAsset(uint8_t* data, size_t size);
    void destroyGLTFAsset(filament::gltfio::FilamentAsset* asset);

    void render(std::vector<filament::gltfio::FilamentAsset*> assets, RenderResult* result);

private:
    std::thread::id mThreadId;

    filament::Engine* mEngine = nullptr;
    filament::Renderer* mRenderer = nullptr;

    filament::gltfio::AssetLoader* mAssetLoader = nullptr;
    filament::gltfio::MaterialProvider* mMaterialProvider = nullptr;
    filament::gltfio::TextureProvider* mTextureProvider = nullptr;
    filament::gltfio::ResourceLoader* mResourceLoader = nullptr;

    void verifyOnEngineThread();
};
