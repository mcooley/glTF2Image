#include <mutex>
#include <functional>
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

struct RenderJob
{
    RenderJob(filament::Engine* engine, uint32_t width, uint32_t height);
    ~RenderJob();

    uint32_t getWidth();
    uint32_t getHeight();

    void addAsset(filament::gltfio::FilamentAsset* asset);
    filament::View* createView();
    void destroyView();

private:
    filament::Engine* mEngine;
    std::vector<filament::gltfio::FilamentAsset*> mAssets;
    uint32_t mWidth;
    uint32_t mHeight;

    filament::View* mView = nullptr;
    filament::Scene* mScene = nullptr;

    filament::Texture* mTexture = nullptr;
    filament::RenderTarget* mRenderTarget = nullptr;
};

struct RenderResult
{
    RenderResult(uint32_t width, uint32_t height);

    void setCallback(std::function<void(uint8_t*)> callback);

    uint32_t getWidth();
    uint32_t getHeight();

    filament::backend::PixelBufferDescriptor createPixelBuffer();

private:
    uint32_t mWidth;
    uint32_t mHeight;
    std::vector<uint8_t> mBuffer;
    std::function<void(uint8_t*)> mCallback = nullptr;

    void onBufferReady(uint8_t* buffer);
};

struct RenderManager
{
    RenderManager();
    ~RenderManager();

    filament::gltfio::FilamentAsset* loadGLTFAsset(uint8_t* data, size_t size);
    void destroyGLTFAsset(filament::gltfio::FilamentAsset* asset);

    RenderJob* createJob(uint32_t width, uint32_t height);
    void destroyJob(RenderJob* job);

    void render(RenderJob* job, RenderResult* result);

private:
    filament::Engine* mEngine = nullptr;
    filament::Renderer* mRenderer = nullptr;

    filament::gltfio::AssetLoader* mAssetLoader = nullptr;
    filament::gltfio::MaterialProvider* mMaterialProvider = nullptr;
    filament::gltfio::TextureProvider* mTextureProvider = nullptr;
    filament::gltfio::ResourceLoader* mResourceLoader = nullptr;
};
