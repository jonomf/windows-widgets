using System.Text.Json.Nodes;
using Microsoft.Windows.Widgets.Providers;

namespace JonoWidgets.Widgets;

/// <summary>
/// One pinned widget. Subclasses build the card for the current size, fetch their data, and handle
/// their own buttons. Per-widget settings live in <see cref="Config"/>, which the Widgets board keeps
/// for us as the widget's "custom state", so they survive restarts and updates.
/// </summary>
internal abstract class WidgetBase
{
    private readonly object renderGate = new();
    private int refreshing;

    protected WidgetBase(string id, string state)
    {
        Id = id;
        try { Config = (string.IsNullOrWhiteSpace(state) ? null : JsonNode.Parse(state) as JsonObject) ?? new JsonObject(); }
        catch { Config = new JsonObject(); }
    }

    public string Id { get; }
    public abstract string DefinitionId { get; }

    /// <summary>"small", "medium" or "large".</summary>
    public string Size { get; set; } = "medium";
    public bool Active { get; set; }
    public bool Customizing { get; set; }

    protected JsonObject Config { get; }
    protected DateTime LastRefresh { get; set; } = DateTime.MinValue;
    protected bool Refreshing => refreshing == 1;

    /// <summary>How often to fetch while the board is open.</summary>
    protected abstract TimeSpan RefreshEvery { get; }

    /// <summary>On opening the board, fetch if the data is older than this.</summary>
    protected abstract TimeSpan StaleAfter { get; }

    protected abstract Task RefreshAsync();
    protected abstract JsonObject Build();
    protected abstract JsonObject BuildSettings();
    protected abstract void OnAction(string verb, JsonObject data);
    public virtual void OnCustomizeOpened() { }

    protected bool Small => Size == "small";
    protected bool Large => Size == "large";

    public void Render()
    {
        lock (renderGate)
        {
            try
            {
                var card = Customizing ? BuildSettings() : Build();
                var options = new WidgetUpdateRequestOptions(Id)
                {
                    Template = card.ToJsonString(),
                    Data = "{}",
                    CustomState = Config.ToJsonString(),
                };
                WidgetManager.GetDefault().UpdateWidget(options);
            }
            catch (Exception e)
            {
                Log.Error($"render {DefinitionId}", e);
            }
        }
    }

    public void RefreshSoon(bool force)
    {
        if (!force && DateTime.UtcNow - LastRefresh < StaleAfter) return;
        if (Interlocked.Exchange(ref refreshing, 1) == 1) return;
        Render(); // shows the refreshing state
        Task.Run(async () =>
        {
            try { await RefreshAsync().ConfigureAwait(false); }
            catch (Exception e) { Log.Error($"refresh {DefinitionId}", e); }
            finally
            {
                LastRefresh = DateTime.UtcNow;
                Interlocked.Exchange(ref refreshing, 0);
                Render();
            }
        });
    }

    /// <summary>Every minute while the board is open: fetch when due, otherwise redraw times.</summary>
    public virtual void Tick()
    {
        if (Customizing) return;
        if (DateTime.UtcNow - LastRefresh >= RefreshEvery) RefreshSoon(force: true);
        else Render();
    }

    public void HandleAction(string verb, JsonObject data)
    {
        switch (verb)
        {
            case "refresh":
                RefreshSoon(force: true);
                break;
            case "settings":
                Customizing = true;
                Render();
                OnCustomizeOpened();
                break;
            case "cancel":
                Customizing = false;
                Render();
                break;
            default:
                if (verb == "saveConfig" && data["claude_link"] != null)
                {
                    Settings.Set("claude_link", Str(data, "claude_link") == "false" ? "false" : null);
                }
                Task.Run(() =>
                {
                    try { OnAction(verb, data); }
                    catch (Exception e) { Log.Error($"{DefinitionId} {verb}", e); }
                });
                break;
        }
    }

    // ------------------------------------------------------------------ shared card pieces

    /// <summary>The Claude Code session that builds these widgets; ✦ in the title row opens it.</summary>
    public const string ClaudeSessionUrl = "https://claude.ai/code/session_01DwykdeafhgAME6S4dXmHNa";

    /// <summary>App-wide setting, on by default; changed from any widget's ⚙ settings.</summary>
    protected static bool ClaudeLinkShown => Settings.Get("claude_link") != "false";

    /// <summary>The show/hide choice for every widget's settings card (saved by HandleAction).</summary>
    protected static JsonObject ClaudeSetting() => AC.Choice("claude_link", "Claude session link (✦ in the title row)",
        new[] { ("Show", "true"), ("Hide", "false") }, ClaudeLinkShown ? "true" : "false");

    protected static string Str(JsonObject d, string key) => d[key]?.ToString()?.Trim() ?? "";

    protected string Cfg(string key, string fallback = "") => Config[key]?.ToString() ?? fallback;

    /// <summary>
    /// Top row: title (tappable), any extra bits (counts), then ↑ (update available), ✦ (Claude
    /// session), ⟳ and ⚙.
    /// </summary>
    protected JsonObject Header(string title, JsonObject? titleAction, params JsonObject?[] extras)
    {
        var cols = new List<JsonObject?>
        {
            AC.Col("stretch", AC.Text(title, bold: true)).Tap(titleAction),
        };
        cols.AddRange(extras);
        if (Updates.Available)
        {
            cols.Add(AC.Col("auto", AC.Text("↑", bold: true, color: "accent")).Tap(AC.Open(Updates.ReleaseUrl)));
        }
        if (ClaudeLinkShown && !Small)
        {
            cols.Add(AC.Col("auto", AC.Text("✦", subtle: true)).Tap(AC.Open(ClaudeSessionUrl)));
        }
        cols.Add(AC.Col("auto", AC.Text(Refreshing ? "…" : "⟳", subtle: true)).Tap(AC.Exec("refresh")));
        if (!Small) cols.Add(AC.Col("auto", AC.Text("⚙", subtle: true)).Tap(AC.Exec("settings")));
        return AC.Columns(cols, spacing: "none");
    }

    /// <summary>Card shown when a token is needed.</summary>
    protected static JsonObject TokenCard(string service, string where, string? problem, string verb = "saveToken")
    {
        return AC.Card(AC.Items(
            AC.Text($"Connect {service}", bold: true),
            problem == null ? null : AC.Text(problem, color: "attention", wrap: true, maxLines: 2, spacing: "small"),
            AC.Text(where, subtle: true, wrap: true, maxLines: 4, spacing: "small"),
            AC.InputText("token", placeholder: $"{service} token", password: true),
            AC.ActionSet(AC.Exec(verb, title: "Save"))
        ));
    }

    protected static JsonObject Message(string text, string? color = null) =>
        AC.Text(text, color: color, subtle: color == null, wrap: true, maxLines: 3, spacing: "small");
}
