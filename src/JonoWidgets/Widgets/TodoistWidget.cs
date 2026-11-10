using System.Text.Json.Nodes;

namespace JonoWidgets.Widgets;

/// <summary>
/// Tasks from one Todoist project, saved filter or custom query, most overdue first (or priority
/// first). Pin two (work + personal). Tap ○ to complete, tap a task to open it in Todoist.
/// </summary>
internal sealed class TodoistWidget : WidgetBase
{
    public const string Definition = "Todoist";
    public override string DefinitionId => Definition;

    private List<TodoTask>? tasks;
    private string? error;
    private DateTime syncedAt = DateTime.MinValue;

    public TodoistWidget(string id, string state) : base(id, state) { }

    protected override TimeSpan RefreshEvery => TimeSpan.FromMinutes(10);
    protected override TimeSpan StaleAfter => TimeSpan.FromMinutes(2);

    private string Source => Cfg("source");
    private bool Configured => Source.Length > 0;
    private string Title => Cfg("name").Length > 0 ? Cfg("name") : "Todoist";
    private bool PriorityFirst => Cfg("sort") == "1";

    // ------------------------------------------------------------------ data

    protected override async Task RefreshAsync()
    {
        var token = Todoist.Token;
        if (token == null) return;
        try
        {
            if (Todoist.Projects.Count == 0 || DateTime.UtcNow - Todoist.MetaAt > TimeSpan.FromMinutes(30))
                await Todoist.LoadMeta(token);
        }
        catch (HttpError e) when (e.IsAuth)
        {
            Todoist.AuthError = true;
            return;
        }
        catch (Exception e)
        {
            error = Http.Describe(e, "Todoist");
            return;
        }
        if (!Configured) return;

        if (Source == "filter")
        {
            var f = Todoist.Filters.FirstOrDefault(x => x.Id == Cfg("ref"));
            if (f != null && f.Query != Cfg("query")) Config["query"] = f.Query; // edited in Todoist
        }
        try
        {
            tasks = Source == "project"
                ? await Todoist.ByProject(token, Cfg("ref"))
                : await Todoist.ByQuery(token, Cfg("query"));
            syncedAt = DateTime.UtcNow;
            error = null;
        }
        catch (Exception e)
        {
            error = Http.Describe(e, "Todoist");
        }
    }

    public override void OnCustomizeOpened()
    {
        if (Todoist.Token != null && Todoist.Projects.Count == 0) RefreshSoon(force: true);
    }

    protected override void OnAction(string verb, JsonObject data)
    {
        switch (verb)
        {
            case "saveToken":
                SaveToken(Str(data, "token"));
                break;
            case "saveConfig":
                SaveConfig(data);
                break;
            case "complete":
                Complete(Str(data, "id"));
                break;
        }
    }

    private void SaveToken(string token)
    {
        if (token.Length == 0) return;
        try
        {
            Todoist.LoadMeta(token).GetAwaiter().GetResult();
            Settings.SetSecret(Settings.TodoistToken, token);
            error = null;
        }
        catch (Exception e)
        {
            Todoist.AuthError = e is HttpError { IsAuth: true };
            error = Http.Describe(e, "Todoist");
        }
        Hub.RenderAll(Definition);
        Hub.RefreshAll(Definition);
    }

    private void SaveConfig(JsonObject d)
    {
        var token = Str(d, "token");
        if (token.Length > 0) SaveToken(token);

        var src = Str(d, "src");
        var query = Str(d, "query");
        var name = Str(d, "name");
        if (src.StartsWith("p:"))
        {
            var p = Todoist.Projects.FirstOrDefault(x => x.Id == src[2..]);
            Config["source"] = "project";
            Config["ref"] = src[2..];
            Config["query"] = "";
            if (name.Length == 0) name = p?.Name ?? "Todoist";
        }
        else if (src.StartsWith("f:"))
        {
            var f = Todoist.Filters.FirstOrDefault(x => x.Id == src[2..]);
            Config["source"] = "filter";
            Config["ref"] = src[2..];
            Config["query"] = f?.Query ?? "";
            if (name.Length == 0) name = f?.Name ?? "Todoist";
        }
        else if (query.Length > 0)
        {
            Config["source"] = "query";
            Config["ref"] = query;
            Config["query"] = query;
            if (name.Length == 0) name = query;
        }
        else if (!Configured)
        {
            error = "Pick a project or filter, or type a query.";
            Render();
            return;
        }
        Config["name"] = name;
        Config["sort"] = Str(d, "sort") == "1" ? "1" : "0";
        Customizing = false;
        tasks = null;
        error = null;
        RefreshSoon(force: true);
    }

