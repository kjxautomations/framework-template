#pragma once

#include <cstdint>
#include "mbed.h"

class Led
{
    DigitalOut _device;
public:
    Led(uint32_t pin);

    void SetState(bool on);
};