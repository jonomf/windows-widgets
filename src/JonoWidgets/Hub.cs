using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using JonoWidgets.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace JonoWidgets;

/// <summary>All pinned widgets in this process, and the once-a-minute tick that keeps visible ones fresh.</summary>
internal static class Hub
{
    private static readonly ConcurrentDictionary<string, WidgetBase> Widgets = new();
    private static Timer? timer;

    public static void Start()
    {
        try
        {
            foreach (var info in WidgetManager.GetDefault().GetWidgetInfos() ?? Array.Empty<WidgetInfo>())
            {
                var c = info.WidgetContext;
                var w = Make(c.Id, c.DefinitionId, info.CustomState ?? "");
                if (w == null) continue;
                w.Size = c.Size.ToString().ToLowerInvariant();
                Widgets[c.Id] = w;
                Log.Info($"recovered {c.DefinitionId} {c.Id} ({w.Size})");
            }
        }
        catch (Exception e)
        {
            Log.Error("recover", e);
        }
        timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
    }

    private static WidgetBase? Make(string id, string definition, string state) => definition switch
    {
        TodoistWidget.Definition => new TodoistWidget(id, state),
        ReposWidget.Definition => new ReposWidget(id, state),
        NowWidget.Definition => new NowWidget(id, state),
        _ => null,
    };

    /// <summary>The widget, recreating it (with its saved settings) if this process hasn't seen it yet.</summary>
    private static WidgetBase? Get(string id, string definition, string size)
    {
        if (Widgets.TryGetValue(id, out var w))
        {
            w.Size = size;
            return w;
        }
        string state = "";
        try { state = WidgetManager.GetDefault().GetWidgetInfo(id)?.CustomState ?? ""; }
        catch (Exception e) { Log.Error("get info " + id, e); }
        w = Make(id, definition, state);
        if (w == null)
        {
            Log.Info($"unknown widget definition {definition}");
            return null;
        }
        w.Size = size;
        return Widgets.GetOrAdd(id, w);
    }

    public static void Create(string id, string definition, string size)
    {
        Log.Info($"create {definition} {id} ({size})");
        var w = Get(id, definition, size);
        if (w == null) return;
        w.Render();
        w.RefreshSoon(force: true);
    }

    public static void Delete(string id)
    {
        Log.Info($"delete {id}");
        Widgets.TryRemove(id, out _);
    }

    public static void Activate(string id, string definition, string size)
    {
        var w = Get(id, definition, size);
        if (w == null) return;
        w.Active = true;
        w.Render();
        w.RefreshSoon(force: false);
        _ = Updates.CheckAsync(() => RenderAll());
    }

    public static void Deactivate(string id)
    {
        if (Widgets.TryGetValue(id, out var w)) w.Active = false;
    }

    public static void Resized(string id, string definition, string size)
    {
        var w = Get(id, definition, size);
        w?.Render();
    }

    public static void Customize(string id, string definition, string size)
    {
        var w = Get(id, definition, size);
        if (w == null) return;
        w.Customizing = true;
        w.Render();
        w.OnCustomizeOpened();
    }

    public static void Action(string id, string definition, string size, string verb, string data)
    {
        var w = Get(id, definition, size);
        if (w == null) return;
        JsonObject d;
        try { d = (string.IsNullOrWhiteSpace(data) ? null : JsonNode.Parse(data) as JsonObject) ?? new JsonObject(); }
        catch { d = new JsonObject(); }
        Log.Info($"action {definition} {verb}");
        w.HandleAction(verb, d);
    }

    /// <summary>Redraws every widget of a kind (e.g. all Todoist widgets after a task is completed).</summary>
    public static void RenderAll(string? definition = null)
    {
        foreach (var w in Widgets.Values)
            if (definition == null || w.DefinitionId == definition) w.Render();
    }

    public static void RefreshAll(string definition)
    {
        foreach (var w in Widgets.Values)
            if (w.DefinitionId == definition) w.RefreshSoon(force: true);
    }

    public static IEnumerable<WidgetBase> All(string definition) => Widgets.Values.Where(w => w.DefinitionId == definition);

    private static void Tick()
    {
        foreach (var w in Widgets.Values)
        {
            if (!w.Active) continue;
            try { w.Tick(); }
            catch (Exception e) { Log.Error("tick", e); }
        }
    }
}
