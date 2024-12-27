#pragma once
#include "firmware.pb.h"
#include <vector>

class ProtobufMessageSerializer
{
    private:
    ProtobufMessageSerializer() = delete;
    public:
    // Serialize objects for transmission, INCLUDING a uint16_t length prefixed
    // calls std::abort() if there is a serialization failure or the message is too big
    static void Serialize(const Response* request, std::vector<uint8_t>& dest_buffer);
};