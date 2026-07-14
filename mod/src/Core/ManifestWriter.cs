using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sts2Recorder.Core;

/// <summary>Atomic manifest.json writes: write a temp file, then move over the target.</summary>
internal static class ManifestWriter
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public const string ManifestName = "manifest.json";

    public static void WriteAtomic(string sessionDir, JsonObject manifest)
    {
        var target = Path.Combine(sessionDir, ManifestName);
        var temp = target + ".tmp";
        File.WriteAllText(temp, manifest.ToJsonString(Indented) + "\n", Utf8NoBom);
        File.Move(temp, target, overwrite: true);
    }
}
