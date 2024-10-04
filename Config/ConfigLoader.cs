using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using IniFile;

namespace Framework.Config;

public static class ConfigLoader
{
    private const string SystemSection = "System";
    private const string SystemTypeLabel = "SystemType";
    private const string TypeLabel = "_type";
    private const string InterfacePrefix = "_interface";
    
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
            }


            return result;
    
        }
        catch (Exception e)
        {
            throw new ConfigError($"Error loading main config file '{configFile}'", e);
        }
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
            }
            else
            {
                throw new InvalidDataException($"Section {section.Name} does not define a type");
            }

            result = new ConfigSection(type, section.Name);
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
            }
            else
            {
                type = result.Type;
            }
        }

        if (type == null)
        {
            throw new InvalidDataException($"{section.Name} does not define a type");
        }

        var allPropertiesWithRequiredAttribute = type.GetProperties()
            .Where(p => p.GetCustomAttributes(typeof(RequiredAttribute), true).Length > 0)
            .Select(p => p.Name)
            .ToList();
        foreach (var item in section.Where(x => x.Name != TypeLabel))
        {
            if (item.Name.StartsWith(InterfacePrefix))
            {
                var interfaceType = Type.GetType(item.Value);
                if (interfaceType == null)
                    throw new InvalidDataException($"Unable to load interface type '{item.Value}'");
                if (!interfaceType.IsInterface)
                    throw new InvalidDataException($"{item.Value} is not an interface");
                if (!interfaceType.IsAssignableFrom(type))
                    throw new InvalidDataException($"Type {type.Name} does not support interface {item.Value}");
                result.Interfaces.Add(interfaceType);
            }
            else
            {
                var prop = type.GetProperty(item.Name);
                if (prop == null || !prop.CanWrite)
                    throw new InvalidDataException(
                        $"{item.Name} not found in type {type.Name} as a settable property");
                var converter = TypeDescriptor.GetConverter(prop.PropertyType);
                var convertedObject = converter.ConvertFromString(item.Value.ToString());
                if (convertedObject == null)
                {
                    throw new InvalidDataException($"Unable to convert '{item.Value}' to '{prop.PropertyType.Name}'");
                }
                if (!PropertyValidator.TryValidateProperty(prop, convertedObject, out var validationResults))
                {
                    throw new InvalidDataException(
                        $"Validation failed for property '{item.Name}' in section '{section.Name}': {string.Join(", ", validationResults.Select(x => x.ErrorMessage))}");
                }
                result.Properties[item.Name] = convertedObject;
                allPropertiesWithRequiredAttribute.Remove(item.Name);
            }
        }

        if (allPropertiesWithRequiredAttribute.Any())
        {
            throw new InvalidDataException($"Required properties did not have values provided in section '{section.Name}': {string.Join(", ", allPropertiesWithRequiredAttribute)}");    
        }
    }
}