using System.Net;

namespace PcWatch.Tests;

/// <summary>
/// A canned HTTP responder, so update-check behaviour can be driven without a network.
/// </summary>
/// <remarks>
/// 2026-09-04. Every interesting path in <see cref="UpdateChecker"/> is a failure path, and a real
/// GitHub cannot be asked to return a 403, an empty tag or malformed JSON on demand. It also cannot
/// be asked to do so REPEATABLY, which is what separates a test from an observation.
/// </remarks>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    /// <summary>Requests this handler was asked for, so a test can assert WHERE it went.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    private FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    /// <summary>Respond with a status and body.</summary>
    public static FakeHttp Returning(HttpStatusCode status, string body = "") =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    /// <summary>Respond with 200 and this JSON.</summary>
    public static FakeHttp Json(string json) => Returning(HttpStatusCode.OK, json);

    /// <summary>Throw, standing in for no network, DNS failure or a timeout.</summary>
    public static FakeHttp Throwing(Exception error) => new(_ => throw error);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_respond(request));
    }

    /// <summary>
    /// A minimal GitHub "latest release" payload.
    /// </summary>
    /// <remarks>
    /// Built from parts rather than held as one fixed string so a test can omit exactly one field
    /// and prove the reader copes, which is the shape of the real-world breakage: GitHub returning
    /// something structurally valid that is missing what we assumed was always there.
    /// </remarks>
    public static string Release(
        string? tag = "v9.9.9",
        string? htmlUrl = "https://github.com/LewisIsWorking/pc-watch/releases/tag/v9.9.9",
        string? body = "notes here",
        params (string Name, string Url)[] assets)
    {
        var fields = new List<string>();
        if (tag is not null) fields.Add($"\"tag_name\":{Quote(tag)}");
        if (htmlUrl is not null) fields.Add($"\"html_url\":{Quote(htmlUrl)}");
        if (body is not null) fields.Add($"\"body\":{Quote(body)}");

        if (assets.Length > 0)
        {
            IEnumerable<string> entries = assets.Select(a =>
                $"{{\"name\":{Quote(a.Name)},\"browser_download_url\":{Quote(a.Url)}}}");
            fields.Add($"\"assets\":[{string.Join(",", entries)}]");
        }

        return $"{{{string.Join(",", fields)}}}";
    }

    /// <summary>A release whose assets array exists but whose entries lack a download url.</summary>
    public static string ReleaseWithUrllessAsset(string tag = "v9.9.9") =>
        $"{{\"tag_name\":{Quote(tag)},\"html_url\":\"https://example.invalid/page\","
        + "\"body\":\"\",\"assets\":[{\"name\":\"PcWatch.zip\"}]}";

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
