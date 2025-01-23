# Core 
## Dependency Injection
This is a companion to the Config assembly. This code loads the output of the Config assembly and uses it to populate the specified Autofac container.

It is designed to support the following patterns:
- All objects are single instance
- All objects have a metadata value called Name that is set to the section they were defined in.
- All objects have a Key that is also the name of the config section
- All objects are assumed and required to have a constructor string parameter called "Name" that is set to the section they were defined in.
- All specified properties are set on the object after it is created.

# Usage
To consume an object, follow the standard Autofac patterns.