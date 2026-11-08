using System.Text.Json.Nodes;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage;

namespace JonoWidgets;

/// <summary>
/// App-wide settings shared by all widgets: the API tokens (encrypted for your Windows account with
/// DPAPI) and small cached values. Per-widget choices live in each widget's own saved state instead.
/// </summary>
internal static class Settings
{
    public const string TodoistToken = "todoist_token";
    public const string GitHubToken = "github_token";
    public const string TogglToken = "toggl_token";

    private static readonly object Gate = new();
    private static JsonObject? cache;
    private static readonly Dictionary<string, string?> SecretCache = new();

    private static string FilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings.json");

    private static JsonObject Load()
    {
        if (cache != null) return cache;
        try
        {
            if (File.Exists(FilePath)) cache = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
        }
        catch (Exception e)
        {
            Log.Error("settings load", e);
        }
        return cache ??= new JsonObject();
    }

    private static void Save()
    {
        try { File.WriteAllText(FilePath, Load().ToJsonString()); }
        catch (Exception e) { Log.Error("settings save", e); }
    }

    public static string? Get(string key)
    {
        lock (Gate) return Load()[key]?.ToString();
    }

    public static void Set(string key, string? value)
    {
        lock (Gate)
        {
            if (value == null) Load().Remove(key);
            else Load()[key] = value;
            Save();
        }
    }

    public static string? Secret(string key)
    {
        lock (Gate)
        {
            if (SecretCache.TryGetValue(key, out var v)) return v;
            var stored = Load()[key]?.ToString();
            string? plain = null;
            if (!string.IsNullOrEmpty(stored))
            {
                try
                {
                    var buf = CryptographicBuffer.DecodeFromBase64String(stored);
                    var clear = new DataProtectionProvider().UnprotectAsync(buf).AsTask().GetAwaiter().GetResult();
                    plain = CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, clear);
                }
                catch (Exception e)
                {
                    Log.Error("unprotect " + key, e);
                }
            }
            SecretCache[key] = string.IsNullOrWhiteSpace(plain) ? null : plain;
            return SecretCache[key];
        }
    }

    public static void SetSecret(string key, string? value)
    {
        value = value?.Trim();
        lock (Gate)
        {
            if (string.IsNullOrEmpty(value))
            {
                Load().Remove(key);
                SecretCache[key] = null;
            }
            else
            {
                var buf = CryptographicBuffer.ConvertStringToBinary(value, BinaryStringEncoding.Utf8);
                var prot = new DataProtectionProvider("LOCAL=user").ProtectAsync(buf).AsTask().GetAwaiter().GetResult();
                Load()[key] = CryptographicBuffer.EncodeToBase64String(prot);
                SecretCache[key] = value;
            }
            Save();
        }
    }
}
