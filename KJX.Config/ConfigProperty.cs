using System.Reflection;

namespace KJX.Config;

public class ConfigProperty
{
    public required string Name { get; init; }
    public required object Value { get; init; }
    public required bool IsRequired { get; init; }
    public required PropertyInfo PropertyInfo { get; init; }
}