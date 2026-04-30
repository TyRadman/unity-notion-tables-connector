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

    /// <summary>Database ID only — API key is read from the Notion Syncer settings.</summary>
    public TNotionDatabaseAttribute(string databaseName)
    {
        this.databaseName = databaseName;
    }

    /// <summary>Per-asset DB ID via <see cref="ITNotionSyncUniqueTable"/> — API key from settings.</summary>
    public TNotionDatabaseAttribute()
    {
    }

    /// <summary>Explicit API key override — use only when you need a different key than the one stored in settings.</summary>
    public TNotionDatabaseAttribute(string databaseName, string api)
    {
        this.databaseName = databaseName;
        this.api = api;
    }
}