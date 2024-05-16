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

	RenderJob* createJob(uint32_t width, uint32_t height) {
		RenderJob* renderJob;
		workQueue.addWorkItemAndWait([this, &renderJob, width, height]() {
			renderJob = renderManager->createJob(width, height);
			});
		return renderJob;
	}

	void destroyJob(RenderJob* job) {
		workQueue.addWorkItem([this, job]() {
			renderManager->destroyJob(job);
			});
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

	void render(RenderJob* job, RenderResult* result) {
		workQueue.addWorkItem([this, job, result]() {
			renderManager->render(job, result);
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
};

API_EXPORT ApiResult createRenderManager(void** renderManager) {
	try
	{
		RenderInterface* pRenderInterface = new RenderInterface();
		*renderManager = reinterpret_cast<void*>(pRenderInterface);
	}
	catch (...)
	{
		return ApiResult::UnknownError;
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult destroyRenderManager(void* renderManager) {
	try
	{
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		delete pRenderInterface;
	}
	catch (...)
	{
		return ApiResult::UnknownError;
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult loadGLTFAsset(void* renderManager, uint8_t* data, size_t size, void** gltfAsset) {
	try
	{
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		filament::gltfio::FilamentAsset* pAsset = pRenderInterface->loadGLTFAsset(data, size);
		if (!pAsset) {
			return ApiResult::InvalidScene_CouldNotLoadAsset;
		}

		*gltfAsset = reinterpret_cast<void*>(pAsset);
	}
	catch (...)
	{
		return ApiResult::UnknownError;
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult destroyGLTFAsset(void* renderManager, void* gltfAsset) {
	try
	{
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		filament::gltfio::FilamentAsset* pAsset = reinterpret_cast<filament::gltfio::FilamentAsset*>(gltfAsset);
		pRenderInterface->destroyGLTFAsset(pAsset);
	}
	catch (...)
	{
		return ApiResult::UnknownError;
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult createJob(void* renderManager, uint32_t width, uint32_t height, void** job) {
	try
	{
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		RenderJob* pJob = pRenderInterface->createJob(width, height);
		*job = reinterpret_cast<void*>(pJob);
	}
	catch (...)
	{
		return ApiResult::UnknownError;
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult destroyJob(void* renderManager, void* job) {
	try
	{
		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		RenderJob* pJob = reinterpret_cast<RenderJob*>(job);
		pRenderInterface->destroyJob(pJob);
	}
	catch (...)
	{
		return ApiResult::UnknownError;
	}

	return ApiResult::Success;
}

API_EXPORT ApiResult addAsset(void* job, void* gltfAsset) {
	try
	{
		RenderJob* pJob = reinterpret_cast<RenderJob*>(job);
		filament::gltfio::FilamentAsset* pAsset = reinterpret_cast<filament::gltfio::FilamentAsset*>(gltfAsset);
		pJob->addAsset(pAsset);
	}
	catch (...)
	{
		return ApiResult::UnknownError;
	}

	return ApiResult::Success;
}

typedef void (*RenderJobCallback)(uint8_t* buffer, uint32_t width, uint32_t height, void* user);

API_EXPORT ApiResult renderJob(void* renderManager, void* job, RenderJobCallback callback, void* user) {
	try
	{
		RenderJob* pJob = reinterpret_cast<RenderJob*>(job);

		RenderResult* pResult = new RenderResult(pJob->getWidth(), pJob->getHeight());
		pResult->setCallback([callback, pResult, user](uint8_t* buffer) {
			callback(buffer, pResult->getWidth(), pResult->getHeight(), user);
			delete pResult;
			});

		RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
		pRenderInterface->render(pJob, pResult);
	}
	catch (...)
	{
		return ApiResult::UnknownError;
	}

	return ApiResult::Success;
}