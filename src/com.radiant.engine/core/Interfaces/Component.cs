using System;

namespace com.radiant.engine.core;

public interface Component { }

[AttributeUsage(AttributeTargets.Struct)]
public class ComponentDescriptionAttribute : Attribute
{
    public string Description { get; }
    public ComponentDescriptionAttribute(string description) => Description = description;
}