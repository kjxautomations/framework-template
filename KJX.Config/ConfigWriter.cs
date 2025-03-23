using IniFile;

namespace KJX.Config;

/// <summary>
/// The ConfigWriter class is used to write a new configuration file based on the original
/// configuration file and the edited configuration. The edits may only be setting properties to
/// new values. The original configuration file is used to determine the structure of the new
/// file. Values in the changed set are added to the sections in the new configuration file.
/// If a value in the configuration file is set to what it would be anyway (i.e. the default value),
/// it is preserved, assuming that the original author did that for a reason, such as wanting
/// to pin the value instead of letting it change with the defaults that may change with new releases.
///
/// Any comments in the original configuration file are preserved in the new configuration file.
/// </summary>
public class ConfigWriter
{
    public static void SaveEditedConfig(Stream originalConfig, Stream newConfig,
        Dictionary<string, Dictionary<string, object>> newValues)
    {
        var parser = new Ini(originalConfig);
        foreach(var newSectionValues in newValues)
        {
            if (!parser.TryGetValue(newSectionValues.Key, out var sectionValues))
            {
                // a value was set for something that existed in a [System] section, but not in the original
                // Add this to the new config file. It does not need a _type b/c it inherits the type from
                // the [System] section.
                sectionValues = new Section(newSectionValues.Key);
                parser.Add(sectionValues);
            }
            foreach (var newValue in newSectionValues.Value)
            {
                sectionValues[newValue.Key] = newValue.Value.ToString(); 
            }
        }
        parser.SaveTo(newConfig);
    }
}