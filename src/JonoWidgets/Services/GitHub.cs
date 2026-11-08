using System.Text.Json.Nodes;

namespace JonoWidgets;

internal sealed record Run(string Status, string? Conclusion, int Number, string? Message, string? Url);

internal sealed record Repo(
    string FullName,
    string Name,
    string HtmlUrl,
    string? Description,
    DateTimeOffset PushedAt,
    bool Private,
    bool Archived)
{
    public Run? Run { get; init; }
    public string? LastCommit { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.MinValue;
}

/// <summary>The few read-only GitHub calls the Repos widget needs, with your token.</summary>
internal static class GitHub
{
    private const string Base = "https://api.github.com";

    /// <summary>Only the newest repos get the extra per-repo calls.</summary>
    private const int Detailed = 12;

    public static string? Token => Settings.Secret(Settings.GitHubToken);

    public static List<Repo> Repos { get; private set; } = new();
    public static string? Login { get; private set; }
    public static DateTime FetchedAt { get; private set; } = DateTime.MinValue;

    private static Task<JsonNode?> Get(string token, string path) =>
        Http.Json(HttpMethod.Get, Base + path, Http.Bearer(token),
            headers: h => h.Add("X-GitHub-Api-Version", "2022-11-28"));

    public static async Task<string> CheckToken(string token) =>
        (await Get(token, "/user").ConfigureAwait(false))?["login"]?.ToString() ?? "";

    public static async Task Refresh(string token, bool includeOthers)
    {
        Login ??= await CheckToken(token).ConfigureAwait(false);
        var affiliation = includeOthers ? "owner,collaborator,organization_member" : "owner";
        var arr = await Get(token, $"/user/repos?sort=pushed&direction=desc&per_page=40&affiliation={affiliation}")
            .ConfigureAwait(false) as JsonArray;
        var list = (arr ?? new JsonArray()).OfType<JsonObject>().Select(Parse)
            .OrderByDescending(r => r.PushedAt).ToList();
        var previous = Repos.ToDictionary(r => r.FullName);

        var jobs = list.Select((r, i) => Details(token, r, previous.GetValueOrDefault(r.FullName), i < Detailed));
        Repos = (await Task.WhenAll(jobs).ConfigureAwait(false)).ToList();
        FetchedAt = DateTime.UtcNow;
    }

    private static Repo Parse(JsonObject j)
    {
        DateTimeOffset.TryParse(j["pushed_at"]?.ToString(), out var pushed);
        return new Repo(
            j["full_name"]?.ToString() ?? "",
            j["name"]?.ToString() ?? "",
            j["html_url"]?.ToString() ?? "",
            j["description"]?.ToString(),
            pushed,
            j["private"]?.GetValue<bool>() ?? false,
            j["archived"]?.GetValue<bool>() ?? false);
    }

    private static async Task<Repo> Details(string token, Repo repo, Repo? prev, bool detailed)
    {
        // Nothing new since a settled look (finished CI, checked well after the push): reuse it.
        bool settled = prev != null && prev.CheckedAt > DateTime.MinValue && prev.PushedAt == repo.PushedAt &&
                       prev.CheckedAt - repo.PushedAt.UtcDateTime > TimeSpan.FromMinutes(10) &&
                       (prev.Run == null || prev.Run.Status == "completed");
        if (settled) return repo with { Run = prev!.Run, LastCommit = prev.LastCommit, CheckedAt = prev.CheckedAt };
        if (!detailed)
            return prev == null ? repo : repo with { Run = prev.Run, LastCommit = prev.LastCommit, CheckedAt = prev.CheckedAt };

        Run? run = null;
        string? commit = null;
        try
        {
            var runs = (await Get(token, $"/repos/{repo.FullName}/actions/runs?per_page=1").ConfigureAwait(false))?["workflow_runs"] as JsonArray;
            if (runs?.FirstOrDefault() is JsonObject r)
            {
                var msg = (r["head_commit"] as JsonObject)?["message"]?.ToString() ?? r["display_title"]?.ToString();
                run = new Run(
                    r["status"]?.ToString() ?? "",
                    r["conclusion"]?.ToString(),
                    r["run_number"]?.GetValue<int>() ?? 0,
                    FirstLine(msg),
                    r["html_url"]?.ToString());
            }
        }
        catch (HttpError)
        {
            // No Actions access, or no Actions: fall through to the commit.
        }
        if (run == null)
        {
            try
            {
                var commits = await Get(token, $"/repos/{repo.FullName}/commits?per_page=1").ConfigureAwait(false) as JsonArray;
                commit = FirstLine(((commits?.FirstOrDefault() as JsonObject)?["commit"] as JsonObject)?["message"]?.ToString());
            }
            catch (HttpError)
            {
                // Empty repo (409) or no access.
            }
        }
        return repo with { Run = run, LastCommit = commit, CheckedAt = DateTime.UtcNow };
    }

    private static string? FirstLine(string? s) => s?.Split('\n').FirstOrDefault()?.Trim();
}