    private void Complete(string taskId)
    {
        var token = Todoist.Token;
        if (taskId.Length == 0 || token == null) return;
        lock (Todoist.Hidden) Todoist.Hidden.Add(taskId);
        Hub.RenderAll(Definition);
        try
        {
            Todoist.Close(token, taskId).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            error = "Couldn't complete it: " + Http.Describe(e, "Todoist");
        }
        finally
        {
            lock (Todoist.Hidden) Todoist.Hidden.Remove(taskId);
        }
        // A recurring task comes back with its next date; other widgets may show the same task.
        Hub.RefreshAll(Definition);
    }

    // ------------------------------------------------------------------ cards

    protected override JsonObject Build()
    {
        if (Todoist.Token == null)
            return TokenCard("Todoist", "In Todoist: Settings → Integrations → Developer → copy your API token.", error);
        if (Todoist.AuthError)
            return TokenCard("Todoist", "In Todoist: Settings → Integrations → Developer → copy your API token.",
                "Todoist didn't accept the token");
        if (!Configured) return BuildSettings();

        var today = DateOnly.FromDateTime(DateTime.Now);
        var now = TimeOnly.FromDateTime(DateTime.Now);
        List<TodoTask> list;
        lock (Todoist.Hidden) list = Sort((tasks ?? new()).Where(t => !Todoist.Hidden.Contains(t.Id))).ToList();
        int overdue = list.Count(t => IsOverdue(t, today, now));

        var body = new List<JsonObject?>
        {
            Header(Title, SourceLink(),
                overdue > 0 ? AC.Col("auto", AC.Text(overdue.ToString(), bold: true, color: "attention")) : null,
                tasks != null ? AC.Col("auto", AC.Text(list.Count.ToString(), subtle: true)) : null),
        };

        if (tasks == null)
        {
            body.Add(Message(error ?? "Loading…", error == null ? null : "attention"));
            return AC.Card(AC.Items(body));
        }
        if (list.Count == 0) body.Add(Message("Nothing here 🎉"));

        int rows = Small ? 3 : Large ? 10 : 8;
        var names = Todoist.ProjectNames();
        foreach (var t in list.Take(rows)) body.AddRange(Row(t, today, now, names));
        if (list.Count > rows)
            body.Add(AC.Text($"+{list.Count - rows} more", subtle: true, size: "small").Tap(SourceLink()));

        if (error != null) body.Add(Message(error, "attention"));
        else if (Large && syncedAt > DateTime.MinValue)
            body.Add(AC.Text($"updated {AC.Ago(syncedAt.ToLocalTime())}", subtle: true, size: "small", spacing: "small"));
        return AC.Card(AC.Items(body));
    }

    private IEnumerable<JsonObject> Row(TodoTask t, DateOnly today, TimeOnly now, Dictionary<string, string> names)
    {
        var (dueText, dueColor) = Due(t, today, now);
        var open = AC.Open($"https://app.todoist.com/app/task/{Uri.EscapeDataString(t.Id)}");
        var priorityColor = t.Priority switch { 4 => "attention", 3 => "warning", 2 => "accent", _ => (string?)null };
        yield return AC.Columns(new[]
        {
            AC.Col("auto", AC.Text("○", bold: true, color: priorityColor, subtle: priorityColor == null))
                .Tap(AC.Exec("complete", new JsonObject { ["id"] = t.Id })),
            AC.Col("stretch", AC.Text(t.Content)).Tap(open),
            dueText == null ? null : AC.Col("auto", AC.Text(dueText, size: "small", color: dueColor, subtle: dueColor == null)),
        }, spacing: "none");

        if (Large)
        {
            var bits = new List<string>();
            if (Source != "project" && t.ProjectId != null && names.TryGetValue(t.ProjectId, out var pn)) bits.Add(pn);
            bits.AddRange(t.Labels.Select(l => "@" + l));
            if (t.Deadline is { } dl) bits.Add("⚑ " + ShortDate(dl, today));
            if (t.Description.Length > 0) bits.Add(t.Description);
            if (bits.Count > 0)
                yield return AC.Text(string.Join(" · ", bits), size: "small", subtle: true)
                    .With("spacing", "none");
        }
    }

    private JsonObject? SourceLink() => Source switch
    {
        "project" => AC.Open($"https://app.todoist.com/app/project/{Uri.EscapeDataString(Cfg("ref"))}"),
        "filter" => AC.Open($"https://app.todoist.com/app/filter/{Uri.EscapeDataString(Cfg("ref"))}"),
        "query" => AC.Open($"https://app.todoist.com/app/search/{Uri.EscapeDataString(Cfg("query"))}"),
        _ => AC.Open("https://app.todoist.com/app/today"),
    };

