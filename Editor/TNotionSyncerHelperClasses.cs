using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;

public sealed class NotionFieldBinding
{
    private readonly FieldInfo _field;
    public readonly string NotionName;
    public Type FieldType => _field.FieldType;

    public NotionFieldBinding(FieldInfo field, string notionName)
    {
        _field = field;
        NotionName = notionName;
    }

    public bool TryGet(object target, out object value)
    {
        value = _field.GetValue(target);
        return value != null;
    }

    public void Set(object target, object value)
    {
        _field.SetValue(target, value);
    }
}

// TODO: move them to separate script files
public sealed class NotionDatabaseSchema
{
    public readonly Dictionary<string, string> Properties = new Dictionary<string, string>(64);
}

public sealed class NotionPage
{
    public string PageId;
    public string Key;
    public Dictionary<string, object> Properties;
}
public sealed class TableEntry
{
    public string DbId;
    public string ApiKey; // any key that can read this DB
    public string Label;  // multiline label
    public int TotalAssets;

    public readonly List<TypeEntry> Types = new List<TypeEntry>(8);

    public bool MatchesMain(string q)
    {
        if (string.IsNullOrEmpty(q)) return true;
        return (Label?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
            || (DbId?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
    }
}

public sealed class TypeEntry
{
    public DatabaseInfo Db; // your existing DatabaseInfo
    public int AssetCount;
    public string Label; // one-liner
}

public sealed class DatabaseInfo
{
    public readonly Type Type;
    public readonly string RawDatabaseName;
    public readonly string ApiKey;
    public readonly List<NotionFieldBinding> Properties;

    public string DisplayName => $"{Type.Name}.cs";

    public DatabaseInfo(Type type, string rawDbName, string apiKey, List<NotionFieldBinding> props)
    {
        Type = type;
        RawDatabaseName = rawDbName;
        ApiKey = apiKey;
        Properties = props;
    }

    public bool TryResolveDatabaseId(out string databaseId, out string error)
    {
        error = null;

        databaseId = ResolveDatabaseId(RawDatabaseName);
        if (!string.IsNullOrEmpty(databaseId))
            return true;

        databaseId = null;

        if (string.IsNullOrEmpty(RawDatabaseName))
            error = $"Type '{DisplayName}' has no database id in [NotionDatabase(...)] (first argument is empty).";
        else
            error = $"Type '{DisplayName}' database id must be exactly 32 characters. Got length: {RawDatabaseName.Trim().Length}";

        return false;
    }

    public bool TryResolveDatabaseIdForAsset(ScriptableObject asset, out string databaseId, out string error)
    {
        error = null;

        databaseId = ResolveDatabaseId(RawDatabaseName);
        if (!string.IsNullOrEmpty(databaseId))
            return true;

        if (asset is ITNotionSyncUniqueTable table)
        {
            databaseId = ResolveDatabaseId(table.TableID);
            if (!string.IsNullOrEmpty(databaseId))
                return true;

            error = $"Asset '{asset.name}' has TableID but it's not 32 chars (or empty).";
            return false;
        }

        databaseId = null;
        error = $"Asset '{asset.name}' does not implement ITNotionSyncUniqueTable and no attribute db id exists.";
        return false;
    }

    private static string ResolveDatabaseId(string input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        string s = input.Trim();
        return s.Length == 32 ? s : null;
    }
}