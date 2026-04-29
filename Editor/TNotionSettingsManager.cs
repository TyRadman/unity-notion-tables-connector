using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Manages encrypted storage of the Notion API token.
/// The token is AES-256 encrypted with a machine-specific key and stored
/// in <c>UserSettings/NotionSyncSettings.json</c> (gitignored by default).
/// </summary>
public static class TNotionSettingsManager
{
    private const string SettingsDir = "UserSettings";
    private const string SettingsFile = "NotionSyncSettings.json";

    private static string _cachedToken;
    private static bool _loaded;

    public static bool HasApiToken()
    {
        return !string.IsNullOrEmpty(GetApiToken());
    }

    public static string GetApiToken()
    {
        if (_loaded)
            return _cachedToken;

        _loaded = true;
        _cachedToken = LoadAndDecrypt();
        return _cachedToken;
    }

    public static void SetApiToken(string token)
    {
        _cachedToken = token;
        _loaded = true;
        EncryptAndSave(token);
    }

    public static void ClearApiToken()
    {
        _cachedToken = null;
        _loaded = true;

        string path = Path.Combine(SettingsDir, SettingsFile);
        if (File.Exists(path))
            File.Delete(path);
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(SettingsDir, SettingsFile);
    }

    private static byte[] DeriveKey()
    {
        string seed = Environment.MachineName + "_" + SystemInfo.deviceUniqueIdentifier + "_NotionSync";
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
    }

    private static void EncryptAndSave(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return;

        byte[] key = DeriveKey();

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
        byte[] cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var data = new SettingsData
        {
            encryptedApiToken = Convert.ToBase64String(cipherBytes),
            iv = Convert.ToBase64String(aes.IV)
        };

        string json = JsonUtility.ToJson(data, true);

        if (!Directory.Exists(SettingsDir))
            Directory.CreateDirectory(SettingsDir);

        File.WriteAllText(GetSettingsPath(), json, Encoding.UTF8);
    }

    private static string LoadAndDecrypt()
    {
        string path = GetSettingsPath();

        if (!File.Exists(path))
            return null;

        string json;
        try
        {
            json = File.ReadAllText(path, Encoding.UTF8);
        }
        catch
        {
            return null;
        }

        var data = JsonUtility.FromJson<SettingsData>(json);
        if (data == null || string.IsNullOrEmpty(data.encryptedApiToken) || string.IsNullOrEmpty(data.iv))
            return null;

        try
        {
            byte[] key = DeriveKey();
            byte[] iv = Convert.FromBase64String(data.iv);
            byte[] cipherBytes = Convert.FromBase64String(data.encryptedApiToken);

            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            Debug.LogWarning("[NotionSync] Failed to decrypt API token. It may have been saved on a different machine.");
            return null;
        }
    }

    [Serializable]
    private class SettingsData
    {
        public string encryptedApiToken;
        public string iv;
    }
}
