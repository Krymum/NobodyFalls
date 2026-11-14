using System.Security.Cryptography;
using System.Text;

namespace NobodyFalls.Domain.Security;

/// <summary>A recovery code as stored: never the code itself, only a salted hash.</summary>
public sealed record StoredRecoveryCode(byte[] Salt, byte[] Hash);

/// <summary>
/// One-time codes for a team member who lost their authenticator (ADR 0019): ten codes
/// of 10 base32 characters (50 bits each), shown once, stored salted and hashed.
/// </summary>
public static class RecoveryCodes
{
    public const int Count = 10;
    private const int Length = 10;

    public static IReadOnlyList<string> Generate()
    {
        var codes = new List<string>(Count);
        for (var i = 0; i < Count; i++)
        {
            var text = Base32.Encode(RandomNumberGenerator.GetBytes(7))[..Length].ToLowerInvariant();
            codes.Add($"{text[..5]}-{text[5..]}");
        }

        return codes;
    }

    public static StoredRecoveryCode Protect(string code)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new StoredRecoveryCode(salt, HashOf(salt, Normalize(code)));
    }

    public static bool Matches(StoredRecoveryCode stored, string? candidate)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return candidate is not null
            && CryptographicOperations.FixedTimeEquals(stored.Hash, HashOf(stored.Salt, Normalize(candidate)));
    }

    /// <summary>Case, spaces and dashes do not matter when typing a code.</summary>
    private static string Normalize(string code) =>
        new(code.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static byte[] HashOf(byte[] salt, string code) =>
        SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(code)]);
}
