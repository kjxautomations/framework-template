#include "ProtobufMessageSerializer.h"
#include <pb_encode.h>
#include <cstdint>
#include <limits>
#include <stdint.h>

void ProtobufMessageSerializer::Serialize(const Response* response, std::vector<uint8_t>& buffer)
{
    uint32_t buffer_size;
    if (!pb_get_encoded_size(&buffer_size, Response_fields, response) || buffer_size > std::numeric_limits<uint16_t>::max())
        std::abort();
    uint16_t shortBufferSize = (uint16_t)buffer_size;

    buffer.resize(buffer_size + sizeof(shortBufferSize));
    memcpy(buffer.data(), &shortBufferSize, sizeof(shortBufferSize));

    // Create an output stream pointing to our vector
    auto stream = pb_ostream_from_buffer(buffer.data() + sizeof(shortBufferSize), buffer_size);

    // Serialize the message into the stream
    bool status = pb_encode(&stream, Response_fields, response);
    if (!status)
        std::abort(); // critical error
}
