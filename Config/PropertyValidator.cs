using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Framework.Config;


public static class PropertyValidator
{
    public static bool TryValidateProperty(PropertyInfo propertyInfo, object value, out List<ValidationResult> validationResults)
    {
        // Initialize the result list to hold validation errors
        validationResults = new List<ValidationResult>();

        // Retrieve custom attributes from the property
        var validationAttributes = propertyInfo
            .GetCustomAttributes<ValidationAttribute>(true);

        // Validate the value against each attribute
        foreach (var attribute in validationAttributes)
        {
            // Call the IsValid method directly
            if (!attribute.IsValid(value))
            {
                // Create a validation result with the error message
                validationResults.Add(new ValidationResult(attribute.FormatErrorMessage(propertyInfo.Name)));
            }
        }

        // Return true if no validation errors were found
        return validationResults.Count == 0;
    }
}