    protected override JsonObject BuildSettings()
    {
        if (Todoist.Token == null || Todoist.AuthError)
            return TokenCard("Todoist", "In Todoist: Settings → Integrations → Developer → copy your API token.",
                Todoist.AuthError ? "Todoist didn't accept the token" : error);

        var items = new List<JsonObject?> { AC.Text(Configured ? "Widget settings" : "What should this widget show?", bold: true) };
        if (Todoist.Projects.Count == 0 && Todoist.Filters.Count == 0)
        {
            items.Add(Message(error ?? "Loading your projects…", error == null ? null : "attention"));
            if (Configured) items.Add(AC.ActionSet(AC.Exec("cancel", title: "Cancel")));
            return AC.Card(AC.Items(items));
        }

        var choices = new List<(string, string)>();
        choices.AddRange(Todoist.Filters.Select(f => ("⚙ " + f.Name, "f:" + f.Id)));
        choices.AddRange(Todoist.Projects.Select(p => ("# " + p.Name, "p:" + p.Id)));
        choices.Add(("Custom query (below)", "q"));
        string current = Source switch
        {
            "project" => "p:" + Cfg("ref"),
            "filter" => "f:" + Cfg("ref"),
            "query" => "q",
            _ => "",
        };

        items.Add(AC.Choice("src", "Project or filter", choices, current));
        items.Add(AC.InputText("query", placeholder: "Custom query, e.g. today | overdue",
            value: Source == "query" ? Cfg("query") : null));
        items.Add(AC.Choice("sort", "Order", new[] { ("Most overdue first", "0"), ("Priority first", "1") },
            PriorityFirst ? "1" : "0"));
        items.Add(AC.InputText("name", "Title", placeholder: "Defaults to the project or filter name",
            value: Cfg("name")));
        items.Add(AC.InputText("token", "Todoist token", placeholder: "Leave empty to keep the current one", password: true));
        if (error != null) items.Add(Message(error, "attention"));
        items.Add(ClaudeSetting());
        items.Add(AC.ActionSet(
            AC.Exec("saveConfig", title: "Save"),
            Configured ? AC.Exec("cancel", title: "Cancel") : null));
        return AC.Card(AC.Items(items));
    }

    // ------------------------------------------------------------------ rules (same as the Android widget)

    /// <summary>Most overdue first, then priority; undated last. Or priority first, then date.</summary>
    private IEnumerable<TodoTask> Sort(IEnumerable<TodoTask> list)
    {
        var byDate = list.OrderBy(t => t.DueDate ?? DateOnly.MaxValue);
        IOrderedEnumerable<TodoTask> sorted = PriorityFirst
            ? list.OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
            : byDate.ThenByDescending(t => t.Priority);
        return sorted.ThenBy(t => t.DueTime ?? TimeOnly.MaxValue).ThenBy(t => t.Order);
    }

    private static bool IsOverdue(TodoTask t, DateOnly today, TimeOnly now)
    {
        if (t.DueDate is not { } d) return false;
        if (d < today) return true;
        return d == today && t.DueTime is { } time && time < now;
    }

    /// <summary>"3d" overdue (red), "Today" / "2:30pm" (green), "Tmrw" (orange), "Fri" (blue), "Nov 12". ↻ if recurring.</summary>
    private static (string? text, string? color) Due(TodoTask t, DateOnly today, TimeOnly now)
    {
        if (t.DueDate is not { } d) return (null, null);
        int days = today.DayNumber - d.DayNumber; // > 0: overdue by that many days
        string? clock = t.DueTime is { } tm ? AC.Clock(DateTime.Today.Add(tm.ToTimeSpan())) : null;
        (string text, string? color) r = days switch
        {
            >= 365 => ($"{days / 365}y", "attention"),
            >= 60 => ($"{days / 30}mo", "attention"),
            >= 14 => ($"{days / 7}w", "attention"),
            > 0 => ($"{days}d", "attention"),
            0 => (clock ?? "Today", t.DueTime is { } time && time < now ? "attention" : "good"),
            -1 => ("Tmrw" + (clock != null ? " " + clock : ""), "warning"),
            >= -6 => (d.ToString("ddd") + (clock != null ? " " + clock : ""), "accent"),
            _ => (ShortDate(d, today), null),
        };
        return (t.Recurring ? r.text + " ↻" : r.text, r.color);
    }

    private static string ShortDate(DateOnly d, DateOnly today) =>
        d.Year == today.Year ? d.ToString("MMM d") : d.ToString("MMM d ''yy");
}
