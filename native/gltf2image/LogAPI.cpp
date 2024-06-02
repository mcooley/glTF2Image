#include "APITypes.h"

#include <utils/Log.h>

enum class LogLevel : uint32_t
{
    Verbose = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
};

typedef void (*LogCallback)(LogLevel level, char const* message, void* user);

static LogCallback s_logCallback = nullptr;

API_EXPORT ApiResult setLogCallback(LogCallback callback, void* user) {
    try {
        if (!callback) {
            utils::slog.d.setConsumer(nullptr, nullptr);
            utils::slog.e.setConsumer(nullptr, nullptr);
            utils::slog.w.setConsumer(nullptr, nullptr);
            utils::slog.i.setConsumer(nullptr, nullptr);
            utils::slog.v.setConsumer(nullptr, nullptr);

            s_logCallback = nullptr;
        }
        else {
            s_logCallback = callback;

            utils::slog.d.setConsumer([](void* user, char const* message) {
                s_logCallback(LogLevel::Debug, message, user);
            }, user);

            utils::slog.e.setConsumer([](void* user, char const* message) {
                s_logCallback(LogLevel::Error, message, user);
            }, user);

            utils::slog.w.setConsumer([](void* user, char const* message) {
                s_logCallback(LogLevel::Warning, message, user);
            }, user);

            utils::slog.i.setConsumer([](void* user, char const* message) {
                s_logCallback(LogLevel::Info, message, user);
            }, user);

            utils::slog.v.setConsumer([](void* user, char const* message) {
                s_logCallback(LogLevel::Verbose, message, user);
            }, user);
        }
    }
    catch (...) {
        return ApiResult::UnknownError;
    }

    return ApiResult::Success;
}