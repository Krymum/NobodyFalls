using System.Text;
using NobodyFalls.Domain.Security;

namespace NobodyFalls.Domain.Tests.Security;

public sealed class TotpTests
{
    // RFC 6238, appendix B: the SHA-1 secret is the ASCII string "12345678901234567890".
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    [InlineData(20000000000L, "65353130")]
    public void Matches_the_rfc_6238_test_vectors(long unixSeconds, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));

        Assert.Equal(expected, Totp.Code(RfcSecret, step, digits: 8));
        Assert.Equal(expected[^6..], Totp.Code(RfcSecret, step));
    }

    [Fact]
    public void Accepts_the_current_code_and_one_step_of_drift_either_way()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepAt(now);

        Assert.Equal(step, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step), now, null));
        Assert.Equal(step - 1, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 1), now, null));
        Assert.Equal(step + 1, Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 1), now, null));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 2), now, null));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step + 2), now, null));
    }

    [Fact]
    public void A_code_works_only_once()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepAt(now);
        var code = Totp.Code(RfcSecret, step);

        Assert.Null(Totp.Verify(RfcSecret, code, now, lastUsedStep: step));
        Assert.Null(Totp.Verify(RfcSecret, Totp.Code(RfcSecret, step - 1), now, lastUsedStep: step));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12a456")]
    public void Refuses_malformed_codes(string? code) =>
        Assert.Null(Totp.Verify(RfcSecret, code, DateTimeOffset.UnixEpoch, null));

    [Fact]
    public void Spaces_typed_in_a_code_do_not_matter()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var code = Totp.Code(RfcSecret, Totp.StepAt(now));

        Assert.NotNull(Totp.Verify(RfcSecret, $"{code[..3]} {code[3..]}", now, null));
    }

    [Fact]
    public void Provisioning_uri_carries_the_base32_secret_and_the_settings()
    {
        var uri = Totp.ProvisioningUri(RfcSecret, "léa");

        Assert.StartsWith("otpauth://totp/NobodyFalls%3Al%C3%A9a?", uri, StringComparison.Ordinal);
        Assert.Contains("secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", uri, StringComparison.Ordinal);
        Assert.Contains("digits=6&period=30", uri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_matches_rfc_4648(string text, string encoded)
    {
        Assert.Equal(encoded, Base32.Encode(Encoding.ASCII.GetBytes(text)));
        Assert.Equal(text, Encoding.ASCII.GetString(Base32.Decode(encoded.ToLowerInvariant())));
    }

    [Fact]
    public void Recovery_codes_are_ten_distinct_codes_that_match_only_themselves()
    {
        var codes = RecoveryCodes.Generate();

        Assert.Equal(RecoveryCodes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[a-z2-7]{5}-[a-z2-7]{5}$", c));
        var stored = RecoveryCodes.Protect(codes[0]);
        Assert.True(RecoveryCodes.Matches(stored, codes[0]));
        Assert.True(RecoveryCodes.Matches(stored, codes[0].ToUpperInvariant().Replace("-", " ", StringComparison.Ordinal)));
        Assert.False(RecoveryCodes.Matches(stored, codes[1]));
        Assert.False(RecoveryCodes.Matches(stored, null));
    }
}
