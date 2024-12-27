

#ifndef INC_TCPCONNECTIONHANDLER_H_
#define INC_TCPCONNECTIONHANDLER_H_


#include "mbed.h"
#include "EthernetInterface.h"
#include "ProtobufMessageParser.h"
#include "ProtobufMessageProcessor.h"
#include "MessageQueue.h"


class TcpConnectionHandler {
private:
    TCPSocket  _serverSocket;
    Socket*    _clientSocket{nullptr};
    EthernetInterface _net;

public:
    TcpConnectionHandler(int port);
    ~TcpConnectionHandler();
    void Run(ProtobufMessageParser& accumulator, ProtobufMessageProcessor& processor, MessageQueue<Response>& msgSource);

private:
    void CloseClient();
};



#endif /* INC_TCPCONNECTIONHANDLER_H_ */
