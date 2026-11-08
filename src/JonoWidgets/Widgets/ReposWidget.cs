using System.Text.Json.Nodes;

namespace JonoWidgets.Widgets;

/// <summary>Your GitHub repos, most recently pushed first, with each one's latest CI run.</summary>
internal sealed class ReposWidget : WidgetBase
{
    public const string Definition = "Repos";
    public override string DefinitionId => Definition;

    private string? error;
    private bool authError;

    public ReposWidget(string id, string state) : base(id, state) { }

    // Faster while a build is running, so you see it finish.
    protected override TimeSpan RefreshEvery =>
        GitHub.Repos.Take(5).Any(r => r.Run != null && r.Run.Status != "completed")
            ? TimeSpan.FromMinutes(1)
            : TimeSpan.FromMinutes(5);

    protected override TimeSpan StaleAfter => TimeSpan.FromMinutes(1);

    private bool IncludeOthers => Cfg("others") == "true";

    private const string TokenHelp =
        "On github.com: Settings → Developer settings → Fine-grained tokens. All repositories, read-only " +
        "Contents + Actions. Also lets these widgets see their own updates.";

    protected override async Task RefreshAsync()
    {
        var token = GitHub.Token;
        if (token == null) return;
        try
        {
            await GitHub.Refresh(token, IncludeOthers);
            error = null;
            authError = false;
        }
        catch (Exception e)
        {
            authError = e is HttpError { Code: 401 };
            error = Http.Describe(e, "GitHub");
        }
    }

    protected override void OnAction(string verb, JsonObject data)
    {
        if (verb is not ("saveToken" or "saveConfig")) return;
        var token = Str(data, "token");
        if (verb == "saveConfig")
        {
            Config["others"] = Str(data, "others") == "true" ? "true" : "false";
            Customizing = false;
        }
        if (token.Length > 0)
        {
            try
            {
                GitHub.CheckToken(token).GetAwaiter().GetResult();
                Settings.SetSecret(Settings.GitHubToken, token);
                authError = false;
                error = null;
            }
            catch (Exception e)
            {
                error = Http.Describe(e, "GitHub");
                Render();
                return;
            }
        }
        RefreshSoon(force: true);
    }

    protected override JsonObject Build()
    {
        if (GitHub.Token == null) return TokenCard("GitHub", TokenHelp, error);
        if (authError) return TokenCard("GitHub", TokenHelp, error);

        var repos = GitHub.Repos;
        var body = new List<JsonObject?>
        {
            Header(GitHub.Login ?? "Repos", AC.Open("https://github.com/" + (GitHub.Login ?? ""))),
        };
        if (repos.Count == 0)
        {
            body.Add(Message(error ?? (GitHub.FetchedAt == DateTime.MinValue ? "Loading…" : "No repos"),
                error == null ? null : "attention"));
            return AC.Card(AC.Items(body));
        }

        int rows = Small ? 3 : Large ? 9 : 7;
        foreach (var r in repos.Take(rows))
        {
            var (mark, color) = r.Run switch
            {
                null => ("·", (string?)null),
                { Status: not "completed" } => ("●", "warning"),
                { Conclusion: "success" } => ("✓", "good"),
                { Conclusion: "cancelled" or "skipped" } => ("–", (string?)null),
                _ => ("✗", "attention"),
            };
            var open = AC.Open(r.HtmlUrl);
            var markCol = AC.Col("auto", AC.Text(mark, bold: true, color: color, subtle: color == null));
            if (r.Run?.Url != null) markCol.Tap(AC.Open(r.Run.Url));
            body.Add(AC.Columns(new[]
            {
                markCol,
                AC.Col("stretch", AC.Text(r.Name)).Tap(open),
                r.Run is { Number: > 0 } ? AC.Col("auto", AC.Text("#" + r.Run.Number, size: "small", subtle: true)) : null,
                AC.Col("auto", AC.Text(AC.Ago(r.PushedAt), size: "small", subtle: true)),
            }, spacing: "none"));
            if (!Small)
            {
                var line = r.Run?.Message ?? r.LastCommit ?? r.Description;
                if (!string.IsNullOrWhiteSpace(line))
                    body.Add(AC.Text(line, size: "small", subtle: true).With("spacing", "none"));
            }
        }
        if (error != null) body.Add(Message(error, "attention"));
        return AC.Card(AC.Items(body));
    }

    protected override JsonObject BuildSettings()
    {
        return AC.Card(AC.Items(
            AC.Text("Repos settings", bold: true),
            AC.Choice("others", "Which repos", new[]
            {
                ("Only mine", "false"),
                ("Also ones I collaborate on", "true"),
            }, IncludeOthers ? "true" : "false"),
            AC.InputText("token", "GitHub token", "Leave empty to keep the current one", password: true),
            AC.Text(TokenHelp, subtle: true, size: "small", wrap: true, maxLines: 4, spacing: "small"),
            error == null ? null : Message(error, "attention"),
            AC.ActionSet(AC.Exec("saveConfig", title: "Save"), AC.Exec("cancel", title: "Cancel"))));
    }
}
