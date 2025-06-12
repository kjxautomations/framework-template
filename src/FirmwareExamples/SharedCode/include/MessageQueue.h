
#ifndef INC_MESSAGEQUEUE_H_
#define INC_MESSAGEQUEUE_H_

#include <memory>
#include <functional>
#include <deque>
#include "mbed.h"
#include "rtos.h"

template <class TMessage>
class MessageQueue {
public:
    using CallbackType = std::function<void()>;
    MessageQueue() = default;
    ~MessageQueue() = default;
    void Enqueue(std::unique_ptr<TMessage> item)
    {
        CallbackType notification;
        {
            ScopedLock<Mutex> lock(_mutex); 
            _queue.push_back(std::move(item));
            notification = _messageQueuedCallback;
        }
        if (notification)
        {
            notification();
        }
    }

    bool Pop(std::unique_ptr<TMessage>& item)
    {
        ScopedLock<Mutex> lock(_mutex); 
        if (_queue.empty()) {
            return false;  // Indicate the queue is empty
        }
        item = std::move(_queue.front());
        _queue.pop_front();
        return true;
    }
    void ConnectionTerminated()
    {
        ScopedLock<Mutex> lock(_mutex); 

        _queue.clear();
    }


    void RegisterCallback(const CallbackType& cb)
    {
        ScopedLock<Mutex> lock(_mutex);
        _messageQueuedCallback = cb;
    }
    void ClearCallback()
    {
        ScopedLock<Mutex> lock(_mutex);
        _messageQueuedCallback = nullptr;
    }
private:
    std::deque<std::unique_ptr<TMessage>> _queue;
    Mutex _mutex;
    CallbackType _messageQueuedCallback;
};



#endif /* INC_MESSAGEQUEUE_H_ */
