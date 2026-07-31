; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
KJXSA001 | KJX.Scripting | Error | Unsupported type on the script API surface
KJXSA002 | KJX.Scripting | Error | Events are not scriptable
KJXSA003 | KJX.Scripting | Error | Generic declarations are not scriptable
KJXSA004 | KJX.Scripting | Error | ref, out and in are not scriptable
KJXSA005 | KJX.Scripting | Error | Script API member names must be unique
KJXSA006 | KJX.Scripting | Error | Object references are not permitted inside DTOs
KJXSA007 | KJX.Scripting | Error | Delegates are not scriptable
KJXSA008 | KJX.Scripting | Error | Unsupported member kind on a script API interface
