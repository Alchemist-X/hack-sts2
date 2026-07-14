using System.IO;

namespace Sts2Recorder.Core;

/// <summary>Session directory layout: &lt;outputRoot&gt;/sessions/&lt;start&gt;-&lt;seed&gt;[-partN].</summary>
internal static class SessionPaths
{
    public const string NativeDirName = "native";

    /// <summary>
    /// Creates a fresh session directory. If the base run-id directory already exists
    /// (a resumed run recorded across a game restart), a -part2/-part3/... suffix is
    /// used and the part number is reported so the manifest can carry it.
    /// </summary>
    public static (string Dir, int Part) CreateSessionDirectory(string outputRoot, SessionMeta meta)
    {
        var sessionsRoot = Path.Combine(outputRoot, "sessions");
        Directory.CreateDirectory(sessionsRoot);
        var baseName = $"{meta.StartTime}-{meta.Seed}";
        var part = 1;
        var candidate = Path.Combine(sessionsRoot, baseName);
        while (Directory.Exists(candidate))
        {
            part++;
            candidate = Path.Combine(sessionsRoot, $"{baseName}-part{part}");
        }
        Directory.CreateDirectory(candidate);
        return (candidate, part);
    }
}
