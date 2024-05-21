#include "GLTFRender.h"
#include "WorkQueue.h"

struct RenderInterface
{
	RenderInterface() {
		workQueue.start();
		workQueue.addWorkItemAndWait([this]() {
			renderManager = new RenderManager();
			});
	}

	~RenderInterface() {
		workQueue.addWorkItem([this]() {
			delete renderManager;
			});
		workQueue.exit();
	}

	filament::gltfio::FilamentAsset* loadGLTFAsset(uint8_t* data, size_t size) {
		filament::gltfio::FilamentAsset* asset;
		workQueue.addWorkItemAndWait([this, &asset, data, size]() {
			asset = renderManager->loadGLTFAsset(data, size);
			});
		return asset;
	}

	void destroyGLTFAsset(filament::gltfio::FilamentAsset* asset) {
		workQueue.addWorkItem([this, asset]() {
			renderManager->destroyGLTFAsset(asset);
			});
	}

	void render(std::span<filament::gltfio::FilamentAsset*> assets, RenderResult* result) {
		std::vector<filament::gltfio::FilamentAsset*> assetsVector(assets.begin(), assets.end());
		workQueue.addWorkItem([this, assetsVector = std::move(assetsVector), result]() {
			renderManager->render(assetsVector, result);
			});
	}

	RenderManager* renderManager = nullptr;
	WorkQueue workQueue;
};

#if _MSC_VER
#define API_EXPORT extern "C" __declspec(dllexport)
#else
#define API_EXPORT extern "C" __attribute__((visibility("default")))
#endif

enum class ApiResult : uint32_t
{
	Success = 0,
	UnknownError = 1,
	InvalidScene_CouldNotLoadAsset = 2,
	InvalidScene_NoCamerasFound = 3,
	InvalidScene_TooManyCameras = 4,
};

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
	catch (...) {
		return ApiResult::UnknownError;
	}
}

API_EXPORT ApiResult createRenderManager(void** renderManager) {
	try {
		RenderInterface* pRenderInterface = new RenderInterface();
		*renderManager = reinterpret_cast<void*>(pRenderInterface);
	}
	catch (...) {
		return apiResultFromException(std::current_exception());
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult destroyRenderManager(void* renderManager) {
	try {
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		delete pRenderInterface;
	}
	catch (...) {
		return apiResultFromException(std::current_exception());
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult loadGLTFAsset(void* renderManager, uint8_t* data, size_t size, void** gltfAsset) {
	try {
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		filament::gltfio::FilamentAsset* pAsset = pRenderInterface->loadGLTFAsset(data, size);
		if (!pAsset) {
			return ApiResult::InvalidScene_CouldNotLoadAsset;
		}

		*gltfAsset = reinterpret_cast<void*>(pAsset);
	}
	catch (...) {
		return apiResultFromException(std::current_exception());
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult destroyGLTFAsset(void* renderManager, void* gltfAsset) {
	try {
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		filament::gltfio::FilamentAsset* pAsset = reinterpret_cast<filament::gltfio::FilamentAsset*>(gltfAsset);
		pRenderInterface->destroyGLTFAsset(pAsset);
	}
	catch (...) {
		return apiResultFromException(std::current_exception());
	}

	return ApiResult::Success;
}

typedef void (*RenderCallback)(ApiResult apiResult, uint8_t* buffer, uint32_t width, uint32_t height, void* user);

API_EXPORT ApiResult render(void* renderManager, uint32_t width, uint32_t height, void** gltfAssets, uint32_t gltfAssetsCount, RenderJobCallback callback, void* user) {
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

		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		pRenderInterface->render(std::span<filament::gltfio::FilamentAsset*>(reinterpret_cast<filament::gltfio::FilamentAsset**>(gltfAssets), static_cast<size_t>(gltfAssetsCount)), pResult);
	}
	catch (...) {
		return apiResultFromException(std::current_exception());
	}

	return ApiResult::Success;
}