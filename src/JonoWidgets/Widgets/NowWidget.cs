using System.Text.Json.Nodes;

namespace JonoWidgets.Widgets;

/// <summary>
/// "What I'm doing now", like the Android Now widget, logged to Toggl Track:
/// - Tap a different activity to switch to it (stops the running Toggl entry, starts a new one).
/// - Tapped the wrong one? Tap the right one within a minute and the mistake is replaced.
/// - Forgot to tap? Tap the current activity again to move its start back (15 minutes by default).
/// </summary>
internal sealed class NowWidget : WidgetBase
{
    public const string Definition = "Now";
    public override string DefinitionId => Definition;

    private static readonly string DefaultActs = string.Join("\n",
        "😴 Sleep", "🍽️ Eat", "🏃 Move", "🛋️ Rest", "💼 Work", "🏡 Family", "🫂 Friends", "🎨 Hobby",
        "📚 Read", "🎮 Games", "🎬 Watch", "🧹 Chores", "🛒 Errands", "🚗 Commute", "📱 Phone", "✳️ Other");

    private const string TokenHelp =
        "In Toggl Track: Profile settings → API Token (at the bottom). Each tap starts a Toggl time entry named after the activity.";

    private readonly object gate = new();
    private List<TogglEntry> entries = new();
    private TogglEntry? current;
    private long workspace;
    private string? error;
    private bool authError;
    private string lastTap = "";
    private DateTime lastTapAt = DateTime.MinValue;

    public NowWidget(string id, string state) : base(id, state) { }

    // Toggl's free plan allows ~30 calls an hour: fetch rarely, count the minutes ourselves.
    protected override TimeSpan RefreshEvery => TimeSpan.FromMinutes(20);
    protected override TimeSpan StaleAfter => TimeSpan.FromMinutes(10);

    private int StepMinutes => int.TryParse(Cfg("step"), out var m) && m > 0 ? m : 15;

    private sealed record Act(string Emoji, string Name);

    private List<Act> Acts()
    {
        var text = Cfg("acts");
        if (string.IsNullOrWhiteSpace(text)) text = DefaultActs;
        var list = new List<Act>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            int sp = line.IndexOf(' ');
            var first = sp > 0 ? line[..sp] : line;
            bool isEmoji = !first.Any(char.IsLetterOrDigit);
            list.Add(isEmoji && sp > 0
                ? new Act(first, line[(sp + 1)..].Trim())
                : new Act("", line));
        }
        return list.GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    // ------------------------------------------------------------------ data

    protected override async Task RefreshAsync()
    {
        var token = Toggl.Token;
        if (token == null) return;
        try
        {
            var wid = await Toggl.Workspace(token);
            var list = await Toggl.Today(token);
            lock (gate)
            {
                workspace = wid;
                entries = list;
                current = list.LastOrDefault(e => e.Running);
            }
            error = null;
            authError = false;
        }
        catch (Exception e)
        {
            authError = e is HttpError { IsAuth: true };
            error = Http.Describe(e, "Toggl");
        }
    }

    protected override void OnAction(string verb, JsonObject data)
    {
        switch (verb)
        {
            case "tap":
                Tap(Str(data, "act"));
                break;
            case "saveToken":
                SaveToken(Str(data, "token"));
                break;
            case "saveConfig":
                var token = Str(data, "token");
                Config["acts"] = (data["acts"]?.ToString() ?? "").Replace("\r\n", "\n").Trim();
                Config["step"] = int.TryParse(Str(data, "step"), out var s) && s > 0 ? s.ToString() : "15";
                Customizing = false;
                if (token.Length > 0) SaveToken(token);
                else Render();
                break;
        }
    }

    private void SaveToken(string token)
    {
        if (token.Length == 0) return;
        try
        {
            Toggl.ForgetWorkspace();
            var wid = Toggl.Workspace(token).GetAwaiter().GetResult();
            if (wid == 0) throw new InvalidOperationException("Toggl has no default workspace");
            Settings.SetSecret(Settings.TogglToken, token);
            authError = false;
            error = null;
        }
        catch (Exception e)
        {
            error = Http.Describe(e, "Toggl");
            Render();
            return;
        }
        RefreshSoon(force: true);
    }

