#pragma once

#include <functional>
#include <span>
#include <thread>
#include <vector>

namespace filament
{
    class Engine;
    class Renderer;
    class Texture;

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
struct PixelBufferWrongSizeException {};

// Opaque handle returned to callers in the render completion callback. Holds the per-render Filament
// resources (View, RenderTarget, Scene, Texture) that must outlive the GPU work for a render. The
// caller is responsible for invoking RenderManager::destroyRenderResources on the engine thread once
// it is finished with the rendered pixel data, which destroys those resources together.
struct RenderResources;

struct RenderManager
{
    RenderManager();
    ~RenderManager();

    filament::gltfio::FilamentAsset* loadGLTFAsset(uint8_t* data, size_t size);
    void destroyGLTFAsset(filament::gltfio::FilamentAsset* asset);

    // Submits a render. The callback is invoked once the pixel readback has completed and the
    // output buffer has been populated. The callback receives an opaque RenderResources* that the
    // caller must later hand back to destroyRenderResources(), on the engine thread, in order to
    // release the per-render Filament objects. Destroying those objects before the readback fires
    // would race with the GPU.
    //
    // If render() throws (e.g. NoCamerasFoundException, PixelBufferWrongSizeException), any
    // resources allocated up to that point are destroyed synchronously and the callback is NOT
    // invoked.
    void render(
        uint32_t width,
        uint32_t height,
        std::span<filament::gltfio::FilamentAsset*> assets,
        std::span<uint8_t> output,
        std::function<void(RenderResources*)> callback);

    // Destroys all Filament resources associated with a single render (View, RenderTarget, Scene,
    // and the offscreen color Texture) and frees the RenderResources object itself. Must be called
    // on the engine thread, and only after the render completion callback has fired (or after
    // render() has thrown, in which case the resources have already been destroyed and the caller
    // never sees the RenderResources*).
    void destroyRenderResources(RenderResources* resources);

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
