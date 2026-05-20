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
struct MissingCallbackArgumentException {};

// Opaque handle for the per-render Filament resources that must outlive the GPU work for a render.
struct RenderResources;

struct RenderManager
{
    RenderManager();
    ~RenderManager();

    // Loads a GLTF asset from memory. The caller is responsible for calling destroyGLTFAsset()
    // when the asset is no longer needed. Must be called on the engine thread.
    filament::gltfio::FilamentAsset* loadGLTFAsset(uint8_t* data, size_t size);

    // Destroys a GLTF asset previously returned by loadGLTFAsset(). Must be called on the engine thread.
    void destroyGLTFAsset(filament::gltfio::FilamentAsset* asset);

    // Submits a render. Must be called on the engine thread. 
    // The callback is invoked once the pixel readback has completed and the output buffer has been
    // populated. The callback occurs on an arbitrary thread and receives a RenderResources* that the
    // caller must later hand back to destroyRenderResources(), on the engine thread, in order to
    // release the per-render Filament objects. If render() throws (e.g. NoCamerasFoundException,
    // PixelBufferWrongSizeException), any resources allocated up to that point are destroyed synchronously
    // and the callback is not invoked.
    void render(
        uint32_t width,
        uint32_t height,
        std::span<filament::gltfio::FilamentAsset*> assets,
        std::span<uint8_t> output,
        std::function<void(RenderResources*)> callback);

    // Frees the RenderResources object. Must be called on the engine thread, after the render completion
    // callback has fired.
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
