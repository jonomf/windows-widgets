using System.Text.Json.Nodes;
using Windows.ApplicationModel;

namespace JonoWidgets;

/// <summary>
/// Checks GitHub (with the Repos widget's token) for a newer build of this app. Widgets show a small
/// ↑ when one is out; tapping it opens the latest release to download.
/// </summary>
internal static class Updates
{
    public const string Repo = "jonomf/windows-widgets";
    public static string ReleaseUrl => $"https://github.com/{Repo}/releases/latest";

    private static int latest;
    private static DateTime checkedAt = DateTime.MinValue;
    private static int running;

    /// <summary>This build's number: the package version is 1.0.N.0, N = the CI run number.</summary>
    public static int OwnBuild
    {
        get
        {
            try { return Package.Current.Id.Version.Build; }
            catch { return 0; }
        }
    }

    public static bool Available => OwnBuild > 0 && latest > OwnBuild;

    public static async Task CheckAsync(Action onChange)
    {
        if (DateTime.UtcNow - checkedAt < TimeSpan.FromHours(3)) return;
        if (Interlocked.Exchange(ref running, 1) == 1) return;
        try
        {
            checkedAt = DateTime.UtcNow;
            var token = Settings.Secret(Settings.GitHubToken);
            var res = await Http.Json(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest",
                token == null ? null : Http.Bearer(token),
                headers: h => h.Add("X-GitHub-Api-Version", "2022-11-28")).ConfigureAwait(false);
            var tag = res?["tag_name"]?.ToString() ?? "";
            if (tag.StartsWith("build-") && int.TryParse(tag[6..], out var n))
            {
                bool was = Available;
                latest = n;
                Log.Info($"latest build {n}, installed {OwnBuild}");
                if (Available != was) onChange();
            }
        }
        catch (Exception e)
        {
            Log.Info("update check failed: " + e.Message);
        }
        finally
        {
            running = 0;
        }
    }
}
