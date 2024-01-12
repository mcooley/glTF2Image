#include <queue>
#include <thread>
#include <mutex>
#include <condition_variable>
#include <functional>

struct WorkQueue
{
    void addWorkItem(std::function<void()> workItem);
    void addWorkItemAndWait(std::function<void()> workItem);
    void processWorkItems();
    void start();
    void exit();

private:
    std::queue<std::function<void()>> mWorkItems;
    std::mutex mMutex;
    std::condition_variable mCondition;
    std::thread mThread;
    bool mExit = false;
};
