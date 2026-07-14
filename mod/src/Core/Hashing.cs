using System;
using System.Security.Cryptography;
using System.Text;

namespace Sts2Recorder.Core;

internal static class Hashing
{
    /// <summary>First 16 lowercase hex chars of the SHA-256 of the serialized state JSON.</summary>
    public static string StateHash(string serializedStateJson)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(serializedStateJson));
        return Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
    }
}
