using System.Security.Cryptography;

namespace Nyvora.Core.Updates;

public static class Checksum
{
    /// <summary>Reads the hash from "&lt;sha256&gt;  &lt;filename&gt;" (sha256sum / Get-FileHash style); null if malformed.</summary>
    public static string? ParseExpected(string text)
    {
        var token = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return token is { Length: 64 } && token.All(Uri.IsHexDigit) ? token.ToLowerInvariant() : null;
    }

    public static async Task<string> Sha256HexAsync(Stream stream, CancellationToken ct = default) =>
        Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
}