    /// <summary>The one thing the buttons do (same rules as the Android widget).</summary>
    private void Tap(string name)
    {
        var token = Toggl.Token;
        if (token == null || name.Length == 0) return;
        var now = DateTimeOffset.Now;
        Func<Task> call;
        lock (gate)
        {
            if (Same(name, lastTap) && DateTime.UtcNow - lastTapAt < TimeSpan.FromMilliseconds(700)) return;
            lastTap = name;
            lastTapAt = DateTime.UtcNow;
            var cur = current;
            long wid = workspace;

            if (cur != null && Same(cur.Description, name))
            {
                // Forgot to tap: move the start back, but not before the previous entry ended.
                var prevEnd = entries.Where(e => e != cur && e.Stop != null && e.Stop <= cur.Start)
                    .Select(e => e.Stop!.Value).DefaultIfEmpty(DateTimeOffset.MinValue).Max();
                var floor = prevEnd == DateTimeOffset.MinValue ? DateTimeOffset.MinValue : prevEnd.AddMinutes(1);
                var start = cur.Start.AddMinutes(-StepMinutes);
                if (start < floor) start = floor;
                if (start >= cur.Start) return;
                cur.Start = start;
                call = async () =>
                {
                    if (cur.Id != 0) await Toggl.Update(token, await Wid(token, wid), cur.Id, Toggl.StartField(start));
                };
            }
            else if (cur != null && now - cur.Start < TimeSpan.FromMinutes(1))
            {
                // Tapped the wrong one a moment ago: replace it.
                cur.Description = name;
                call = async () =>
                {
                    if (cur.Id != 0)
                        await Toggl.Update(token, await Wid(token, wid), cur.Id, new JsonObject { ["description"] = name });
                };
            }
            else
            {
                var started = new TogglEntry { Description = name, Start = now };
                if (cur != null) cur.Stop = now;
                entries.Add(started);
                current = started;
                call = async () =>
                {
                    var w = await Wid(token, wid);
                    if (cur != null && cur.Id != 0) await Toggl.Stop(token, w, cur.Id);
                    var made = await Toggl.Start(token, w, name, now);
                    started.Id = made.Id;
                };
            }
        }

        Render(); // show it right away; Toggl catches up
        try
        {
            call().GetAwaiter().GetResult();
            error = null;
        }
        catch (Exception e)
        {
            error = Http.Describe(e, "Toggl");
            RefreshSoon(force: true); // get back in step with Toggl
        }
        Render();
    }

    private async Task<long> Wid(string token, long known)
    {
        if (known > 0) return known;
        var w = await Toggl.Workspace(token);
        lock (gate) workspace = w;
        return w;
    }

    // ------------------------------------------------------------------ cards

    protected override JsonObject Build()
    {
        if (Toggl.Token == null || authError)
            return TokenCard("Toggl", TokenHelp, authError ? "Toggl didn't accept the token" : error);

        var acts = Acts();
        TogglEntry? cur;
        List<TogglEntry> today;
        lock (gate)
        {
            cur = current;
            today = entries.ToList();
        }
        var curAct = cur == null ? null : acts.FirstOrDefault(a => Same(a.Name, cur.Description));

        string title = cur == null
            ? "Nothing yet"
            : curAct != null
                ? $"{curAct.Emoji} {curAct.Name}".Trim()
                : (cur.Description.Length > 0 ? cur.Description : "(no description)");
        string? since = null;
        if (cur != null)
        {
            var elapsed = AC.Duration(DateTimeOffset.Now - cur.Start);
            since = Small ? elapsed : $"{AC.Clock(cur.Start.LocalDateTime)} · {elapsed}";
        }

        var body = new List<JsonObject?>
        {
            Header(title, AC.Open("https://track.toggl.com/timer"),
                since == null ? null : AC.Col("auto", AC.Text(since, subtle: true, size: "small"))),
        };
        if (LastRefresh == DateTime.MinValue && cur == null && error == null)
            body.Add(Message("Loading…"));

        int slots = Small ? 4 : Large ? acts.Count : 12;
        var shown = Picks(acts, curAct, slots);
        foreach (var row in shown.Chunk(4))
        {
            var cols = row.Select(a => Button(a, curAct != null && a == curAct)).ToList();
            while (cols.Count < 4) cols.Add(AC.Col("1"));
            body.Add(AC.Columns(cols, spacing: "small"));
        }

        if (Large) body.AddRange(Breakdown(today, acts));
        if (error != null) body.Add(Message(error, "attention"));
        return AC.Card(AC.Items(body));
    }

