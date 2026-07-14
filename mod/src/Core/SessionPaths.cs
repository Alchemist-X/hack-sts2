using System.IO;

namespace Sts2Recorder.Core;

/// <summary>Session directory layout: &lt;outputRoot&gt;/sessions/&lt;start&gt;-&lt;seed&gt;[-partN].</summary>
internal static class SessionPaths
{
    public const string NativeDirName = "native";

    /// <summary>
    /// Name of the zero-byte claim marker created atomically (O_EXCL semantics)
    /// inside a session directory. Owning the claim file = owning the directory.
    /// </summary>
    public const string ClaimFileName = ".claim";

    private const int MaxParts = 10_000;

    /// <summary>
    /// Creates and atomically claims a fresh session directory. If the base
    /// run-id directory is already taken (a resumed run recorded across a game
    /// restart, or a concurrent Begin racing for the same run id), a
    /// -part2/-part3/... suffix is used and the part number is reported so the
    /// manifest can carry it.
    ///
    /// Concurrency contract: Directory.CreateDirectory succeeds on an existing
    /// directory, so directory creation alone cannot claim a path. The claim is
    /// FileMode.CreateNew on <see cref="ClaimFileName"/> — exactly one caller
    /// wins each part number; losers retry with the next part.
    /// </summary>
    public static (string Dir, int Part) CreateSessionDirectory(string outputRoot, SessionMeta meta)
    {
        var sessionsRoot = Path.Combine(outputRoot, "sessions");
        Directory.CreateDirectory(sessionsRoot);
        var baseName = $"{meta.StartTime}-{meta.Seed}";
        for (var part = 1; part <= MaxParts; part++)
        {
            var name = part == 1 ? baseName : $"{baseName}-part{part}";
            var candidate = Path.Combine(sessionsRoot, name);
            if (Directory.Exists(candidate))
            {
                // Taken by an earlier session (resume) or a pre-claim-era dir.
                continue;
            }
            Directory.CreateDirectory(candidate);
            if (TryClaim(candidate))
            {
                return (candidate, part);
            }
            // Lost a concurrent race for this part number; try the next one.
        }
        throw new IOException(
            $"could not claim a session directory for run id '{baseName}' "
            + $"after {MaxParts} attempts");
    }

    private static bool TryClaim(string directory)
    {
        var claimPath = Path.Combine(directory, ClaimFileName);
        try
        {
            using var claim = new FileStream(
                claimPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            // CreateNew failed: another Begin() claimed this directory first.
            return false;
        }
    }
}
