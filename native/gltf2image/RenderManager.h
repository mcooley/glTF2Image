#pragma once

#include <functional>
#include <span>
#include <thread>
#include <vector>

namespace filament
{
    class Engine;
    class Renderer;

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

struct RenderManager
{
    RenderManager();
    ~RenderManager();

    filament::gltfio::FilamentAsset* loadGLTFAsset(uint8_t* data, size_t size);
    void destroyGLTFAsset(filament::gltfio::FilamentAsset* asset);

    void render(
        uint32_t width,
        uint32_t height,
        std::span<filament::gltfio::FilamentAsset*> assets,
        std::span<uint8_t> output,
        std::function<void(int)> callback);

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
