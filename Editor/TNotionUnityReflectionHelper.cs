using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class TNotionUnityReflectionHelper
{
    public static List<DatabaseInfo> FindDatabases()
    {
        var list = new List<DatabaseInfo>(32);
        var types = TypeCache.GetTypesWithAttribute<TNotionDatabaseAttribute>();

        for (int i = 0; i < types.Count; i++)
        {
            var t = types[i];
            if (!typeof(ScriptableObject).IsAssignableFrom(t))
                continue;

            var attr = t.GetCustomAttribute<TNotionDatabaseAttribute>(false);
            if (attr == null)
                continue;

            var props = FindNotionProperties(t);

            list.Add(new DatabaseInfo(t, attr.databaseName, attr.api, props));
        }

        return list;
    }

    private static List<NotionFieldBinding> FindNotionProperties(Type t)
    {
        var props = new List<NotionFieldBinding>(32);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var fields = t.GetFields(flags);
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            var a = f.GetCustomAttribute<TNotionPropertyAttribute>(true);
            if (a == null) continue;

            props.Add(new NotionFieldBinding(f, a.PropertyName));
        }

        return props;
    }

    public static List<ScriptableObject> FindAssetsOfType(Type t)
    {
        var result = new List<ScriptableObject>(128);
        var guids = AssetDatabase.FindAssets($"t:{t.Name}");

        for (int i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var asset = AssetDatabase.LoadAssetAtPath(path, t) as ScriptableObject;
            if (asset != null)
                result.Add(asset);
        }

        return result;
    }

    public static bool TryNormalizeValueForNotion(object value, Type fieldType, out object normalized)
    {
        normalized = null;
        if (value == null) return false;

        if (fieldType == typeof(int)) { normalized = (int)value; return true; }
        if (fieldType == typeof(long)) { normalized = (long)value; return true; }
        if (fieldType == typeof(float)) { normalized = (float)value; return true; }
        if (fieldType == typeof(double)) { normalized = (double)value; return true; }
        if (fieldType == typeof(bool)) { normalized = (bool)value; return true; }
        if (fieldType == typeof(string)) { normalized = (string)value; return true; }

        return false;
    }

    public static bool TryConvertNotionValueToField(object notionValue, Type fieldType, out object fieldValue)
    {
        fieldValue = null;
        if (notionValue == null) return false;

        if (fieldType == typeof(string))
        {
            fieldValue = notionValue.ToString();
            return true;
        }

        if (fieldType == typeof(bool))
        {
            if (notionValue is bool b) { fieldValue = b; return true; }
            if (notionValue is string sb && bool.TryParse(sb, out var pb)) { fieldValue = pb; return true; }
            return false;
        }

        if (fieldType == typeof(int))
        {
            if (TryToDouble(notionValue, out var d)) { fieldValue = (int)d; return true; }
            return false;
        }

        if (fieldType == typeof(long))
        {
            if (TryToDouble(notionValue, out var d)) { fieldValue = (long)d; return true; }
            return false;
        }

        if (fieldType == typeof(float))
        {
            if (TryToDouble(notionValue, out var d)) { fieldValue = (float)d; return true; }
            return false;
        }

        if (fieldType == typeof(double))
        {
            if (TryToDouble(notionValue, out var d)) { fieldValue = d; return true; }
            return false;
        }

        return false;
    }

    private static bool TryToDouble(object v, out double d)
    {
        d = 0;

        if (v is double dd) { d = dd; return true; }
        if (v is float ff) { d = ff; return true; }
        if (v is long ll) { d = ll; return true; }
        if (v is int ii) { d = ii; return true; }

        if (v is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            d = parsed;
            return true;
        }

        return false;
    }

    public static string NormalizeDbId(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        s = s.Trim();
        return s.Length == 32 ? s : null;
    }

    public static string MakeTypeKey(string dbId, Type t)
    {
        return dbId + "|" + t.AssemblyQualifiedName;
    }
}
