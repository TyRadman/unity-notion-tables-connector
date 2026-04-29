using System;
using UnityEngine;

/// <summary>
/// Marks the property as a Notion property, which means that its value can be pulled from or push to a property on a Notion Database under the condition that the class that has this property is marked as 
/// <see cref="TNotionDatabaseAttribute"/>
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class TNotionPropertyAttribute : PropertyAttribute
{
    public readonly string PropertyName;

    public TNotionPropertyAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }
}