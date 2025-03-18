namespace KJX.Devices.Logic;

using System;

[AttributeUsage(AttributeTargets.Property)]
public class RangeAttribute : Attribute
{
    public double Min { get; }
    public double Max { get; }
    public double Increment { get; }

    public RangeAttribute(double min, double max, double increment)
    {
        Min = min;
        Max = max;
        Increment = increment;
    }

    public bool IsValid(IConvertible? value)
    {
        if (value == null)
            return false;

        var doubleValue = value.ToDouble(null);
        return doubleValue >= Min && doubleValue <= Max;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class GroupAttribute : Attribute
{
    public string Group { get; }

    public GroupAttribute(string group)
    {
        Group = group;
    }
}
