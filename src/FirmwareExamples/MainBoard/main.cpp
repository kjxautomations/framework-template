#include "mbed.h"
#include "ProtobufMessageParser.h"
#include "TcpConnectionHandler.h"
#include "MessageQueue.h"
#include "ProtobufMessageProcessor.h"



MessageQueue<Response> queue;
ProtobufMessageParser accumulator;
ProtobufMessageProcessor processor(queue);


int main() {
    TcpConnectionHandler handler(9968);
    handler.Run(accumulator, processor, queue);
    
    return 0;
}
