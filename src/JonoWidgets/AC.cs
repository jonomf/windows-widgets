using System.Text.Json.Nodes;

namespace JonoWidgets;

/// <summary>
/// Small builders for Adaptive Card JSON. Cards are built whole in code and sent with empty data,
/// so any "${" in user text is broken up to keep the templating engine from reading it.
/// </summary>
internal static class AC
{
    public static string Safe(string? s) => (s ?? "").Replace("${", "$​{");

    public static JsonObject Card(JsonArray body) => new()
    {
        ["type"] = "AdaptiveCard",
        ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
        ["version"] = "1.5",
        ["body"] = body,
    };

    public static JsonArray Items(params JsonObject?[] items)
    {
        var a = new JsonArray();
        foreach (var i in items) if (i != null) a.Add(i);
        return a;
    }

    public static JsonArray Items(IEnumerable<JsonObject?> items) => Items(items.ToArray());

    public static JsonObject Text(
        string text,
        string? size = null,
        bool bold = false,
        string? color = null,
        bool subtle = false,
        bool wrap = false,
        int maxLines = 1,
        string? spacing = "none",
        string? align = null)
    {
        var o = new JsonObject { ["type"] = "TextBlock", ["text"] = Safe(text) };
        if (size != null) o["size"] = size;
        if (bold) o["weight"] = "bolder";
        if (color != null) o["color"] = color;
        if (subtle) o["isSubtle"] = true;
        if (wrap || maxLines > 1)
        {
            o["wrap"] = true;
            if (maxLines > 0) o["maxLines"] = maxLines;
        }
        if (spacing != null) o["spacing"] = spacing;
        if (align != null) o["horizontalAlignment"] = align;
        return o;
    }

    public static JsonObject Columns(params JsonObject?[] columns) => Columns((IEnumerable<JsonObject?>)columns);

    public static JsonObject Columns(IEnumerable<JsonObject?> columns, string? spacing = "small")
    {
        var o = new JsonObject { ["type"] = "ColumnSet", ["columns"] = Items(columns) };
        if (spacing != null) o["spacing"] = spacing;
        return o;
    }

    /// <summary>A column: width "auto", "stretch" or a weight like "1".</summary>
    public static JsonObject Col(string width, params JsonObject?[] items)
    {
        var o = new JsonObject { ["type"] = "Column", ["width"] = width, ["items"] = Items(items) };
        o["verticalContentAlignment"] = "center";
        return o;
    }

    public static JsonObject Container(IEnumerable<JsonObject?> items, string? style = null, string? spacing = "small")
    {
        var o = new JsonObject { ["type"] = "Container", ["items"] = Items(items) };
        if (style != null) o["style"] = style;
        if (spacing != null) o["spacing"] = spacing;
        return o;
    }

    public static JsonObject Exec(string verb, JsonObject? data = null, string? title = null)
    {
        var o = new JsonObject { ["type"] = "Action.Execute", ["verb"] = verb };
        if (data != null) o["data"] = data;
        if (title != null) o["title"] = Safe(title);
        return o;
    }

    public static JsonObject Open(string url, string? title = null)
    {
        var o = new JsonObject { ["type"] = "Action.OpenUrl", ["url"] = url };
        if (title != null) o["title"] = Safe(title);
        return o;
    }

    public static JsonObject ActionSet(params JsonObject?[] actions) => new()
    {
        ["type"] = "ActionSet",
        ["actions"] = Items(actions),
        ["spacing"] = "medium",
    };

    public static JsonObject InputText(string id, string? label = null, string? placeholder = null, string? value = null,
        bool multiline = false, bool password = false)
    {
        var o = new JsonObject { ["type"] = "Input.Text", ["id"] = id, ["spacing"] = "small" };
        if (label != null) o["label"] = label;
        if (placeholder != null) o["placeholder"] = placeholder;
        if (!string.IsNullOrEmpty(value)) o["value"] = value;
        if (multiline) o["isMultiline"] = true;
        if (password) o["style"] = "password";
        return o;
    }

    public static JsonObject Choice(string id, string? label, IEnumerable<(string title, string value)> choices, string? value)
    {
        var arr = new JsonArray();
        foreach (var (t, v) in choices) arr.Add(new JsonObject { ["title"] = Safe(t), ["value"] = v });
        var o = new JsonObject
        {
            ["type"] = "Input.ChoiceSet",
            ["id"] = id,
            ["style"] = "compact",
            ["choices"] = arr,
            ["spacing"] = "small",
        };
        if (label != null) o["label"] = label;
        if (!string.IsNullOrEmpty(value)) o["value"] = value;
        return o;
    }

    /// <summary>Makes a column/container tappable.</summary>
    public static JsonObject Tap(this JsonObject o, JsonObject? action)
    {
        if (action != null) o["selectAction"] = action;
        return o;
    }

    public static JsonObject With(this JsonObject o, string key, JsonNode? value)
    {
        o[key] = value;
        return o;
    }

    /// <summary>"5m", "3h", "2d" since a moment.</summary>
    public static string Ago(DateTimeOffset t)
    {
        var d = DateTimeOffset.Now - t;
        if (d.TotalMinutes < 1) return "now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours}h";
        if (d.TotalDays < 14) return $"{(int)d.TotalDays}d";
        if (d.TotalDays < 60) return $"{(int)(d.TotalDays / 7)}w";
        if (d.TotalDays < 365) return $"{(int)(d.TotalDays / 30)}mo";
        return $"{(int)(d.TotalDays / 365)}y";
    }

    /// <summary>"1h 20m", "45m".</summary>
    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        int h = (int)d.TotalHours, m = d.Minutes;
        return h > 0 ? (m > 0 ? $"{h}h {m}m" : $"{h}h") : $"{m}m";
    }

    /// <summary>"2:30pm", "9am".</summary>
    public static string Clock(DateTime t) =>
        (t.Minute == 0 ? t.ToString("%h") : t.ToString("h:mm")) + (t.Hour < 12 ? "am" : "pm");

    public static string Short(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}
