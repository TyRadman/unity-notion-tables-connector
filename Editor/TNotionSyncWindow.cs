using UnityEngine;
using System;
using UnityEditor;
using System.Collections.Generic;
using System.Text;

public sealed class TNotionSyncWindow : EditorWindow
{
    private readonly Dictionary<string, string> _dbTitleCache = new Dictionary<string, string>(64);
    private readonly Dictionary<string, TableEntry> _tablesById = new Dictionary<string, TableEntry>(64);
    private readonly Dictionary<string, bool> _selectedTypeKeys = new Dictionary<string, bool>(256);
    private readonly List<TableEntry> _tableEntries = new List<TableEntry>(64);

    private readonly StringBuilder _log = new StringBuilder(4096);
    private static GUIStyle _wrapLabelStyle;
    private static Texture2D _windowIcon;

    private Vector2 _scroll;
    private Vector2 _dbSelectScroll;
    private string _dbSearch = "";

    private const string DEFAULT_API = "API_GOES_HERE";
    private const string TitleColumnName = "Name";
    private const string PATH_TO_ICON = "NotionSyncer/T_Icon_Notion";
    private const string ICON_WARNING_DONT_SHOW_AGAIN_PREF_KEY = "NotionSyncer_DontShowAgain";


    [MenuItem("Tools/Notion Syncer")]
    private static void Open()
    {
        TNotionSyncWindow window = GetWindow<TNotionSyncWindow>();
        GUIContent content = new GUIContent("Notion Syncer");
        window.titleContent = content;
        content.image = GetWindowIcon();
        window.Show();
    }

    private static Texture2D GetWindowIcon()
    {
        if (_windowIcon == null)
        {
            _windowIcon = Resources.Load<Texture2D>(PATH_TO_ICON);
        }

        if (_windowIcon == null)
        {
            if (!EditorPrefs.HasKey(ICON_WARNING_DONT_SHOW_AGAIN_PREF_KEY))
            {
                EditorPrefs.SetBool(ICON_WARNING_DONT_SHOW_AGAIN_PREF_KEY, true);
            }

            if (EditorPrefs.GetBool(ICON_WARNING_DONT_SHOW_AGAIN_PREF_KEY))
            {
                bool value = EditorUtility.DisplayDialog(
                    "Notion Syncer", "No icon found at Assets/Resources/TNotionSyncer. Expecting an icon named T_Icon_Notion.png",
                    "Close", "Don't show again. Icons suck.");

                if (!value)
                {
                    EditorPrefs.SetBool(ICON_WARNING_DONT_SHOW_AGAIN_PREF_KEY, false);
                }
            }
        }

        return _windowIcon;
    }

    private string GetDatabaseTitleCached(string databaseId, string apiKey)
    {
        if (string.IsNullOrEmpty(databaseId) || string.IsNullOrEmpty(apiKey))
        {
            return null;
        }

        if (_dbTitleCache.TryGetValue(databaseId, out var cached))
        {
            return string.IsNullOrEmpty(cached) ? null : cached;
        }

        // Fetch once, then store (including negative cache)
        if (TNotionApiClient.TryFetchDatabaseTitle(databaseId, apiKey, out string title))
        {
            _dbTitleCache[databaseId] = title ?? "";
            return string.IsNullOrEmpty(title) ? null : title;
        }

        _dbTitleCache[databaseId] = "";
        return null;
    }

    private void SetAllSelection(bool value, string filter)
    {
        for (int i = 0; i < _tableEntries.Count; i++)
        {
            var t = _tableEntries[i];
            if (!t.MatchesMain(filter))
            {
                continue;
            }

            for (int c = 0; c < t.Types.Count; c++)
            {
                _selectedTypeKeys[TNotionUnityReflectionHelper.MakeTypeKey(t.DbId, t.Types[c].Db.Type)] = value;
            }
        }
    }

