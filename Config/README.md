# Configuration System

## Design Goals

The primary goal of the configuration system is to allow instruments to have calibration data that is per-instrument to be stored on the instrument. A good example of this is storing offsets from motor homing position to a standard coordinate system.
Other goals of the system are:
* Allow easy simulation of devices. This is useful during instrument bring-up when all the hardware may not be available.
* Facilitate experiments by internal users of the software, such as System Integration and Software Test. An example of this is performing A/B testing of the customer-facing software with alternate tuning parameters.  
* Allow advanced testing and debugging scenarios that involve the interchange of software components that share a common interface. Examples of this include:
* Allowing different instrument generations (e.g. several generations of prototypes) to be supported by a single program

## Audience

The target users of the configuration system are twofold - non-programmers and programmers. Non-programmers are presented with an easy-to-understand, easy-to-edit file format (INI files) that are capable of being anywhere from minimalist to a full specification of the object graph of a system.  
Programmers will mostly be interested in the ability to define separate base configuration files for each system architecture supported by the software.

## Why INI files?

A mentor of mine once told me a perl of wisdom, “Nobody but engineers like tree controls.” As engineers, we are so conditioned to overly-complex stuff that we often lose sight of how difficult it can be for non-programmers to use. INI files are simple to use, edit, and visually scan. XML and JSON are not.  
If you disagree or have different experience, then you are free to change the code to match your experience, beliefs, and intended target audience. What you see here reflects mine.

# Syntax

## Basic Syntax

INI files are text files that have sections defined in square brackets and attribute-value pairs with an equal sign. Comments must begin with a semicolon:  
```
[XMotor]  
_type = Framework.Devices.SimulatedLinearStepperMotor, Devices  
_interface1 = Framework.Devices.IMotor, Devices  
_interface2 = Framework.Devices.ISupportsInitialization, Devices  
_interface3 = Framework.Devices.ISupportsHoming, Devices  
Acceleration = 1  
Velocity = 1

*;[Webcam]*  
*;_type = Framework.Devices.Webcam, Devices*  
*;_interface1 = Framework.Devices.ICamera, Devices*  
*;_interface2 = Framework.Devices.ISupportsInitialization, Devices*  
*;Resolution = 640,480*

[Camera]  
_type = Framework.Devices.SimulatedCamera, Devices  
_interface1 = Framework.Devices.ICamera, Devices  
_interface2 = Framework.Devices.ISupportsInitialization, Devices
```

## All-in-one specification

The above example is an all-in-one specification, meaning that a single INI file defines all of the configurable elements of the system. Specifically, it must NOT contain a section named “System”. In this case, only the objects listed in the configuration file are defined, and they must be defined completely.  
This is mostly for writing small test programs that use this architecture.

## Minimal specification

This is the form that is intended for use in programs that will run on multiple instrument types and have calibration values. They contain a special section called “System” that causes a base configuration file to be loaded. Subsequent sections in the minimal config file are overlaid onto the base configuration.  
For example, we may have two systems. System 1 defines a motor supported by an RMS-356 stepper controller from LIN Industries:  
```
[XMotor]  
_type = Framework.Devices.RMS356StepperMotor, Devices  
_interface1 = Framework.Devices.IMotor, Devices  
_interface2 = Framework.Devices.ISupportsInitialization, Devices  
_interface3 = Framework.Devices.ISupportsHoming, Devices  
Acceleration = 1  
Velocity = 1
```  
The second system has moved all motion control into custom firmware:
```
[XMotor]  
_type = Framework.Devices.FirmwareStepperMotor, Devices  
_interface1 = Framework.Devices.IMotor, Devices  
_interface2 = Framework.Devices.ISupportsInitialization, Devices  
_interface3 = Framework.Devices.ISupportsHoming, Devices  
Acceleration = 1  
Velocity = 1

[Firmware]  
_type = Framework.Devices.FirmwareConnection, Devices  
_interface1 = Framework.Devices.IFirmwareConnection, Devices  
IPAddress = 192.168.2.34  
Port = 9968
```
The main configuration file can look like this:  
```
[System]  
SystemType = FirstSystem

[XMotor]  
Acceleration = 2
```
In this example, FirstSystem.ini is loaded, then the remaining values are overlaid onto the loaded values. In this example, “Acceleration = 2” is either a calibration value or an experimental value being used for testing.

## Reserved Values

The following tags are reserved for the system and may not be used for other purposes:

| Name        | Where | Description |
|:------------| :---- | :---- |
| System      | A section name | Defines a special section that controls the loading of other config files |
| SystemType  | In a “System” section | Defines the name of the INI file to be loaded for the system |
| _type       | In any non-System section | Defines the .NET object type that will be created to hold the configuration values |
| _interface* | In any non-System section | Defines 0 or more interfaces supported by the object |
| _simulated  | In any non-System section | If true, the object is simulated |

# What occurs under the hood

The configuration loader reads the INI file and creates a set of sections. Each section defines an object type, a set of interfaces, and a set of properties to be set on the object.  
When using the System/minimal configuration, the values in the loaded config file are overlaid on the ones from the System config file, with one exception. If you change the type of the object being created (e.g. set _type to a new value) then the properties an interfaces for that section are discarded.  
The remaining sections are ready to be inserted into a dependency-injection framework. Code is provided to do that for Autofac 8.

# Patterns for Simulation

## The easy way

The easiest way to simulate a device is to have a separate class that implements the same interfaces as the real device. The name of the 
class should be the same as the real class with the word “Simulated” prepended to it. It must exist in the same assembly and namespace as the 
real class.

For example:
```
namespace Framework.Devices  
{  
    public class LinearStepperMotor : IMotor, ISupportsInitialization, ISupportsHoming  
    {  
        // Implementation here  
    }
    public class SimulatedLinearStepperMotor : IMotor, ISupportsInitialization, ISupportsHoming  
    {  
        // Implementation here  
    }  
}
```

In the configuration file, you would specify the simulated class like this:  
```
[System]
SystemType = SystemWithXMotorDefined

[XMotor]
_simulated = true
```

This is the approach that we took in the desktop example.

## The hard way

A more difficult, but more flexible way to simulate a device is to completely replace its definition with a class that simulates the interfaces:  
```
[XMotor]  
_type = Framework.Devices.SimulatedLinearStepperMotor, Devices  
_interface1 = Framework.Devices.IMotor, Devices  
_interface2 = Framework.Devices.ISupportsInitialization, Devices  
_interface3 = Framework.Devices.ISupportsHoming, Devices  
```
This is the approach we took in the Browser sample.
