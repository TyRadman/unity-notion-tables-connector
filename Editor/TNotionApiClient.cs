using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using UnityEngine;

public static class TNotionApiClient
{
    private static readonly HttpClient Http = new HttpClient(
        new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
    private const string NotionVersion = "2022-06-28";
    private const string TitleColumnName = "Name";

    public static void ApplyHeaders(HttpRequestMessage req, string apiKey)
    {
        req.Headers.Clear();
        req.Headers.Add("Authorization", $"Bearer {apiKey}");
        req.Headers.Add("Notion-Version", NotionVersion);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
    }

    public static bool Send(HttpRequestMessage req, out string responseBody, out string error)
    {
        responseBody = null;
        error = null;

        HttpResponseMessage res;
        try
        {
            res = Http.SendAsync(req).Result;
        }
        catch (Exception e)
        {
            error = e.GetType().Name + ": " + e.Message;
            return false;
        }

        responseBody = res.Content.ReadAsStringAsync().Result;

        if (!res.IsSuccessStatusCode)
        {
            error = ((int)res.StatusCode) + " " + res.ReasonPhrase + " | " + Truncate(responseBody, 400);
            return false;
        }

        return true;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length <= max) return s;
        return s.Substring(0, max);
    }

    private static string StripBom(string s)
    {
        if (!string.IsNullOrEmpty(s) && s.Length > 0 && s[0] == '\uFEFF')
            return s.Substring(1);
        return s;
    }

    public static bool TryFetchDatabaseTitle(string databaseId, string apiKey, out string title)
    {
        title = null;

        var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.notion.com/v1/databases/{databaseId}");
        ApplyHeaders(req, apiKey);

        if (!Send(req, out string body, out _))
            return false;

        body = StripBom(body);

        var root = MiniJSON.Deserialize(body) as Dictionary<string, object>;
        if (root == null)
        {
            return false;
        }

        if (!root.TryGetValue("title", out object titleObj) || titleObj is not List<object> titleArr)
            return false;

        // Concatenate plain_text pieces
        var sb = new StringBuilder(64);
        for (int i = 0; i < titleArr.Count; i++)
        {
            if (titleArr[i] is not Dictionary<string, object> part) continue;
            if (part.TryGetValue("plain_text", out object pt) && pt is string s && !string.IsNullOrEmpty(s))
                sb.Append(s);
        }

        title = sb.ToString();
        return true;
    }

    public static NotionDatabaseSchema GetDatabaseSchema(string databaseId, string apiKey, string displayName)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.notion.com/v1/databases/{databaseId}");
        ApplyHeaders(req, apiKey);

        if (!Send(req, out string body, out string err))
        {
            Debug.LogError($"[Error] DB schema fetch failed '{displayName}' | {err}");
            return null;
        }

        body = StripBom(body);

        var root = MiniJSON.Deserialize(body) as Dictionary<string, object>;
        if (root == null)
        {
            Debug.LogError($"[Error] DB schema parse failed '{displayName}'. BodyHead: {Truncate(body, 300)}");
            return null;
        }

        if (!root.TryGetValue("properties", out object propsObj) || propsObj is not Dictionary<string, object> propsDict)
        {
            Debug.LogError($"[Error] DB schema missing properties '{displayName}'.");
            return null;
        }

        var schema = new NotionDatabaseSchema();
        foreach (var kv in propsDict)
        {
            if (kv.Value is not Dictionary<string, object> def) continue;
            if (!def.TryGetValue("type", out object typeObj)) continue;
            schema.Properties[kv.Key] = typeObj as string ?? string.Empty;
        }

