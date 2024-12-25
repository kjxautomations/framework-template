#include <ProtobufMessageParser.h>
#include <algorithm>
#include <memory>
#include <pb_decode.h>


std::vector<std::unique_ptr<Request>> ProtobufMessageParser::ProcessBuffer(
		std::vector<uint8_t>::const_iterator start,
		std::vector<uint8_t>::const_iterator end) 
{
	_buffer.insert(_buffer.end(), start, end);
	std::vector<std::unique_ptr<Request>> messages;
	size_t bufferIndex = 0;

    while (bufferIndex + sizeof(uint16_t) <= _buffer.size()) 
    {
        // Read the length of the message
        uint16_t messageLength;
        memcpy(&messageLength, _buffer.data() + bufferIndex, sizeof(messageLength));
        bufferIndex += sizeof(messageLength); // Move past the length prefix

        // Check if we have the complete message
        if (bufferIndex + messageLength <= _buffer.size()) 
        {
            pb_istream_t stream = pb_istream_from_buffer(_buffer.data() + bufferIndex, messageLength);
    
            // Attempt to decode the message
            auto message = std::make_unique<Request>();
            bool status = pb_decode(&stream, Request_fields, message.get());
            if (status)
            {
                messages.push_back(std::move(message));
            }
            else 
            {
                // Fatal error - deserialization failure. Most likely a code issue
                std::abort();
            }
            
            bufferIndex += messageLength; // Move past the message content
        } else {
            // Not enough data for this message, we'll wait for more data
            break;
        }
    }

    // Remove the parsed part of the buffer
    if (bufferIndex > 0) {
        _buffer.erase(_buffer.begin(), _buffer.begin() + bufferIndex);
    }
    return messages;
}

void ProtobufMessageParser::ConnectionTerminated()
{
	_buffer.clear();
}




