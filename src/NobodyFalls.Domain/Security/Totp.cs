using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NobodyFalls.Domain.Security;

/// <summary>
/// Time-based one-time passwords (RFC 6238, HMAC-SHA1, 6 digits, 30-second steps), the
/// format every authenticator app reads. Small enough to own, and tested against the
/// RFC's vectors (ADR 0018). Codes follow real time, never the simulated game clock.
/// </summary>
public static class Totp
{
    public const int Digits = 6;
    public const int StepSeconds = 30;
    public const int SecretBytes = 20;

    /// <summary>Steps accepted on each side of now, for clocks that drift a little.</summary>
    public const int Window = 1;

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>The code for a time step, with <paramref name="digits"/> digits (6 in the game, 8 in the RFC vectors).</summary>
    public static string Code(ReadOnlySpan<byte> secret, long step, int digits = Digits)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xFF);
            step >>= 8;
        }

        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // RFC 6238 and authenticator apps use HMAC-SHA1; it is not used for collision resistance.
        HMACSHA1.HashData(secret, counter, hash);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var modulo = (int)Math.Pow(10, digits);
        return (binary % modulo).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    /// <summary>
    /// The time step matched by <paramref name="code"/>, or null. A step at or before
    /// <paramref name="lastUsedStep"/> is refused: a code works once (replay protection).
    /// </summary>
    public static long? Verify(ReadOnlySpan<byte> secret, string? code, DateTimeOffset now, long? lastUsedStep)
    {
        var normalized = Normalize(code);
        if (normalized is null)
        {
            return null;
        }

        var current = StepAt(now);
        for (var step = current - Window; step <= current + Window; step++)
        {
            if (lastUsedStep is { } last && step <= last)
            {
                continue;
            }

            var expected = Code(secret, step);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(normalized)))
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>The <c>otpauth://</c> link that authenticator apps read from a QR code.</summary>
    public static string ProvisioningUri(ReadOnlySpan<byte> secret, string account, string issuer = "NobodyFalls")
    {
        var label = Uri.EscapeDataString($"{issuer}:{account}");
        return $"otpauth://totp/{label}?secret={Base32.Encode(secret)}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    private static string? Normalize(string? code)
    {
        if (code is null)
        {
            return null;
        }

        var digits = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return digits.Length == Digits && digits.All(char.IsAsciiDigit) ? digits : null;
    }
}

/// <summary>RFC 4648 base32 without padding, the encoding of TOTP secrets.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return output.ToString();
    }

    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var output = new List<byte>(text.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in text.TrimEnd('=').ToUpperInvariant())
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException($"'{c}' is not a base32 character.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
