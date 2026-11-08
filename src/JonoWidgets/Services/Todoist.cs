using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JonoWidgets;

internal sealed record TodoTask(
    string Id,
    string Content,
    string Description,
    int Priority, // API value: 4 = p1 (urgent) … 1 = p4
    DateOnly? DueDate,
    TimeOnly? DueTime,
    bool Recurring,
    string? ProjectId,
    IReadOnlyList<string> Labels,
    DateOnly? Deadline,
    int Order);

internal sealed record TodoProject(string Id, string Name);

internal sealed record TodoFilter(string Id, string Name, string Query);

/// <summary>
/// Todoist API v1 with your personal token, the same calls the Android widget makes.
/// Also holds what all Todoist widgets share: project names and saved filters.
/// </summary>
internal static class Todoist
{
    private const string Base = "https://api.todoist.com/api/v1";
    private const int Page = 200;
    private const int MaxPages = 5;

    public static List<TodoProject> Projects { get; private set; } = new();
    public static List<TodoFilter> Filters { get; private set; } = new();
    public static DateTime MetaAt { get; private set; } = DateTime.MinValue;
    public static bool AuthError { get; set; }

    /// <summary>Tasks being completed right now: hidden in every widget until Todoist confirms.</summary>
    public static readonly HashSet<string> Hidden = new();

    public static string? Token => Settings.Secret(Settings.TodoistToken);

    public static Dictionary<string, string> ProjectNames() => Projects.ToDictionary(p => p.Id, p => p.Name);

    public static async Task LoadMeta(string token)
    {
        var res = await Http.Json(HttpMethod.Post, Base + "/sync", Http.Bearer(token),
            Http.Form(("sync_token", "*"), ("resource_types", "[\"projects\",\"filters\"]"))).ConfigureAwait(false);
        var projects = Objects(res?["projects"])
            .Where(o => !Bool(o, "is_deleted") && !Bool(o, "is_archived"))
            .OrderBy(o => Int(o, "child_order"))
            .Select(o => new TodoProject(Str(o, "id"), Str(o, "name")))
            .ToList();
        var filters = Objects(res?["filters"])
            .Where(o => !Bool(o, "is_deleted"))
            .OrderBy(o => Int(o, "item_order"))
            .Select(o => new TodoFilter(Str(o, "id"), Str(o, "name"), Str(o, "query")))
            .ToList();
        Projects = projects;
        Filters = filters;
        MetaAt = DateTime.UtcNow;
        AuthError = false;
    }

    public static Task<List<TodoTask>> ByQuery(string token, string query) =>
        Paged(token, "/tasks/filter", "query=" + Uri.EscapeDataString(query));

    public static Task<List<TodoTask>> ByProject(string token, string projectId) =>
        Paged(token, "/tasks", "project_id=" + Uri.EscapeDataString(projectId));

    public static Task Close(string token, string taskId) =>
        Http.Json(HttpMethod.Post, $"{Base}/tasks/{Uri.EscapeDataString(taskId)}/close", Http.Bearer(token));

    private static async Task<List<TodoTask>> Paged(string token, string path, string query)
    {
        var all = new List<TodoTask>();
        string? cursor = null;
        for (int i = 0; i < MaxPages; i++)
        {
            var url = $"{Base}{path}?{query}&limit={Page}" + (cursor == null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var res = await Http.Json(HttpMethod.Get, url, Http.Bearer(token)).ConfigureAwait(false);
            all.AddRange(Objects(res?["results"]).Select(Parse));
            cursor = res?["next_cursor"]?.ToString();
            if (string.IsNullOrEmpty(cursor) || cursor == "null") break;
        }
        return all;
    }

    private static TodoTask Parse(JsonObject o)
    {
        var due = o["due"] as JsonObject;
        // "date" is yyyy-MM-dd, or a date-time (floating "…T14:30:00", or fixed "…Z").
        var raw = Str(due, "datetime");
        if (raw.Length == 0) raw = Str(due, "date");
        DateOnly? date = null;
        TimeOnly? time = null;
        if (raw.Length > 0)
        {
            if (raw.Length <= 10)
            {
                if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) date = d;
            }
            else if (raw.EndsWith("Z") || raw.Length > 19)
            {
                if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                {
                    var local = dto.ToLocalTime().DateTime;
                    date = DateOnly.FromDateTime(local);
                    time = TimeOnly.FromDateTime(local);
                }
            }
            else if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var floating))
            {
                date = DateOnly.FromDateTime(floating);
                time = TimeOnly.FromDateTime(floating);
            }
        }
        DateOnly? deadline = null;
        var dl = Str(o["deadline"] as JsonObject, "date");
        if (dl.Length >= 10 && DateOnly.TryParseExact(dl[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd))
            deadline = dd;

        var labels = (o["labels"] as JsonArray)?.Select(n => n?.ToString() ?? "").Where(s => s.Length > 0).ToList()
            ?? new List<string>();
        var description = Clean(Str(o, "description")).Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
        var pid = Str(o, "project_id");
        return new TodoTask(
            Str(o, "id"),
            Clean(Str(o, "content")),
            description.Trim(),
            Math.Clamp(Int(o, "priority", 1), 1, 4),
            date,
            time,
            due != null && Bool(due, "is_recurring"),
            pid.Length > 0 ? pid : null,
            labels,
            deadline,
            Int(o, "child_order", Int(o, "order")));
    }

    /// <summary>Todoist text is markdown: keep link text, drop emphasis marks.</summary>
    private static string Clean(string s)
    {
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");
        return s.Replace("**", "").Replace("__", "").Replace("`", "").Trim();
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? n) =>
        (n as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();

    private static string Str(JsonObject? o, string key)
    {
        var v = o?[key];
        if (v == null) return "";
        var s = v.ToString();
        return s == "null" ? "" : s;
    }

    private static bool Bool(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static int Int(JsonObject o, string key, int fallback = 0)
    {
        if (o[key] is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<string>(out var s) && int.TryParse(s, out i)) return i;
        }
        return fallback;
    }
}
