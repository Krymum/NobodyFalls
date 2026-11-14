using System.Globalization;
using System.Text;

namespace NobodyFalls.Domain.Common;

/// <summary>
/// A player's public name. 3 to 20 characters: letters (any script), digits,
/// <c>_</c> and <c>.</c>, with at least one letter. Uniqueness is checked on
/// <see cref="Key"/>, which ignores case and accents so that "Léa_M" and
/// "lea_m" cannot coexist.
/// </summary>
public sealed record Pseudo
{
    public const int MinLength = 3;
    public const int MaxLength = 20;

    private Pseudo(string value, string key)
    {
        Value = value;
        Key = key;
    }

    /// <summary>The name as the player typed it (trimmed, NFC-normalized).</summary>
    public string Value { get; }

    /// <summary>Case- and accent-insensitive form used for uniqueness.</summary>
    public string Key { get; }

    public static Result<Pseudo> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return PseudoErrors.Length;
        }

        var value = raw.Trim().Normalize(NormalizationForm.FormC);
        var length = new StringInfo(value).LengthInTextElements;
        if (length is < MinLength or > MaxLength)
        {
            return PseudoErrors.Length;
        }

        var hasLetter = false;
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                hasLetter = true;
            }
            else if (!IsAllowedNonLetter(rune))
            {
                return PseudoErrors.Characters;
            }
        }

        return hasLetter
            ? Result.Success(new Pseudo(value, ToKey(value)))
            : PseudoErrors.Characters;
    }

    /// <summary>Rehydrates a pseudo that was validated before being stored.</summary>
    public static Pseudo FromTrusted(string value) => new(value, ToKey(value));

    public override string ToString() => Value;

    private static bool IsAllowedNonLetter(Rune rune) =>
        Rune.IsDigit(rune)
        || rune.Value == '_'
        || rune.Value == '.'
        || Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark;

    private static string ToKey(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var rune in decomposed.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(Rune.ToLowerInvariant(rune).ToString());
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

public static class PseudoErrors
{
    public static readonly DomainError Length =
        new("pseudo.length", $"A pseudo has {Pseudo.MinLength} to {Pseudo.MaxLength} characters.");

    public static readonly DomainError Characters =
        new("pseudo.characters", "A pseudo uses letters, digits, '_' and '.', with at least one letter.");
}
