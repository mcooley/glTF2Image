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

API_EXPORT void* createRenderManager() {
	RenderInterface* pRenderInterface = new RenderInterface();
	return reinterpret_cast<void*>(pRenderInterface);
}

API_EXPORT void destroyRenderManager(void* renderManager) {
	RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
	delete pRenderInterface;
}

API_EXPORT void* loadGLTFAsset(void* renderManager, uint8_t* data, size_t size) {
	RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
	filament::gltfio::FilamentAsset* pAsset = pRenderInterface->loadGLTFAsset(data, size);
	return reinterpret_cast<void*>(pAsset);
}

API_EXPORT void destroyGLTFAsset(void* renderManager, void* gltfAsset) {
	RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
	filament::gltfio::FilamentAsset* pAsset = reinterpret_cast<filament::gltfio::FilamentAsset*>(gltfAsset);
	pRenderInterface->destroyGLTFAsset(pAsset);
}

API_EXPORT void* createJob(void* renderManager, uint32_t width, uint32_t height) {
	RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
	RenderJob* pJob = pRenderInterface->createJob(width, height);
	return reinterpret_cast<void*>(pJob);
}

API_EXPORT void destroyJob(void* renderManager, void* job) {
	RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
	RenderJob* pJob = reinterpret_cast<RenderJob*>(job);
	pRenderInterface->destroyJob(pJob);
}

API_EXPORT void addAsset(void* job, void* gltfAsset) {
	RenderJob* pJob = reinterpret_cast<RenderJob*>(job);
	filament::gltfio::FilamentAsset* pAsset = reinterpret_cast<filament::gltfio::FilamentAsset*>(gltfAsset);
	pJob->addAsset(pAsset);
}

typedef void (*RenderJobCallback)(uint8_t* buffer, uint32_t width, uint32_t height, void* user);

API_EXPORT void renderJob(void* renderManager, void* job, RenderJobCallback callback, void* user) {
	RenderJob* pJob = reinterpret_cast<RenderJob*>(job);

	RenderResult* pResult = new RenderResult(pJob->getWidth(), pJob->getHeight());
	pResult->setCallback([callback, pResult, user](uint8_t* buffer) {
		callback(buffer, pResult->getWidth(), pResult->getHeight(), user);
		delete pResult;
		});

	RenderInterface* pRenderInterface = reinterpret_cast<RenderInterface*>(renderManager);
	pRenderInterface->render(pJob, pResult);
}