    private static JsonObject Button(Act a, bool on)
    {
        var col = AC.Col("1",
            AC.Text(a.Emoji.Length > 0 ? a.Emoji : a.Name[..1].ToUpperInvariant(), size: "medium", align: "center"),
            AC.Text(a.Name, size: "small", subtle: !on, bold: on, align: "center"));
        if (on) col["style"] = "accent";
        return col.Tap(AC.Exec("tap", new JsonObject { ["act"] = a.Name }));
    }

    /// <summary>Which activities get buttons when they don't all fit. The current one is always there; your order is kept.</summary>
    private static List<Act> Picks(List<Act> all, Act? cur, int n)
    {
        if (all.Count <= n) return all;
        var picked = all.Take(n).ToList();
        if (cur != null && !picked.Contains(cur))
        {
            picked[^1] = cur;
            picked = picked.OrderBy(a => all.IndexOf(a)).ToList();
        }
        return picked;
    }

    /// <summary>Today's time per activity, biggest first, two to a row.</summary>
    private static IEnumerable<JsonObject> Breakdown(List<TogglEntry> today, List<Act> acts)
    {
        var midnight = new DateTimeOffset(DateTime.Today);
        var now = DateTimeOffset.Now;
        var totals = today
            .GroupBy(e => e.Description.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (name: g.Key, time: TimeSpan.FromTicks(g.Sum(e =>
            {
                var from = e.Start < midnight ? midnight : e.Start;
                var to = e.Stop ?? now;
                return Math.Max(0, (to - from).Ticks);
            }))))
            .Where(x => x.time >= TimeSpan.FromMinutes(1))
            .OrderByDescending(x => x.time)
            .ToList();
        if (totals.Count == 0) yield break;

        var total = TimeSpan.FromTicks(totals.Sum(x => x.time.Ticks));
        yield return AC.Text($"Today · {AC.Duration(total)}", bold: true, size: "small", spacing: "medium");
        foreach (var pair in totals.Take(8).Chunk(2))
        {
            yield return AC.Columns(pair.Select(x =>
            {
                var act = acts.FirstOrDefault(a => Same(a.Name, x.name));
                var label = act != null ? $"{act.Emoji} {act.Name}".Trim() : (x.name.Length > 0 ? x.name : "(none)");
                return AC.Col("1", AC.Text($"{label}  {AC.Duration(x.time)}", size: "small", subtle: true));
            }).Append(pair.Length < 2 ? AC.Col("1") : null), spacing: "none");
        }
    }

    protected override JsonObject BuildSettings()
    {
        var acts = Cfg("acts");
        return AC.Card(AC.Items(
            AC.Text("Now settings", bold: true),
            AC.InputText("acts", "Activities (one per line, emoji first)", value: string.IsNullOrWhiteSpace(acts) ? DefaultActs : acts,
                multiline: true),
            AC.Choice("step", "Tapping the current activity moves its start back by",
                new[] { ("5 minutes", "5"), ("10 minutes", "10"), ("15 minutes", "15"), ("30 minutes", "30") },
                StepMinutes.ToString()),
            AC.InputText("token", "Toggl API token", "Leave empty to keep the current one", password: true),
            AC.Text(TokenHelp, subtle: true, size: "small", wrap: true, maxLines: 3, spacing: "small"),
            error == null ? null : Message(error, "attention"),
            ClaudeSetting(),
            AC.ActionSet(AC.Exec("saveConfig", title: "Save"), AC.Exec("cancel", title: "Cancel"))));
    }
}
