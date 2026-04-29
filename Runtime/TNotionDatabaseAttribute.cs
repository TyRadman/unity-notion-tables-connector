using System;
using UnityEngine;

/// <summary>
/// Marks a class as a Notion Database type. This will take effect only if at least one member is marked with
/// <see cref="TNotionPropertyAttribute"/>.
/// </summary>
/// <seealso cref="TNotionDatabaseAttribute"/>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TNotionDatabaseAttribute : PropertyAttribute
{
    public readonly string databaseName;
    public readonly string api;

    public TNotionDatabaseAttribute(string databaseName, string api)
    {
        this.databaseName = databaseName;
        this.api = api;
    }

    public TNotionDatabaseAttribute(string api)
    {
        this.api = api;
    }
}