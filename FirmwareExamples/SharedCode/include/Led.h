#pragma once

#include <cstdint>
#include "mbed.h"

class Led
{
    // Assuming LED is connected to PB0 for LD1, adjust as per your board's configuration
    DigitalOut _device;
    public:
    Led(uint32_t pin);

    void SetState(bool on);
};