        return schema;
    }

    public static List<NotionPage> QueryAllPages(string databaseId, string apiKey, string displayName)
    {
        var pages = new List<NotionPage>(256);

        string startCursor = null;
        int safety = 0;

        while (true)
        {
            safety++;
            if (safety > 200)
            {
                Debug.LogError($"[Error] Pagination safety hit '{displayName}'.");
                return pages;
            }

            string jsonBody = BuildQueryJson(startCursor);

            var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.notion.com/v1/databases/{databaseId}/query");
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            ApplyHeaders(req, apiKey);

            if (!Send(req, out string body, out string err))
            {
                Debug.LogError($"[Error] DB query failed '{displayName}' | {err}");
                return null;
            }

            body = StripBom(body);

            var chunk = ParseQueryResults(body, out bool hasMore, out string nextCursor);
            if (chunk == null)
            {
                Debug.LogError($"[Error] DB query parse failed '{displayName}'. BodyHead: {Truncate(body, 300)}");
                return null;
            }

            pages.AddRange(chunk);

            if (!hasMore || string.IsNullOrEmpty(nextCursor))
                break;

            startCursor = nextCursor;
        }

        return pages;
    }

    private static List<NotionPage> ParseQueryResults(string json, out bool hasMore, out string nextCursor)
    {
        hasMore = false;
        nextCursor = null;

        var root = MiniJSON.Deserialize(json) as Dictionary<string, object>;
        if (root == null) return null;

        if (root.TryGetValue("has_more", out object hm) && hm is bool b) hasMore = b;
        if (root.TryGetValue("next_cursor", out object nc)) nextCursor = nc as string;

        if (!root.TryGetValue("results", out object resultsObj) || resultsObj is not List<object> results)
            return new List<NotionPage>();

        var list = new List<NotionPage>(results.Count);

        for (int i = 0; i < results.Count; i++)
        {
            if (results[i] is not Dictionary<string, object> pageDict) continue;

            string pageId = pageDict.TryGetValue("id", out object idObj) ? (idObj as string) : null;

            if (!pageDict.TryGetValue("properties", out object propsObj) || propsObj is not Dictionary<string, object> propsDict)
                continue;

            string key = ExtractTitleKey(propsDict);
            var props = ExtractSimpleProperties(propsDict);

            list.Add(new NotionPage
            {
                PageId = pageId,
                Key = key,
                Properties = props
            });
        }

        return list;
    }

    private static string BuildQueryJson(string startCursor)
    {
        if (string.IsNullOrEmpty(startCursor))
            return "{\"page_size\":100}";

        return "{\"page_size\":100,\"start_cursor\":\"" + EscapeJson(startCursor) + "\"}";
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;

        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '\"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 32) sb.Append(' ');
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    private static string ExtractTitleKey(Dictionary<string, object> propsDict)
    {
        if (!propsDict.TryGetValue(TitleColumnName, out object titleObj) || titleObj is not Dictionary<string, object> titleProp)
            return null;

        if (!titleProp.TryGetValue("title", out object titleArrObj) || titleArrObj is not List<object> titleArr || titleArr.Count == 0)
            return null;

        if (titleArr[0] is not Dictionary<string, object> first) return null;

        if (first.TryGetValue("plain_text", out object pt))
            return pt as string;

        return null;
    }

    private static Dictionary<string, object> ExtractSimpleProperties(Dictionary<string, object> propsDict)
    {
        var result = new Dictionary<string, object>(propsDict.Count);

        foreach (var kv in propsDict)
        {
            if (kv.Key == TitleColumnName) continue;
            if (kv.Value is not Dictionary<string, object> prop) continue;

            if (!prop.TryGetValue("type", out object typeObj)) continue;
            string type = typeObj as string;

            object value = null;

            switch (type)
            {
                case "number":
                    if (prop.TryGetValue("number", out object num))
                        value = num; // long or double or null
                    break;

                case "checkbox":
                    if (prop.TryGetValue("checkbox", out object chk))
                        value = chk; // bool
                    break;

                case "rich_text":
                    value = ExtractFirstPlainText(prop, "rich_text");
                    break;

                case "title":
                    value = ExtractFirstPlainText(prop, "title");
                    break;
            }

            result[kv.Key] = value;
        }

        return result;
    }

    private static string ExtractFirstPlainText(Dictionary<string, object> prop, string arrayKey)
    {
        if (!prop.TryGetValue(arrayKey, out object arrObj) || arrObj is not List<object> arr || arr.Count == 0)
            return string.Empty;

        if (arr[0] is not Dictionary<string, object> first)
            return string.Empty;

        if (first.TryGetValue("plain_text", out object pt) && pt is string s)
            return s;

        return string.Empty;
    }

    private static void AppendPropertiesJson(StringBuilder sb, Dictionary<string, object> props, bool includeLeadingComma)
    {
        bool first = !includeLeadingComma;

        foreach (var kv in props)
        {
            if (string.IsNullOrEmpty(kv.Key))
                continue;

            if (first)
                first = false;
            else
                sb.Append(",");

            sb.Append("\"").Append(EscapeJson(kv.Key)).Append("\":");

            if (kv.Value is bool b)
            {
                sb.Append("{\"checkbox\":").Append(b ? "true" : "false").Append("}");
            }
            else if (kv.Value is string s)
            {
                sb.Append("{\"rich_text\":[{\"text\":{\"content\":\"").Append(EscapeJson(s)).Append("\"}}]}");
            }
            else
            {
                sb.Append("{\"number\":").Append(FormatNumber(kv.Value)).Append("}");
            }
        }
    }

    /// <summary>
    /// Tries to convert the object to a numeric value if possible, if not, it returns null.
    /// </summary>
    /// <param name="v"></param>
    /// <returns></returns>
    private static string FormatNumber(object v)
    {
        if (v == null) return "null";

        if (v is int i) return i.ToString(CultureInfo.InvariantCulture);
        if (v is float f) return f.ToString(CultureInfo.InvariantCulture);
        if (v is double d) return d.ToString(CultureInfo.InvariantCulture);
        if (v is long l) return l.ToString(CultureInfo.InvariantCulture);

        // if the value isn't either of the 4, but is still a number somehow like "4,5", "1e-3", we convert it to a double just because doubles are generally easier.
        if (v is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsed))
            return parsed.ToString(CultureInfo.InvariantCulture);

        return "null";
    }

    private static string BuildCreatePageJson(string databaseId, string entryKey, Dictionary<string, object> props)
    {
        var sb = new StringBuilder(512);
        sb.Append("{\"parent\":{\"database_id\":\"").Append(EscapeJson(databaseId)).Append("\"},\"properties\":{");

        sb.Append("\"").Append(TitleColumnName).Append("\":{\"title\":[{\"text\":{\"content\":\"")
          .Append(EscapeJson(entryKey))
          .Append("\"}}]}");

        AppendPropertiesJson(sb, props, includeLeadingComma: true);

        sb.Append("}}");
        return sb.ToString();
    }

    private static string BuildUpdatePageJson(Dictionary<string, object> props)
    {
        var sb = new StringBuilder(512);
        sb.Append("{\"properties\":{");
        AppendPropertiesJson(sb, props, includeLeadingComma: false);
        sb.Append("}}");
        return sb.ToString();
    }

    public static bool CreatePage(string databaseId, string apiKey, string entryKey, Dictionary<string, object> props, out string error)
    {
        error = null;
        string json = BuildCreatePageJson(databaseId, entryKey, props);
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.notion.com/v1/pages");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        ApplyHeaders(req, apiKey);

        return Send(req, out _, out error);
    }

    public static bool UpdatePage(string pageId, string apiKey, Dictionary<string, object> props, out string error)
    {
        error = null;
        string json = BuildUpdatePageJson(props);
        var req = new HttpRequestMessage(new HttpMethod("PATCH"), $"https://api.notion.com/v1/pages/{pageId}");
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        ApplyHeaders(req, apiKey);

        return Send(req, out _, out error);
    }
}
