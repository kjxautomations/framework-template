using IniFile;

namespace Framework.Config;

public class ConfigSection(Type type, string name)
{
    public string Name { get; init; } = name;
    public Type Type { get; set; } = type;
    public HashSet<Type> Interfaces { get; } = new();
    public Dictionary<string, object> Properties { get; } = new();
}