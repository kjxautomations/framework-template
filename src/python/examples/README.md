# Examples

Complete projects that drive an instrument through the scripting API. Each directory stands on its
own: open it as a project in PyCharm, or run it from a shell, and read its README for what it does.

| Example                          | Does                                                              |
|----------------------------------|-------------------------------------------------------------------|
| [home_and_move](home_and_move/)  | Homes the X and Y motors, then walks them out 1 mm at a time to 10 mm |

All of them need two things: the client installed, and an instrument application running to talk
to.

```
pip install -e src/python
dotnet run --project src/KJX.ProjectTemplate.Engineering
```

The application serves scripts over a unix socket by default, and over the network only when it is
given a port and a token. See [kjx-instrument](../README.md) for the client and
[KJX.Scripting](../../Lib/KJX.Scripting/README.md) for the design.
