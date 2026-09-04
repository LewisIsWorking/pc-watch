using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PcWatch;

/// <summary>A release newer than the one running.</summary>
public sealed record AvailableUpdate(string Version, string Url, string DownloadUrl, string Notes);

/// <summary>
/// Asks GitHub whether a newer release exists.
/// </summary>
/// <remarks>
/// 2026-09-02. Checks and NOTIFIES; it does not silently replace the running binary. A monitoring
/// tool that restarts itself without asking loses the history you were watching, and a background
/// self-update that fails halfway leaves no working copy at all. The user clicks, the download opens
/// in the browser, they run it.
///
/// ⚠️ Failure is SILENT BY DESIGN. No network, GitHub down, rate limited, running behind a proxy -
/// none of that is the user's problem and none of it should produce a dialog on a machine they were
/// already worried about. The absence of an update banner means "nothing to say", never "checked and
/// you are current"; the About line reports the last check so the difference is visible.
/// </remarks>
public sealed class UpdateChecker
{
    private const string LatestReleaseApi = "https://api.github.com/repos/LewisIsWorking/pc-watch/releases/latest";

    // One shared client for the real app. A new HttpClient per check exhausts sockets under
    // TIME_WAIT, which is how a "harmless" periodic check ends up taking a machine down.
    private static readonly HttpClient Shared = CreateClient(new HttpClientHandler());

    private readonly HttpClient _http;
    private readonly string _api;

    /// <summary>The real checker: shared client, real GitHub.</summary>
    public UpdateChecker()
    {
        _http = Shared;
        _api = LatestReleaseApi;
    }

    /// <summary>
    /// Test seam: supply a handler to drive every branch with no network.
    /// </summary>
    /// <remarks>
    /// ⛔ 2026-09-04. Added because this file sat at 0% coverage and COULD NOT be tested at all - the
    ///    client was static, the URL was a constant, and exercising a single line needed working
    ///    internet and a real GitHub release.
    ///
    ///    That matters more than a coverage number. Nearly every branch below handles a FAILURE
    ///    (non-200, missing tag, older version, malformed JSON), so the untestable paths were exactly
    ///    the ones that carry the risk. Combined with the silent-by-design failure mode above, a bug
    ///    in any of them would never reach a user as a complaint: it would present as "no updates".
    /// </remarks>
    public UpdateChecker(HttpMessageHandler handler, string api = LatestReleaseApi)
    {
        _http = CreateClient(handler);
        _api = api;
    }

    public DateTime? LastChecked { get; private set; }
    public string? LastError { get; private set; }

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

        // GitHub rejects requests with no User-Agent. Without this every check fails with a 403 that
        // looks exactly like "no updates available".
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PcWatch", AppVersion.Number));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    /// <summary>Returns a newer release, or null if there is none or the check could not be made.</summary>
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellation = default)
    {
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(_api, cancellation);
            LastChecked = DateTime.Now;

            if (!response.IsSuccessStatusCode)
            {
                LastError = $"GitHub returned {(int)response.StatusCode}";
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellation);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation);
            JsonElement root = document.RootElement;

            string tag = root.TryGetProperty("tag_name", out JsonElement t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(tag)) { LastError = "release had no tag"; return null; }

            // Version comparison, never string comparison: "1.10.0" < "1.9.0" alphabetically.
            if (AppVersion.Compare(tag, AppVersion.Number) <= 0)
            {
                LastError = null;
                return null;
            }

            string page = root.TryGetProperty("html_url", out JsonElement h) ? h.GetString() ?? "" : "";
            string notes = root.TryGetProperty("body", out JsonElement b) ? b.GetString() ?? "" : "";
            string download = FindWindowsAsset(root) ?? page;

            LastError = null;
            return new AvailableUpdate(tag.TrimStart('v', 'V'), page, download, notes);
        }
        catch (Exception ex)
        {
            LastChecked = DateTime.Now;
            LastError = ex.Message;
            return null;
        }
    }

    private static string? FindWindowsAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out JsonElement assets)) return null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (asset.TryGetProperty("browser_download_url", out JsonElement url)) return url.GetString();
        }
        return null;
    }

    /// <summary>One line describing the last check, for the About box.</summary>
    public string StatusLine() => LastChecked is null
        ? "update check: not run yet"
        : LastError is null
            ? $"update check: {LastChecked:HH:mm}, up to date"
            : $"update check: {LastChecked:HH:mm} failed ({LastError})";
}
