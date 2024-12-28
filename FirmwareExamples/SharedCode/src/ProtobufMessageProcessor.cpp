#include "ProtobufMessageProcessor.h"
#include "Led.h"
Led led1(LED1);
Led led2(LED2);
Led led3(LED3);
    
    
void ProtobufMessageProcessor::ProcessMessage(std::unique_ptr<Request> message)
{
    if (message->header.target_node_id == NodeId_MAIN)
    {
        auto response = std::make_unique<Response>();
        if (message->has_get_firmware_versions)
        {
            response->has_firmware_versions = true;
            response->firmware_versions.main_version = 123;
        }
        else if (message->has_ping) 
        {
            response->has_pong = true;
            response->pong.id = message->ping.id;
        }
        else if (message->has_led_control)
        {
            Led* led = nullptr;
            switch (message->led_control.led_type) 
            {
                case LedType_LED1:
                    led = &led1;
                    break;
                case LedType_LED2:
                    led = &led2;
                    break;
                case LedType_LED3:
                    led = &led3;
                    break;
            }
            if (led != nullptr) 
            {
                led->SetState(message->led_control.led_status == LedStatus_ON);
                response->has_ack = true;
            }
            else 
            {
                response->has_nak = true;
                response->nak.error_code = 2;
            }
        }
        else 
        {
            response->has_nak = true;
            response->nak.error_code = 2;
        }
        response->header.request_id = message->header.request_id;
        response->header.source_node_id = NodeId_MAIN;
        response->header.target_node_id = message->header.source_node_id;
        _responses.Enqueue(std::move(response));

    }
    else 
    {
        // route to other boards vis CANBus
    }

}

void ProtobufMessageProcessor::ConnectionTerminated()
{

}
