#include "mbed.h"
#include "rtos/EventFlags.h"

#include <cstdint>
#include <iostream>
#include <memory>
#include <queue>
#include <vector>
#include <cstring>


#include "TcpConnectionHandler.h"
#include "ProtobufMessageSerializer.h"

// Define your static IP settings
#define STATIC_IP "192.168.68.5"
#define NETMASK "255.255.255.0"
#define GATEWAY "192.168.68.1"

#define FLAG_IO  0x01


TcpConnectionHandler::TcpConnectionHandler(int port)
{
    
    // Configure the network with static IP
    nsapi_error_t status = _net.set_network(STATIC_IP, NETMASK, GATEWAY);
    if (status != NSAPI_ERROR_OK) {
        printf("Failed to set network: %d\n", status);
        std::abort();
    }

    status = _net.connect();
    if (status != NSAPI_ERROR_OK) {
        printf("Failed to connect: %d\n", status);
        std::abort();
    }

    status = _serverSocket.open(&_net);
    if (status != NSAPI_ERROR_OK) {
        printf("Failed to open: %d\n", status);
        std::abort();
    }

	// Bind the server to port 9968
    if (_serverSocket.bind(port) != NSAPI_ERROR_OK) {
        printf( "Bind failed\n");
        std::abort();
    }

    // Start listening for incoming connections
    
	_serverSocket.set_blocking(false); 
}

TcpConnectionHandler::~TcpConnectionHandler() {
	_serverSocket.close();
	CloseClient();
}

void TcpConnectionHandler::Run(ProtobufMessageParser& messageAccumulator,
		ProtobufMessageProcessor& messageProcessor,
		MessageQueue<Response>& messageSource) 
{
    if (_serverSocket.listen(1) != NSAPI_ERROR_OK) {
        printf("Listen failed\n");
        std::abort();
    }

	std::vector<uint8_t> buffer(1024);
    EventFlags event_flags;
        
	std::vector<uint8_t> sendQueue;

	auto cleanup = [&]()
	{
		messageProcessor.ConnectionTerminated();
		messageAccumulator.ConnectionTerminated();
		messageSource.ConnectionTerminated();
        messageSource.ClearCallback();
		CloseClient();
	};

	while (true) {
        _clientSocket = _serverSocket.accept();
        
		if (_clientSocket != nullptr) {
			_clientSocket->set_blocking(false);
            _clientSocket->sigio([&]() 
            {
                event_flags.set(FLAG_IO);
                //printf("sigio set flag\n");
            });  // Attach activity callback
            event_flags.set(FLAG_IO);
            messageSource.RegisterCallback([&]() 
            {
                event_flags.set(FLAG_IO);
                //printf("message source set flag\n");
            });  // Attach activity callback)
		}

		while (_clientSocket != nullptr) 
        {
			// if our outgoing buffer is empty, try to refill it
			std::unique_ptr<Response> newMessage;
			if (sendQueue.empty() && messageSource.Pop(newMessage)) 
            {
                //printf("Serializing message\n");
                ProtobufMessageSerializer::Serialize(newMessage.get(), sendQueue);
                //printf("Got %d bytes\n", sendQueue.size());
			}
			if (!sendQueue.empty()) 
            {
				event_flags.set(FLAG_IO);
			}

			uint32_t flags = event_flags.wait_any(FLAG_IO);
            event_flags.clear(FLAG_IO);

            auto bytesRead = _clientSocket->recv(buffer.data(), buffer.size());

            //printf("Read %d bytes\n", bytesRead);

            if (bytesRead < 0 && bytesRead != NSAPI_ERROR_WOULD_BLOCK) 
            {
                cleanup();
                break;
            } 
            else if (bytesRead > 0)
            {
                event_flags.set(FLAG_IO);
                auto messages = messageAccumulator.ProcessBuffer(buffer.begin(), buffer.begin() +  bytesRead);
                for (auto& message : messages) 
                {
                    //printf("Processing message\n");
                    messageProcessor.ProcessMessage(std::move(message));
                }
            }
            
            if (!sendQueue.empty()) 
            {
                auto bytesSent = _clientSocket->send(sendQueue.data(), sendQueue.size());
                //printf("sent %d bytes\n", bytesSent);

                if (bytesSent > 0) 
                {
                    sendQueue.erase(sendQueue.begin(), sendQueue.begin() + bytesSent);
                } 
                else if (bytesSent < 0 && bytesSent != NSAPI_ERROR_WOULD_BLOCK)
                {
                    // some error - close it
                    cleanup();
                    break;
                }
                else 
                {
                    event_flags.set(FLAG_IO);
                }
            }
            if (!sendQueue.empty()) 
            {
                event_flags.set(FLAG_IO);  // Keep flag set if more data remains
            }
  		}
	}
	CloseClient();
}

void TcpConnectionHandler::CloseClient() 
{
	if (_clientSocket != nullptr) 
    {
		_clientSocket->close();
		_clientSocket = nullptr;
	}
}

