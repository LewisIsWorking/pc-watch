using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace PcWatch;

/// <summary>An update that could not be installed, with a reason fit to show the user.</summary>
/// <param name="leftDiskChanged">
/// True only when the failure happened AFTER the running exe was moved and could not be put back.
/// </param>
/// <remarks>
/// ⛔ 2026-09-16. Carried as a fact rather than inferred from the message, because every other failure
///    can honestly say "nothing was changed" and this one cannot. Telling someone their install is
///    untouched when the exe has been renamed aside is the worst message this updater could show.
/// </remarks>
public sealed class UpdateFailedException(string message, Exception? inner = null, bool leftDiskChanged = false)
    : Exception(message, inner)
{
    public bool LeftDiskChanged { get; } = leftDiskChanged;
}

/// <summary>
/// Downloads a release asset and proves the bytes are the ones GitHub published.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-16. NOTHING IS DOWNLOADED WITHOUT A DIGEST TO CHECK IT AGAINST. With no published
///    SHA-256 the only evidence a file is the real release is that it arrived over HTTPS, and a
///    truncated or corrupted download arrives over HTTPS too. That case is refused before any request
///    is made, so the caller falls back to the download page.
///
/// ⚠️ A mismatch deletes the file. A half-trusted executable left in the temp folder is one that
///    something else might later pick up and run.
/// </remarks>
public sealed class UpdateDownloader
{
    private readonly HttpClient _http;

    /// <summary>The real downloader.</summary>
    public UpdateDownloader() : this(new HttpClientHandler()) { }

    /// <summary>Test seam: drive every branch without a network.</summary>
    public UpdateDownloader(HttpMessageHandler handler)
    {
        // A 47 MB asset over a slow link can take minutes; the checker's 15 s would kill it.
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PcWatch", AppVersion.Number));
    }

    /// <summary>
    /// Download <paramref name="update"/> into <paramref name="workDir"/> and return the verified file.
    /// </summary>
    /// <exception cref="UpdateFailedException">No digest, the download failed, or the hash did not match.</exception>
    public async Task<string> DownloadVerifiedAsync(
        AvailableUpdate update, string workDir, CancellationToken cancellation = default)
    {
        if (update.Sha256 is null)
        {
            throw new UpdateFailedException("this release does not publish a checksum, so it cannot be verified");
        }

        Directory.CreateDirectory(workDir);
        string name = Path.GetFileName(new Uri(update.DownloadUrl).LocalPath);
        // Never trust the url to name a file: an empty or odd final segment would write somewhere odd.
        string path = Path.Combine(workDir, string.IsNullOrWhiteSpace(name) ? "update.bin" : name);

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(
                update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateFailedException($"the download returned {(int)response.StatusCode}");
            }

            await using (FileStream file = File.Create(path))
            {
                await response.Content.CopyToAsync(file, cancellation);
            }
        }
        catch (UpdateFailedException)
        {
            TryDelete(path);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(path);
            throw new UpdateFailedException($"the download failed: {ex.Message}", ex);
        }

        string actual = await HashAsync(path, cancellation);
        if (!string.Equals(actual, update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(path);
            throw new UpdateFailedException(
                "the downloaded file did not match the published checksum, so it was discarded");
        }

        return path;
    }

    internal static async Task<string> HashAsync(string path, CancellationToken cancellation = default)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellation);
        return Convert.ToHexStringLower(hash);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort: the temp folder is cleaned by Windows */ }
    }
}
