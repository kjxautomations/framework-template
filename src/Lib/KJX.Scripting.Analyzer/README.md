# Scripting Analyzer

## About
Enforces the rules that make the scripting surface generatable. It reports `KJXSA001` to
`KJXSA008` against any interface marked `[ScriptApi]`: unsupported types, events, generics,
by-reference parameters, duplicate wire names, references buried in DTOs, delegates, and member
kinds that cannot be addressed by name.

This is what keeps the source generator total. If an interface compiles, the generator can emit
dispatch, a descriptor entry and a client proxy for it without any remaining "what if" cases,
which is why these are errors rather than warnings.

Referenced as an analyzer, not as a library:

```xml
<ProjectReference Include="..\KJX.Scripting.Analyzer\KJX.Scripting.Analyzer.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

`ScriptApiTypeModel.cs` is shared source: the generator compiles the same file, so the two cannot
disagree about what an interface's surface is.

See [KJX.Scripting](../KJX.Scripting/README.md) for the rules themselves and the full diagnostic
table.
