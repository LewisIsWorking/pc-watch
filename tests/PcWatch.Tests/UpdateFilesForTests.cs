using System.IO.Compression;
using System.Security.Cryptography;

namespace PcWatch.Tests;

/// <summary>
/// Builds fake release assets and a scratch folder, for the updater tests.
/// </summary>
/// <remarks>
/// ⛔ 2026-09-16. EVERY UPDATER TEST WORKS ON FILES IN ITS OWN TEMP FOLDER. The code under test renames
///    and replaces executables; pointed at the wrong path it would replace the test runner or a real
///    install. Nothing here ever names Environment.ProcessPath.
/// </remarks>
internal sealed class UpdateFilesForTests : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"pcwatch-update-{Guid.NewGuid():N}");

    public UpdateFilesForTests() => Directory.CreateDirectory(Root);

    public string PathOf(string name) => System.IO.Path.Combine(Root, name);

    /// <summary>A file standing in for the running exe, with recognisable content.</summary>
    public string CurrentExe(string content = "OLD VERSION")
    {
        string path = PathOf(UpdateSwap.ExecutableName);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A release zip laid out like the real v1.1.0 asset: PcWatch.exe and PcWatch.ico at the root.</summary>
    public static byte[] ReleaseZip(string exeContent = "NEW VERSION", string exeEntryName = UpdateSwap.ExecutableName)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, exeEntryName, exeContent);
            WriteEntry(zip, "PcWatch.ico", "icon bytes");
        }
        return buffer.ToArray();
    }

    public static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static AvailableUpdate Update(string? sha256, string url = "https://example.invalid/PcWatch-9.9.9-win-x64.zip") =>
        new("9.9.9", "https://example.invalid/release", url, "notes", sha256);

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* a leftover temp folder is harmless */ }
    }
}
