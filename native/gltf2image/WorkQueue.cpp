#include "WorkQueue.h"

#include <future>

void WorkQueue::addWorkItem(std::function<void()> workItem) {
    std::lock_guard<std::mutex> lock(mMutex);
    mWorkItems.push(workItem);
    mCondition.notify_one();
}

void WorkQueue::addWorkItemAndWait(std::function<void()> workItem) {
    std::promise<void> promise;
    std::future<void> future = promise.get_future();
    {
        std::lock_guard<std::mutex> lock(mMutex);
        mWorkItems.push([workItem, &promise] {
            try
            {
                workItem();
                promise.set_value();
            }
            catch (...)
            {
                promise.set_exception(std::current_exception());
            }

            });
        mCondition.notify_one();
    }
    future.wait();
    future.get();
}

void WorkQueue::processWorkItems() {
    while (true) {
        std::function<void()> workItem;
        {
            std::unique_lock<std::mutex> lock(mMutex);
            mCondition.wait(lock, [this] { return !mWorkItems.empty(); });
            workItem = mWorkItems.front();
            mWorkItems.pop();
        }
        workItem();  // Execute the work item

        if (mExit) {
            break;
        }
    }
}

void WorkQueue::start() {
    mThread = std::thread(&WorkQueue::processWorkItems, this);
}

void WorkQueue::exit() {
    addWorkItem([this]() { mExit = true; });
    mThread.join();
}