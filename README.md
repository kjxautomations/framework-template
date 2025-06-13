# **KJX Automations**

Use our application template to get a head start on your instrument
control software (ICS) needs. This example provides the infrastructure and patterns required for a 
standard ICS application. Install the nuget package ([see below](#getting-started)) to install this template and get started.
The package can also be used to generate a fully-realized sample application that encapsulates a 'typical' DNA sequencing workflow and 
engineering application.

### Considerations for ICS

Developing software for instrument/hardware control and automation is a complex endeavor. 
Configuration management, logging, navigation, flexible UI, state management, etc.
need to be planned for and implemented to effectively develop your systems for release. Additionally,
the code should be flexible enough to accomodate device changes as your team moves through the
development process.

Furthermore, requirements for internal and external stakeholders vary wildly and are rarely static.
Often this leads to customer-facing and internal (or Engineering) software applications with drastically different user experiences,
that rely on the same underlying code and logic. This can lead to "maintenance hell",
if not managed effectively.

### What's Included?

This project provides an OS-agnostic example of an internal engineering UI and a customer-facing wizard based workflow,
utilizing [Avalonia](https://avaloniaui.net/) for cross-platform support. 

- Configuration management
- Logging
- State based navigation
- Notifications
- Charting
- Styling
- Patterns for reusable and responsive UI components
- Simulation

### Getting Started

This template repository is also available as an installable nuget package. Download the latest [Release](https://github.com/kjxautomations/framework-template/releases) and install 
the package to begin using this in your IDE of choice.

To install the nuget package via the CLI:

```
dotnet new install <path-to-pkg>
```

You can now select the template from your IDE.

<p align="center">
<img width="600" alt="Screenshot 2025-05-16 at 9 22 12 AM" src="https://github.com/user-attachments/assets/472df699-56b9-4d21-a899-bf1d5f266807" />
</p>

Open the solution file in the latest Visual Studio or Rider. The _Engineering_ project includes an interface that exposes manual device control. 
The _Control_ project includes a wizard-style UI, offering a more guided workflow.

### Need more?

Not every project is the same, and we're here to help. Whether it is implementing real devices and hardware or workflow customizations contact us: git@kjxautomations.com
