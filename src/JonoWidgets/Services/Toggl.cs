using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace JonoWidgets;

internal sealed class TogglEntry
{
    public long Id { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? Stop { get; set; }
    public bool Running => Stop == null;
}

/// <summary>
/// Toggl Track API v9. Toggl's free plan allows only ~30 calls an hour, so the Now widget keeps
/// its own state and calls Toggl only when you tap and every ~20 minutes while the board is open.
/// </summary>
internal static class Toggl
{
    private const string Base = "https://api.track.toggl.com/api/v9";
    private const string WorkspaceKey = "toggl_workspace";

    public static string? Token => Settings.Secret(Settings.TogglToken);

    private static AuthenticationHeaderValue Auth(string token) => Http.Basic(token, "api_token");

    private static string Iso(DateTimeOffset t) =>
        t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Your default workspace, looked up once and remembered.</summary>
    public static async Task<long> Workspace(string token)
    {
        if (long.TryParse(Settings.Get(WorkspaceKey), out var cached) && cached > 0) return cached;
        var me = await Http.Json(HttpMethod.Get, Base + "/me", Auth(token)).ConfigureAwait(false);
        var wid = me?["default_workspace_id"]?.GetValue<long>() ?? 0;
        if (wid > 0) Settings.Set(WorkspaceKey, wid.ToString());
        return wid;
    }

    public static void ForgetWorkspace() => Settings.Set(WorkspaceKey, null);

    /// <summary>Everything since local midnight (plus whatever was running then), oldest first.</summary>
    public static async Task<List<TogglEntry>> Today(string token)
    {
        var midnight = new DateTimeOffset(DateTime.Today);
        var from = Uri.EscapeDataString(Iso(midnight.AddHours(-12)));
        var to = Uri.EscapeDataString(Iso(midnight.AddDays(1)));
        var res = await Http.Json(HttpMethod.Get, $"{Base}/me/time_entries?start_date={from}&end_date={to}", Auth(token))
            .ConfigureAwait(false);
        return (res as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(Parse)
            .Where(e => e.Stop == null || e.Stop > midnight)
            .OrderBy(e => e.Start).ToList();
    }

    public static async Task<TogglEntry> Start(string token, long workspace, string description, DateTimeOffset start)
    {
        var body = new JsonObject
        {
            ["created_with"] = "jono-now-widget",
            ["description"] = description,
            ["workspace_id"] = workspace,
            ["start"] = Iso(start),
            ["duration"] = -1,
        };
        var res = await Http.Json(HttpMethod.Post, $"{Base}/workspaces/{workspace}/time_entries", Auth(token),
            Http.JsonBody(body)).ConfigureAwait(false);
        return res is JsonObject o ? Parse(o) : new TogglEntry { Description = description, Start = start };
    }

    public static Task Stop(string token, long workspace, long id) =>
        Http.Json(HttpMethod.Patch, $"{Base}/workspaces/{workspace}/time_entries/{id}/stop", Auth(token));

    public static Task Update(string token, long workspace, long id, JsonObject fields) =>
        Http.Json(HttpMethod.Put, $"{Base}/workspaces/{workspace}/time_entries/{id}", Auth(token), Http.JsonBody(fields));

    public static JsonObject StartField(DateTimeOffset start) => new() { ["start"] = Iso(start), ["duration"] = -1 };

    private static TogglEntry Parse(JsonObject o)
    {
        DateTimeOffset.TryParse(o["start"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start);
        DateTimeOffset? stop = null;
        var s = o["stop"]?.ToString();
        if (!string.IsNullOrEmpty(s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var st))
            stop = st;
        long duration = 0;
        if (o["duration"] is JsonValue dv) dv.TryGetValue(out duration);
        if (duration < 0) stop = null;
        long id = 0;
        if (o["id"] is JsonValue iv) iv.TryGetValue(out id);
        return new TogglEntry { Id = id, Description = o["description"]?.ToString() ?? "", Start = start, Stop = stop };
    }
}
