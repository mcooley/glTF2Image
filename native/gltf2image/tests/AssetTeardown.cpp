#include "../RenderManager.h"

#include <filament/Engine.h>
#include <filament/TransformManager.h>
#include <gltfio/FilamentAsset.h>
#include <gltfio/FilamentInstance.h>

#include <fstream>
#include <future>
#include <iostream>
#include <iterator>
#include <memory>
#include <stdexcept>
#include <string>

using filament::gltfio::FilamentAsset;

int main() {
    RenderManager renderer;
    auto destroy = [&](FilamentAsset* asset) { renderer.destroyGLTFAsset(asset); };
    auto load = [&](const char* path) {
        std::ifstream stream(path, std::ios::binary);
        if (!stream) {
            throw std::runtime_error(std::string("Could not open ") + path);
        }
        std::vector<uint8_t> data(std::istreambuf_iterator<char>{stream}, {});
        auto* asset = renderer.loadGLTFAsset(data.data(), data.size());
        if (!asset) {
            throw std::runtime_error(std::string("Could not load ") + path);
        }
        return std::unique_ptr<FilamentAsset, decltype(destroy)>(asset, destroy);
    };

    auto camera = load("orthographic_camera.gltf");
    const auto& transforms = camera->getEngine()->getTransformManager();
    const auto baseline = transforms.getComponentCount();
    bool passed = true;
    for (int frame = 1; frame <= 8; ++frame) {
        auto model = load("red_triangle_unlit.gltf");
        const auto instanceRoot = model->getInstance()->getRoot();
        FilamentAsset* scene[] = {camera.get(), model.get()};
        std::vector<uint8_t> pixels(100 * 100 * 4);
        std::promise<RenderResources*> readback;
        auto completed = readback.get_future();
        renderer.render(100, 100, scene, pixels,
                [&readback](RenderResources* resources) { readback.set_value(resources); });
        renderer.destroyRenderResources(completed.get());
        constexpr auto insideTriangle = (40 * 100 + 60) * 4;
        if (pixels[insideTriangle] <= 240 || pixels[insideTriangle + 3] != 255) {
            std::cerr << "Frame " << frame << ": missing rendered triangle\n";
            return 1;
        }

        model.reset();
        // The next endFrame can collect orphan transforms and hide an incomplete destroyAsset.
        const auto count = transforms.getComponentCount();
        const bool rootStillPresent = transforms.hasComponent(instanceRoot);
        if (count != baseline || rootStillPresent) {
            std::cerr << "Frame " << frame << ": expected " << baseline
                      << " transforms after model disposal, found " << count
                      << "; instance-root transform " << (rootStillPresent ? "present" : "absent") << '\n';
            passed = false;
        }
    }
    return passed ? 0 : 1;
}
