using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace JonoWidgets;

internal sealed class HttpError : Exception
{
    public int Code { get; }
    public TimeSpan? RetryAfter { get; }

    public HttpError(int code, string message, TimeSpan? retryAfter = null) : base(message)
    {
        Code = code;
        RetryAfter = retryAfter;
    }

    public bool IsAuth => Code == 401 || Code == 403;
}

/// <summary>One shared HttpClient and a JSON request helper.</summary>
internal static class Http
{
    public static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    static Http()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("jono-windows-widgets/1.0");
    }

    public static async Task<JsonNode?> Json(
        HttpMethod method, string url, AuthenticationHeaderValue? auth,
        HttpContent? body = null, Action<HttpRequestHeaders>? headers = null)
    {
        using var req = new HttpRequestMessage(method, url) { Content = body };
        req.Headers.Authorization = auth;
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        headers?.Invoke(req.Headers);
        using var res = await Client.SendAsync(req).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            TimeSpan? retry = res.Headers.RetryAfter?.Delta;
            throw new HttpError((int)res.StatusCode, Summarize(text, (int)res.StatusCode), retry);
        }
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); }
        catch { return JsonValue.Create(text); }
    }

    public static StringContent JsonBody(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    public static FormUrlEncodedContent Form(params (string key, string value)[] pairs) =>
        new(pairs.Select(p => new KeyValuePair<string, string>(p.key, p.value)));

    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    public static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    private static string Summarize(string body, int code)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject o)
            {
                var msg = o["error"]?.ToString() ?? o["message"]?.ToString();
                if (!string.IsNullOrWhiteSpace(msg)) return msg!;
            }
        }
        catch
        {
            // not JSON
        }
        body = body.Trim();
        return body.Length is > 0 and < 160 ? body : $"HTTP {code}";
    }

    /// <summary>A short, human message for any failure.</summary>
    public static string Describe(Exception e, string service) => e switch
    {
        HttpError h when h.IsAuth => $"{service} didn't accept the token",
        HttpError h when h.Code == 402 || h.Code == 429 =>
            $"{service} rate limit" + (h.RetryAfter is { } r ? $", retry in {Math.Max(1, (int)r.TotalMinutes)}m" : ""),
        HttpError h => $"{service}: {h.Message}",
        HttpRequestException or TaskCanceledException => "offline",
        _ => e.Message,
    };
}
