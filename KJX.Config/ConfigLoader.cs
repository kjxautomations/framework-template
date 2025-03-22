using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using IniFile;

namespace KJX.Config;

public static class ConfigLoader
{
    private const string SystemSection = "System";
    private const string SystemTypeLabel = "SystemType";
    private const string TypeLabel = "_type";
    private const string InterfacePrefix = "_interface";
    private const string SimulatedLabel = "_simulated";

    public static HashSet<ConfigSection> LoadConfig(string configFile, string? systemsDirectory = null)
    {
        using var stream = new FileStream(configFile, FileMode.Open, FileAccess.Read);
        return LoadConfig(stream, systemsDirectory);
    }
    public static HashSet<ConfigSection> LoadConfig(Stream configFile, string? systemsDirectory = null)
    {
        // load the main config file
        try
        {
            var parser = new Ini(configFile);
            string? systemType = null;
            // get the [System] SystemType parameter
            var systemParam = parser.Where((section => section.Name == SystemSection)).ToList();
            if (systemParam.Count == 1)
            {
                var p = systemParam[0].FirstOrDefault(x => x.Name == SystemTypeLabel);
                if (p == null)
                    throw new InvalidDataException($"{SystemSection} must contain {SystemTypeLabel}");
                systemType = p.Value;
            }

            var result = new HashSet<ConfigSection>();
            // look for the {SystemType}.ini file in systemsDirectory and load it
            if (systemType != null)
            {
                if (string.IsNullOrEmpty(systemsDirectory))
                    throw new InvalidDataException("Null or empty systems directory");
                var fullPath = Path.Combine(systemsDirectory, systemType + ".ini");
                var systemIni = new Ini(fullPath);
                ParseMergeSections(result, systemIni.Where(x => x.Name != SystemSection));
            }
            ParseMergeSections(result, parser.Where(section => section.Name != SystemSection));

            // make sure that every entry has a type defined
            foreach (var section in result)
            {
                if (section.Type == null)
                    throw new InvalidDataException($"Section '{section.Name}' does not define a type");
                VerifyAndConvertProperties(section);
                VerifyInterfaces(section);
            }
            
            return result;

        }
        catch (Exception e)
        {
            throw new ConfigError($"Error loading main config file '{configFile}'", e);
        }
    }
    
    public static bool IsSupportedType(Type type)
    {
        return type.IsPrimitive || type == typeof(string);
    }

    /// <summary>
    /// Take the final loaded ConfigSection and convert all the properties to the correct type.
    /// Along the way, perform validation on the properties.
    /// </summary>
    /// <param name="section">The section to validate</param>
    /// <exception cref="InvalidDataException"></exception>
    private static void VerifyAndConvertProperties(ConfigSection section)
    {
        bool IsRequired(PropertyInfo prop)
        {
            return prop.GetCustomAttributes(typeof(RequiredAttribute), true).Length > 0 ||
                   prop.GetCustomAttributes(typeof(RequiredMemberAttribute), true).Length > 0;
        }
        var allPropertiesWithRequiredAttribute = section.Type.GetProperties()
            .Where(p => IsRequired(p) && IsSupportedType(p.PropertyType))
            .Select(p => p.Name)
            .ToList();
        var convertedProperties = new Dictionary<string, object>();
        foreach (var item in section.Properties)
        {
            var prop = section.Type.GetProperty(item.Key);
            if (prop == null || !prop.CanWrite)
                throw new InvalidDataException(
                    $"{item.Key} not found in type {section.Type.Name} as a settable property");
            var convertedObject = ParseObject(prop.PropertyType, item.Value.ToString());
            if (convertedObject == null)
            {
                throw new InvalidDataException($"Unable to convert '{item.Value}' to '{prop.PropertyType.Name}'");
            }
            if (!PropertyValidator.TryValidateProperty(prop, convertedObject, out var validationResults))
            {
                throw new InvalidDataException(
                    $"Validation failed for property '{item.Key}' in section '{section.Name}': {string.Join(", ", validationResults.Select(x => x.ErrorMessage))}");
            }
            convertedProperties.Add(item.Key, convertedObject);
            allPropertiesWithRequiredAttribute.Remove(item.Key);
        }
        section.Properties.Clear();
        // copyConvertedProperties to the section
        foreach (var item in convertedProperties)
        {
            section.Properties.Add(item.Key, item.Value);
        }

        if (allPropertiesWithRequiredAttribute.Any())
        {
            throw new InvalidDataException($"Required properties did not have values provided in section '{section.Name}': {string.Join(", ", allPropertiesWithRequiredAttribute)}");
        }
    }

