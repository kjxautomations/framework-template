# Firmware demo application

If you are building an instrument, having a dozen USB connectors from the PC to each
individual device is tolerable (barely) when you are in prototype stage, but as you move
toward productization, you'll want to integrate multiple devices under the control of 
one or more embedded controllers. There are many reasons:

- Predictable realtime response to sensors, including safety interlocks
- Cost reduction (several controller ships on a custom board is usually cheaper than multiple individual USB controllers) 
This demo application uses the MBedOS framework
- Reliability (every cable is a failure point)

## Choose a robust wire protocol

Serial connections can transparently lose bytes and corrupt bits. With a PC in the mix, there is no guarantee
that the OS won't be too busy to answer the UART interrupts before the serial buffer overflows.
You could use hardware flow control, but that's more wires, and that's only if the device in
question supports it. For full data integrity, you'd have to layer on an error-correcting protocol
like XModem.

I prefer to use TCP/IP over Ethernet. It's fast, handles collisions, and handles errors automatically.

In the past, I've used CAN-FD for communication between embedded controller boards. It has many
of the same features as UDP/IP, but with one big drawback - it only handles 64 byte messages at most.
Still, it's pretty trivial to write code that splits up large messages and reassembles them
at the destination, so I count CAN-FD as a good protocol as well.

Even if you are stuck with plain old CAN with its 8-byte payloads, you can still
use it with a good abstraction layer that splits and merges packets.

## Programming embedded controllers doesn't have to be hard

The MainBoard demo shows a way to use a very programmer-friendly embedded OS [MBed](https://os.mbed.com/)
and serialization frameworks [Protobuf](https://protobuf.dev/) and [NanoPB](https://github.com/nanopb/nanopb).
There is a dedicated main "thread", and it's easy to add more.

Why didn't we use STMCube? Well, I hate to say it, but it was far too buggy. The code it generated
for my board, the [Nucleo-144](https://www.st.com/en/evaluation-tools/nucleo-f767zi.html) was simply
incorrect. I don't have the familiarity with this platform to make it work.

My primary focus is getting early-stage companies up and running and set them up for success later.
If they hire a seasoned firmware engineer, the code written with good encapsulation can
be lifted to any other environment easily. This also enables software engineers and firmware
engineers to collaborate, by following patterns that should be familiar to both.

## Getting the example running

- Download the MBed Studio IDE
- Open the project
- Add a reference to the version of MBed OS you want to use. You can use the Libraries tab to do this. I recommend fething the Git repo and linking to a local copy of it for your projects. See the "Libraries" tab in the IDE.
- Set up soft links to the shared code. This is necessary because the IDE assumes all sources are contained within the tree. From the MainBoard directory:
    - Linux: ln -s ../SharedCode .
    - Windows: mklink /D SharedCode ..\SharedCode (may require Administrator rights)

- Connect your Nucleo-144 board to a spare Ethernet port
- Configure the port to IP: 192.168.68.100, netmask 255.255.255.0, no gateway
- Connect a USB-2.0 cable (not a charging cable!) to the port on the opposite side of the ethernet port
- Open the .NET unit tests and comment out the "Ignore" attribute on FirmwareConnnectionTestHarness
- Run the test

If you want to modify the protocol, you'll need to download and configure NanoPB. The .NET build system
automatically generates the C# classes, but you'll need to manually generate the C++ classes
with NanoPB and place the files in the generated_code subdirectory:
```
cd <your_git_dir>/framework-template/FirmwareExamples/SharedCode/generated_src/
py <your_nanopb_dir>/generator/nanopb_generator.py <your_git_dir>/framework-template/KJX.ProjectTemplate.Devices/Devices/FirmwareProtocol/firmware.proto \
    -I<your_git_dir>/framework-template/KJX.ProjectTemplate.Devices/Devices/FirmwareProtocol
```
