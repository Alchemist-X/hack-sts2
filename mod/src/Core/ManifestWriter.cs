using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

/// <summary>
/// Atomic manifest.json writes: write a uniquely-named temp file in the same
/// directory, fsync it, then rename over the target. The unique temp name means
/// concurrent writers can never consume each other's temp file; the
/// Flush(flushToDisk: true) before the rename means a crash can only leave
/// either the previous manifest or the complete new one, never a truncated file.
/// </summary>
internal static class ManifestWriter
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public const string ManifestName = "manifest.json";

    public static void WriteAtomic(string sessionDir, JsonObject manifest)
    {
        var target = Path.Combine(sessionDir, ManifestName);
        var temp = Path.Combine(
            sessionDir, $"{ManifestName}.{Path.GetRandomFileName()}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(manifest.ToJsonString(Indented) + "\n");
                stream.Write(bytes, 0, bytes.Length);
                // Durability: the temp file's data must reach disk before the
                // rename, or a power loss could persist the rename ahead of the
                // data and leave an empty/truncated manifest.json.
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: a stray .tmp file is harmless.
        }
    }
}
