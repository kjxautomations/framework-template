#pragma once
#include <memory>
#include "firmware.pb.h"
#include "MessageQueue.h"

class ProtobufMessageProcessor 
{
    MessageQueue<Response>& _responses;
public:
    ProtobufMessageProcessor(MessageQueue<Response>& responses) : _responses(responses) {}
    void ProcessMessage(std::unique_ptr<Request> message);
    void ConnectionTerminated();
    ~ProtobufMessageProcessor() = default;
};