using System.ComponentModel.DataAnnotations;

namespace KJX.Devices.Logic;

using System;

[AttributeUsage(AttributeTargets.Property)]
public class RangeIncrementAttribute : RangeAttribute
{
    public object? Increment { get; }

    public RangeIncrementAttribute(double min, double max, double increment) : base(min, max)
    {
        Increment = increment;
    }
    public RangeIncrementAttribute(int min, int max, int increment) : base(min, max)
    {
        Increment = increment;
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
