#pragma once

#include <exception>

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
    WrongThread = 5,
    PixelBufferWrongSize = 6,
};
