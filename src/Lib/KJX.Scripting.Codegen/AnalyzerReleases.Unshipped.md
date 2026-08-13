; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
KJXSG001 | KJX.Scripting | Error | Script API names must be unique across the assembly
KJXSG002 | KJX.Scripting | Error | DTO cannot be rebuilt from its wire form
KJXSG003 | KJX.Scripting | Warning | Member left out of the script API
KJXSG004 | KJX.Scripting | Error | Scripting runtime is not referenced