    private void OnGUI()
    {
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh Databases", GUILayout.Height(28))) RefreshDatabases();
        if (GUILayout.Button("Push", GUILayout.Height(28))) Push(null);
        if (GUILayout.Button("Pull", GUILayout.Height(28))) Pull(null);
        GUILayout.EndHorizontal();

        GUILayout.Space(6);

        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        _dbSearch = GUILayout.TextField(_dbSearch, GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarTextField);
        if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(22))) _dbSearch = "";
        GUILayout.EndHorizontal();

        GUILayout.Space(5);
        GUIStyle headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.fontSize = 18;
        headerStyle.fontStyle = FontStyle.Bold;
        GUILayout.Label("Connected Classes", headerStyle);
        GUILayout.Space(10);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Select all", GUILayout.Height(22))) SetAllSelection(true, _dbSearch);
        if (GUILayout.Button("Deselect all", GUILayout.Height(22))) SetAllSelection(false, _dbSearch);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _dbSelectScroll = EditorGUILayout.BeginScrollView(_dbSelectScroll, GUILayout.Height(220));
        DrawDbEntries();
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        GUILayout.Space(8);

        GUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Console Log", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.TextArea(_log.ToString(), GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        GUILayout.Space(6);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Clear log", GUILayout.Height(24), GUILayout.MaxWidth(200))) ClearLog();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
    }

    private void OnEnable()
    {
        RefreshDatabases();
    }

    private void RefreshDatabases()
    {
        _tablesById.Clear();
        _tableEntries.Clear();

        var dbs = TNotionUnityReflectionHelper.FindDatabases();

        for (int i = 0; i < dbs.Count; i++)
        {
            var db = dbs[i];
            string apiKey = string.IsNullOrEmpty(db.ApiKey) ? DEFAULT_API : db.ApiKey;

            string typeDbId = null;
            if (db.TryResolveDatabaseId(out string idFromAttr, out _))
                typeDbId = TNotionUnityReflectionHelper.NormalizeDbId(idFromAttr);

            var assets = TNotionUnityReflectionHelper.FindAssetsOfType(db.Type);
            if (assets.Count == 0)
                continue;

            if (!string.IsNullOrEmpty(typeDbId))
            {
                AddTypeToTable(typeDbId, apiKey, db, assets.Count);
            }
            else
            {
                // Per-asset TableID (instance-level). Same type may feed multiple tables.
                var countsById = new Dictionary<string, int>(8);

                for (int a = 0; a < assets.Count; a++)
                {
                    if (assets[a] is ITNotionSyncUniqueTable table)
                    {
                        string perAssetId = TNotionUnityReflectionHelper.NormalizeDbId(table.TableID);
                        if (string.IsNullOrEmpty(perAssetId)) continue;

                        countsById.TryGetValue(perAssetId, out int c);
                        countsById[perAssetId] = c + 1;
                    }
                }

                foreach (var kv in countsById)
                {
                    AddTypeToTable(kv.Key, apiKey, db, kv.Value);
                }
            }
        }

        // Resolve titles (cache by id) + build labels
        for (int i = 0; i < _tableEntries.Count; i++)
        {
            var t = _tableEntries[i];
            string title = GetDatabaseTitleCached(t.DbId, t.ApiKey);
            if (string.IsNullOrEmpty(title))
            {
                title = "(Unknown Table)";
            }

            t.Label = title + "\n" + t.DbId + $"  (Assets:{t.TotalAssets}, Types:{t.Types.Count})";

            for (int k = 0; k < t.Types.Count; k++)
            {
                var te = t.Types[k];
                te.Label = $"{te.Db.DisplayName}  (Assets:{te.AssetCount})";
            }
        }

        _tableEntries.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
    }

    private void AddTypeToTable(string dbId, string apiKey, DatabaseInfo db, int assetCount)
    {
        if (!_tablesById.TryGetValue(dbId, out var table))
        {
            table = new TableEntry
            {
                DbId = dbId,
                ApiKey = apiKey
            };

            _tablesById.Add(dbId, table);
            _tableEntries.Add(table);
        }

        if (string.IsNullOrEmpty(table.ApiKey) && !string.IsNullOrEmpty(apiKey))
            table.ApiKey = apiKey;

        table.TotalAssets += assetCount;

        // one child per type under this table
        for (int i = 0; i < table.Types.Count; i++)
        {
            if (table.Types[i].Db.Type == db.Type)
            {
                table.Types[i].AssetCount += assetCount;
                EnsureSelectionKey(dbId, db.Type);
                return;
            }
        }

        table.Types.Add(new TypeEntry { Db = db, AssetCount = assetCount });
        EnsureSelectionKey(dbId, db.Type);
    }

    private void EnsureSelectionKey(string dbId, Type type)
    {
        string key = TNotionUnityReflectionHelper.MakeTypeKey(dbId, type);
        if (!_selectedTypeKeys.ContainsKey(key))
            _selectedTypeKeys[key] = true;
    }

    private void DrawDbEntries()
    {
        _wrapLabelStyle ??= new GUIStyle(EditorStyles.label) { wordWrap = true };

        int shown = 0;
        int selectedChildren = 0;

        for (int i = 0; i < _tableEntries.Count; i++)
        {
            var t = _tableEntries[i];

            if (!t.MatchesMain(_dbSearch))
                continue; // search filters only main entries

            shown++;

            // Parent checkbox state derived from children (mixed supported)
            bool all = true;
            bool none = true;

            for (int c = 0; c < t.Types.Count; c++)
            {
                bool sel = IsTypeSelected(t.DbId, t.Types[c].Db.Type);
                all &= sel;
                none &= !sel;
            }

            bool mixed = !all && !none;

            // Parent row
            bool parentNext;
            using (new EditorGUI.IndentLevelScope(0))
            {
                parentNext = DrawCheckboxRow(t.Label, all, mixed);
            }

            if (parentNext != all)
            {
                // Toggle all children
                for (int c = 0; c < t.Types.Count; c++)
                    _selectedTypeKeys[TNotionUnityReflectionHelper.MakeTypeKey(t.DbId, t.Types[c].Db.Type)] = parentNext;
            }

            // Children rows (never filtered by search)
            using (new EditorGUI.IndentLevelScope(1))
            {
                for (int c = 0; c < t.Types.Count; c++)
                {
                    var child = t.Types[c];
                    string key = TNotionUnityReflectionHelper.MakeTypeKey(t.DbId, child.Db.Type);

                    bool cur = _selectedTypeKeys.TryGetValue(key, out bool v) && v;
                    bool next = DrawCheckboxRow(child.Label, cur, mixed: false);

                    if (next != cur)
                        _selectedTypeKeys[key] = next;

                    if (next)
                        selectedChildren++;
                }
            }

            GUILayout.Space(4);
        }

        GUILayout.Space(4);
        EditorGUILayout.LabelField($"Shown tables: {shown}   Selected classes: {selectedChildren}", EditorStyles.miniLabel);
    }

    private bool DrawCheckboxRow(string label, bool cur, bool mixed)
    {
        var content = new GUIContent(label);

        float labelWidth = EditorGUIUtility.currentViewWidth - 70f;
        float h = _wrapLabelStyle.CalcHeight(content, labelWidth);
        h = Mathf.Max(h, EditorGUIUtility.singleLineHeight);

        Rect row = EditorGUILayout.GetControlRect(false, h);

        Rect toggleRect = new Rect(row.x + EditorGUI.indentLevel * 15f, row.y, 18f, EditorGUIUtility.singleLineHeight);
        Rect labelRect = new Rect(toggleRect.x + 22f, row.y, row.width - (toggleRect.x - row.x) - 22f, h);

        bool next;
        bool prevMixed = EditorGUI.showMixedValue;
        EditorGUI.showMixedValue = mixed;
        next = EditorGUI.Toggle(toggleRect, cur);
        EditorGUI.showMixedValue = prevMixed;

        EditorGUI.LabelField(labelRect, content, _wrapLabelStyle);

        // click row toggles (except checkbox itself)
        var evt = Event.current;
        if (evt.type == EventType.MouseDown && evt.button == 0 && row.Contains(evt.mousePosition) && !toggleRect.Contains(evt.mousePosition))
        {
            next = !cur;
            evt.Use();
            GUI.changed = true;
        }

        return next;
    }

    private void Push(HashSet<string> allowedDbIds)
    {
        ClearLog();

        var dbs = TNotionUnityReflectionHelper.FindDatabases();
        if (dbs.Count == 0)
        {
            Log("[Info] No ScriptableObject types found with [NotionDatabase].");
            return;
        }

        foreach (var db in dbs)
        {
            string typeDatabaseId = null;
            if (db.TryResolveDatabaseId(out string idFromAttr, out _))
                typeDatabaseId = TNotionUnityReflectionHelper.NormalizeDbId(idFromAttr);

            if (!string.IsNullOrEmpty(typeDatabaseId) && !IsTypeSelected(typeDatabaseId, db.Type))
                continue;

            // If attribute dbId exists and isn't selected -> skip whole type
            if (!string.IsNullOrEmpty(typeDatabaseId) && allowedDbIds != null && !allowedDbIds.Contains(typeDatabaseId))
                continue;

            var assets = TNotionUnityReflectionHelper.FindAssetsOfType(db.Type);
            if (assets.Count == 0)
                continue;

            var assetsByDbId = new Dictionary<string, List<ScriptableObject>>(8);

            for (int i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];

                string resolvedId = typeDatabaseId;

                if (string.IsNullOrEmpty(resolvedId))
                {
                    if (asset is ITNotionSyncUniqueTable table)
                        resolvedId = TNotionUnityReflectionHelper.NormalizeDbId(table.TableID);
                }

                bool skipAsset = string.IsNullOrEmpty(resolvedId) || allowedDbIds != null && !allowedDbIds.Contains(resolvedId) || !IsTypeSelected(resolvedId, db.Type);
                if (skipAsset)
                {
                    continue;
                }

                if (!assetsByDbId.TryGetValue(resolvedId, out var list))
                {
                    list = new List<ScriptableObject>(16);

                    assetsByDbId.Add(resolvedId, list);
                }

                list.Add(asset);
            }

            foreach (var kv in assetsByDbId)
            {
                string databaseId = kv.Key;
                List<ScriptableObject> groupAssets = kv.Value;

                var schema = TNotionApiClient.GetDatabaseSchema(databaseId, db.ApiKey, db.DisplayName);
                if (schema == null)
                    continue;

                if (!schema.Properties.TryGetValue(TitleColumnName, out string titleType) || titleType != "title")
                {
                    Log($"[Error] DB '{db.DisplayName}' ({databaseId}) must have a Title column named '{TitleColumnName}'.");
                    continue;
                }

                var pages = TNotionApiClient.QueryAllPages(databaseId, db.ApiKey, db.DisplayName);
                if (pages == null)
                    continue;

                var existing = new Dictionary<string, string>(pages.Count);
                for (int p = 0; p < pages.Count; p++)
                {
                    var page = pages[p];
                    if (!string.IsNullOrEmpty(page.Key) && !string.IsNullOrEmpty(page.PageId) && !existing.ContainsKey(page.Key))
                        existing[page.Key] = page.PageId;
                }

                Log($"[DB] {db.DisplayName} | Table:{databaseId} | Pages:{existing.Count} | Assets:{groupAssets.Count}");

                for (int a = 0; a < groupAssets.Count; a++)
                {
                    var asset = groupAssets[a];
                    string entryKey = $"{asset.name}_{asset.GetInstanceID()}";

                    var payload = new Dictionary<string, object>(db.Properties.Count);

                    for (int pi = 0; pi < db.Properties.Count; pi++)
                    {
                        var prop = db.Properties[pi];

                        if (!schema.Properties.ContainsKey(prop.NotionName))
                            continue;

                        if (!prop.TryGet(asset, out object val))
                            continue;

                        if (!TNotionUnityReflectionHelper.TryNormalizeValueForNotion(val, prop.FieldType, out object normalized))
                            continue;

                        payload[prop.NotionName] = normalized;
                    }

                    if (existing.TryGetValue(entryKey, out string pageId))
                    {
                        if (TNotionApiClient.UpdatePage(pageId, db.ApiKey, payload, out string err))
                            Log($"[Update] {entryKey}");
                        else
                            Log($"[Error] Update failed {entryKey} | {err}");
                    }
                    else
                    {
                        if (TNotionApiClient.CreatePage(databaseId, db.ApiKey, entryKey, payload, out string err))
                            Log($"[Create] {entryKey}");
                        else
                            Log($"[Error] Create failed {entryKey} | {err}");
                    }
                }
            }
        }
    }

    private void Pull(HashSet<string> allowedDbIds)
    {
        ClearLog();

        var dbs = TNotionUnityReflectionHelper.FindDatabases();
        if (dbs.Count == 0)
        {
            Log("[Info] No ScriptableObject types found with [NotionDatabase].");
            return;
        }

        foreach (var db in dbs)
        {
            string typeDatabaseId = null;
            if (db.TryResolveDatabaseId(out string idFromAttr, out _))
                typeDatabaseId = TNotionUnityReflectionHelper.NormalizeDbId(idFromAttr);

            if (!string.IsNullOrEmpty(typeDatabaseId) && !IsTypeSelected(typeDatabaseId, db.Type))
                continue;

            if (!string.IsNullOrEmpty(typeDatabaseId) && allowedDbIds != null && !allowedDbIds.Contains(typeDatabaseId))
                continue;

            var assets = TNotionUnityReflectionHelper.FindAssetsOfType(db.Type);
            if (assets.Count == 0)
            {
                continue;
            }

            var assetsByDbId = new Dictionary<string, List<ScriptableObject>>(8);

            for (int i = 0; i < assets.Count; i++)
            {
                var asset = assets[i];
                string resolvedId = typeDatabaseId;
                if (string.IsNullOrEmpty(resolvedId))
                {
                    if (asset is ITNotionSyncUniqueTable table)
                    {
                        resolvedId = TNotionUnityReflectionHelper.NormalizeDbId(table.TableID);
                    }
                }

                bool skipAsset = string.IsNullOrEmpty(resolvedId) || allowedDbIds != null && !allowedDbIds.Contains(resolvedId) || !IsTypeSelected(resolvedId, db.Type);
                if (skipAsset)
                { 
                    continue;
                }

                if (!assetsByDbId.TryGetValue(resolvedId, out var list))
                {
                    list = new List<ScriptableObject>(16);
                    assetsByDbId.Add(resolvedId, list);
                }

                list.Add(asset);
            }

            foreach (var kv in assetsByDbId)
            {
                string databaseId = kv.Key;
                List<ScriptableObject> groupAssets = kv.Value;

                var assetsByKey = new Dictionary<string, ScriptableObject>(groupAssets.Count);
                for (int i = 0; i < groupAssets.Count; i++)
                {
                    var a = groupAssets[i];
                    assetsByKey[$"{a.name}_{a.GetInstanceID()}"] = a;
                }

                var schema = TNotionApiClient.GetDatabaseSchema(databaseId, db.ApiKey, db.DisplayName);
                if (schema == null)
                    continue;

                if (!schema.Properties.TryGetValue(TitleColumnName, out string titleType) || titleType != "title")
                {
                    Log($"[Error] DB '{db.DisplayName}' ({databaseId}) must have a Title column named '{TitleColumnName}'.");
                    continue;
                }

                var pages = TNotionApiClient.QueryAllPages(databaseId, db.ApiKey, db.DisplayName);
                if (pages == null)
                    continue;

                Log($"[DB] {db.DisplayName} | Table:{databaseId} | Pages:{pages.Count} | Assets:{groupAssets.Count}");

                int applied = 0;

                for (int i = 0; i < pages.Count; i++)
                {
                    var page = pages[i];
                    if (string.IsNullOrEmpty(page.Key))
                        continue;

                    if (!assetsByKey.TryGetValue(page.Key, out var asset))
                    {
                        Log($"[MissingAsset] {page.Key}");
                        continue;
                    }

                    bool anySet = false;

                    for (int pi = 0; pi < db.Properties.Count; pi++)
                    {
                        var prop = db.Properties[pi];

                        if (!page.Properties.TryGetValue(prop.NotionName, out object notionValue))
                            continue;

                        if (notionValue == null)
                            continue;

                        if (!TNotionUnityReflectionHelper.TryConvertNotionValueToField(notionValue, prop.FieldType, out object fieldValue))
                            continue;

                        prop.Set(asset, fieldValue);
                        anySet = true;
                    }

                    if (anySet)
                    {
                        EditorUtility.SetDirty(asset);
                        applied++;
                    }
                }

                AssetDatabase.SaveAssets();
                Log($"[PullDone] {db.DisplayName} | Table:{databaseId} Applied:{applied}");
            }
        }
    }

    private void Log(string line)
    {
        _log.AppendLine(line);
        Repaint();
    }

    private void ClearLog()
    {
        _log.Length = 0;
        Repaint();
    }

    private bool IsTypeSelected(string dbId, Type t)
    {
        return _selectedTypeKeys.TryGetValue(TNotionUnityReflectionHelper.MakeTypeKey(dbId, t), out bool v) && v;
    }
}
