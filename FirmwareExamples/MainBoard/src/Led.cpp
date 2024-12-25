#include "Led.h"
#include "mbed.h"

Led::Led(uint32_t pin) : _device((PinName)pin)
{
    SetState(false);
}
void Led::SetState(bool on)
{
    _device = on ? 1 : 0;
}