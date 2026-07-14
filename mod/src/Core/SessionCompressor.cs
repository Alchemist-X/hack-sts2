using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Sts2Recorder.Core;

/// <summary>
/// Gzip-at-complete for the three JSONL streams. gzip (not Brotli) is a
/// deliberate tradeoff: Python reads gzip from the stdlib, so sts2rec keeps
/// its zero-dependency policy, which beats Brotli's ~15% ratio edge on JSONL.
/// The plain files are deleted ONLY after every stream passes a byte-exact
/// round-trip decompression check; on any failure the partial .gz files are
/// removed, the plain files are kept, and the error is reported to the
/// caller's error channel. manifest.json and native/ always stay plain.
/// Crash-terminated sessions never reach Complete(), so they stay plain by
/// design (docs/design.md, "Compression").
/// </summary>
public static class SessionCompressor
{
    /// <summary>Manifest "compression" value for gzip-packed streams.</summary>
    public const string Format = "gz";

    /// <summary>Suffix appended to a stream file name when compressed.</summary>
    public const string Suffix = ".gz";

    private static readonly string[] StreamFiles =
        { "states.jsonl", "actions.jsonl", "events.jsonl" };

    /// <summary>
    /// Compresses every existing stream file in <paramref name="sessionDir"/>
    /// to &lt;name&gt;.gz and deletes the plain originals only after all of
    /// them round-trip byte-identically. Returns true when the session is now
    /// compressed; false leaves every plain file untouched.
    /// </summary>
    public static bool TryCompressStreams(
        string sessionDir, Action<string, Exception>? onError = null)
    {
        var created = new List<string>();
        try
        {
            foreach (var name in StreamFiles)
            {
                var plain = Path.Combine(sessionDir, name);
                if (!File.Exists(plain))
                {
                    continue;
                }
                var compressed = plain + Suffix;
                created.Add(compressed);
                Compress(plain, compressed);
                if (!RoundTripMatches(plain, compressed))
                {
                    throw new InvalidDataException(
                        $"round-trip verification failed for {compressed}");
                }
            }
            // Every stream verified: only now is it safe to drop the plain files.
            foreach (var name in StreamFiles)
            {
                var plain = Path.Combine(sessionDir, name);
                if (File.Exists(plain) && File.Exists(plain + Suffix))
                {
                    File.Delete(plain);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke("SessionCompressor.TryCompressStreams", ex);
            foreach (var compressed in created)
            {
                TryDelete(compressed);
            }
            return false;
        }
    }

    private static void Compress(string sourcePath, string targetPath)
    {
        using var input = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(
            targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            input.CopyTo(gzip);
        }
        // Durability before the plain file is deleted: the compressed bytes
        // must be on disk, or a crash could lose both representations.
        output.Flush(flushToDisk: true);
    }

    /// <summary>Byte-exact comparison of the plain file vs the decompressed .gz.</summary>
    private static bool RoundTripMatches(string plainPath, string compressedPath)
    {
        using var plain = new FileStream(
            plainPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var compressed = new FileStream(
            compressedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var decompressed = new GZipStream(compressed, CompressionMode.Decompress);

        var bufferA = new byte[81920];
        var bufferB = new byte[81920];
        while (true)
        {
            var readA = ReadFully(plain, bufferA);
            var readB = ReadFully(decompressed, bufferB);
            if (readA != readB)
            {
                return false;
            }
            if (readA == 0)
            {
                return true;
            }
            if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
            {
                return false;
            }
        }
    }

    /// <summary>Fills the buffer as far as the stream allows (Read may return short).</summary>
    private static int ReadFully(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort: a stray .gz next to intact plain files is harmless
            // (tooling prefers .gz only when it verifies as readable), and the
            // cleanup path must never mask the original compression error.
        }
    }
}
