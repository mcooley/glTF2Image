#include "GLTFRender.h"
#include "WorkQueue.h"

struct RenderInterface
{
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

typedef void (*CreateRenderManagerCallback)(ApiResult apiResult, void* renderManager, void* user);

API_EXPORT ApiResult createRenderManager(CreateRenderManagerCallback callback, void* user) {
	try {
		RenderInterface* pRenderInterface = new RenderInterface();
		pRenderInterface->workQueue.start();
		pRenderInterface->workQueue.addWorkItemAndWait([pRenderInterface]() {
			pRenderInterface->renderManager = new RenderManager();
			});

		// TODO make this actually async
		callback(ApiResult::Success, reinterpret_cast<void*>(pRenderInterface), user);
	}
	catch (...) {
		return apiResultFromException(std::current_exception());
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult destroyRenderManager(void* renderManager) {
	try {
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);

		pRenderInterface->workQueue.addWorkItemAndWait([pRenderInterface]() {
			delete pRenderInterface->renderManager;
			});
		pRenderInterface->workQueue.exit();
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

		filament::gltfio::FilamentAsset* asset = nullptr;
		pRenderInterface->workQueue.addWorkItemAndWait([pRenderInterface, &asset, data, size]() {
			asset = pRenderInterface->renderManager->loadGLTFAsset(data, size);
			});
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
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		filament::gltfio::FilamentAsset* pAsset = reinterpret_cast<filament::gltfio::FilamentAsset*>(gltfAsset);

		pRenderInterface->workQueue.addWorkItem([pRenderInterface, pAsset]() {
			pRenderInterface->renderManager->destroyGLTFAsset(pAsset);
			});
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

		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);

		pRenderInterface->workQueue.addWorkItem([pRenderInterface, assetsVector = std::move(assetsVector), pResult]() {
			pRenderInterface->renderManager->render(assetsVector, pResult);
			});
	}
	catch (...) {
		return apiResultFromException(std::current_exception());
	}

	return ApiResult::Success;
}