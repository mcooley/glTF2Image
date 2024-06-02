#include "APITypes.h"
#include "GLTFRender.h"

ApiResult apiResultFromException(std::exception_ptr exception)
{
    try {
        std::rethrow_exception(exception);
    }
    catch (NoCamerasFoundException) {
        return ApiResult::InvalidScene_NoCamerasFound;
    }
    catch (TooManyCamerasException) {
        return ApiResult::InvalidScene_TooManyCameras;
    }
    catch (WrongThreadException) {
        return ApiResult::WrongThread;
    }
    catch (...) {
        return ApiResult::UnknownError;
    }
}

API_EXPORT ApiResult createRenderManager(void** renderManager) {
    try {
        RenderManager* pRenderManager = new RenderManager();
        *renderManager = reinterpret_cast<void*>(pRenderManager);
    }
    catch (...) {
        return apiResultFromException(std::current_exception());
    }

    return ApiResult::Success;
}

API_EXPORT ApiResult destroyRenderManager(void* renderManager) {
    try {
        RenderManager* pRenderManager = reinterpret_cast<RenderManager*>(renderManager);
        delete pRenderManager;
    }
    catch (...) {
        return apiResultFromException(std::current_exception());
    }

    return ApiResult::Success;
}

API_EXPORT ApiResult loadGLTFAsset(void* renderManager, uint8_t* data, size_t size, void** gltfAsset) {
    try {
        RenderManager* pRenderManager = reinterpret_cast<RenderManager*>(renderManager);

        filament::gltfio::FilamentAsset* asset = pRenderManager->loadGLTFAsset(data, size);
        if (!asset) {
            return ApiResult::InvalidScene_CouldNotLoadAsset;
        }

        *gltfAsset = reinterpret_cast<void*>(asset);
    }
    catch (...) {
        return apiResultFromException(std::current_exception());
    }

    return ApiResult::Success;
}

API_EXPORT ApiResult destroyGLTFAsset(void* renderManager, void* gltfAsset) {
    try {
        RenderManager* pRenderManager = reinterpret_cast<RenderManager*>(renderManager);
        filament::gltfio::FilamentAsset* pAsset = reinterpret_cast<filament::gltfio::FilamentAsset*>(gltfAsset);

        pRenderManager->destroyGLTFAsset(pAsset);
    }
    catch (...) {
        return apiResultFromException(std::current_exception());
    }

    return ApiResult::Success;
}

typedef void (*RenderCallback)(ApiResult apiResult, uint8_t* buffer, uint32_t width, uint32_t height, void* user);

API_EXPORT ApiResult render(void* renderManager, uint32_t width, uint32_t height, void** gltfAssets, uint32_t gltfAssetsCount, RenderCallback callback, void* user) {
    try {
        RenderResult* pResult = new RenderResult(width, height);
        pResult->setCallback([callback, pResult, user](std::exception_ptr exception, uint8_t* buffer) {
            if (exception) {
                ApiResult result = apiResultFromException(exception);
                callback(result, nullptr, 0, 0, user);
            }
            else {
                callback(ApiResult::Success, buffer, pResult->getWidth(), pResult->getHeight(), user);
            }

            delete pResult;
            });

        std::span<filament::gltfio::FilamentAsset*> assetsSpan(reinterpret_cast<filament::gltfio::FilamentAsset**>(gltfAssets), static_cast<size_t>(gltfAssetsCount));
        std::vector<filament::gltfio::FilamentAsset*> assetsVector(assetsSpan.begin(), assetsSpan.end());

        RenderManager* pRenderManager = reinterpret_cast<RenderManager*>(renderManager);

        pRenderManager->render(assetsVector, pResult);
    }
    catch (...) {
        return apiResultFromException(std::current_exception());
    }

    return ApiResult::Success;
}