    public static object? ParseObject(Type type, string? itemValue)
    {
        var converter = TypeDescriptor.GetConverter(type);
        var convertedObject = converter.ConvertFromString(itemValue);
        return convertedObject;
    }
    
    private static void ParseMergeSections(HashSet<ConfigSection> existingSections, IEnumerable<Section> sections)
    {
        foreach (var section in sections)
        {
            ParseMergeSection(existingSections, section);
        }
    }

    private static void ParseMergeSection(HashSet<ConfigSection> existingSections, Section section)
    {
        Type type = null;
        ConfigSection result = existingSections.FirstOrDefault(x => x.Name == section.Name);
        if (result == null)
        {
            var typeItem = section.FirstOrDefault(item => item.Name == TypeLabel);
            if (typeItem != null)
            {
                type = Type.GetType(typeItem.Value);
                if (type == null)
                    throw new InvalidDataException($"Unable to load type '{typeItem.Value}'");
                var simItem = section.FirstOrDefault(item => item.Name == SimulatedLabel);
                if (simItem != null && bool.TryParse(simItem.Value, out var isSimulated) && isSimulated)
                {
                    type = RemapSimulatedType(type);
                }
            }
            else
            {
                throw new InvalidDataException($"Section {section.Name} does not define a type");
            }

            result = new ConfigSection(type, section.Name);
            ParseInterfaces(result, section);
            LoadProperties(result, section);
            existingSections.Add(result);
        }
        else
        {
            // see if the new section redefines the type. If so, it is a full replacement
            var typeItem = section.FirstOrDefault(item => item.Name == TypeLabel);
            if (typeItem != null)
            {
                type = Type.GetType(typeItem.Value);
                if (type == null)
                    throw new InvalidDataException($"Unable to load type '{typeItem.Value}'");
                if (type != result.Type)
                {
                    // complete redefinition - clear the old one
                    result.Type = type;
                    result.Interfaces.Clear();
                    result.Properties.Clear();
                }
                // we explicitly do NOT throw away the interfaces and properties if the type is marked as simulated
                var simItem = section.FirstOrDefault(item => item.Name == SimulatedLabel);
                if (simItem != null && bool.TryParse(simItem.Value, out var isSimulated) && isSimulated)
                {
                    result.Type = RemapSimulatedType(type);
                }
            }
            else
            {
                type = result.Type;
                var simItem = section.FirstOrDefault(item => item.Name == SimulatedLabel);
                if (simItem != null && bool.TryParse(simItem.Value, out var isSimulated) && isSimulated)
                {
                    type = result.Type = RemapSimulatedType(type);
                }
            }
            ParseInterfaces(result, section);
            LoadProperties(result, section);
        }

        if (type == null)
        {
            throw new InvalidDataException($"{section.Name} does not define a type");
        }
        
    }

    private static void LoadProperties(ConfigSection result, Section section)
    {
        foreach (var item in section.Where(x => x.Name != TypeLabel))
        {
            if (item.Name.StartsWith(InterfacePrefix) || item.Name == SimulatedLabel)
                continue;
            result.Properties[item.Name] = item.Value;
        }
    }

    private static void ParseInterfaces(ConfigSection result, Section section)
    {
        foreach (var item in section.Where(x => x.Name != TypeLabel))
        {
            if (item.Name.StartsWith(InterfacePrefix))
            {
                var interfaceType = Type.GetType(item.Value);
                if (interfaceType == null)
                    throw new InvalidDataException($"Unable to load interface type '{item.Value}' from section {result.Name}");
                if (!interfaceType.IsInterface)
                    throw new InvalidDataException($"{item.Value} in section {result.Name} is not an interface");
                result.Interfaces.Add(interfaceType);
            }
        }
    }

    private static void VerifyInterfaces(ConfigSection section)
    {
        foreach (var interfaceType in section.Interfaces)
        {
            if (!interfaceType.IsAssignableFrom(section.Type))
                throw new InvalidDataException($"Type {section.Type.Name} in section {section.Name} does not support interface {interfaceType.Name}");
        }

    }

    private static Type RemapSimulatedType(Type type)
    {
        // separate the namespaxe and class name
        var parts = type.FullName.Split('.');
        var simulatedTypeName = "Simulated" + parts.Last();
        var fqtn = $"{string.Join(".", parts.Take(parts.Length - 1))}.{simulatedTypeName}, {type.Assembly.FullName}";
        var result = Type.GetType(fqtn);
        if (result == null)
            throw new InvalidDataException($"Unable to load simulated type '{simulatedTypeName}'");
        return result;
    }
}