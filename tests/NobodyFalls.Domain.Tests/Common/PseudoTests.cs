using NobodyFalls.Domain.Common;
using NobodyFalls.Testing;

namespace NobodyFalls.Domain.Tests.Common;

public sealed class PseudoTests
{
    [Theory]
    [InlineData("léa_m")]
    [InlineData("tom.b")]
    [InlineData("Kenji")]
    [InlineData("ユキ123")]
    [InlineData("abc")]
    [InlineData("abcdefghijklmnopqrst")]
    public void Accepts_letters_digits_underscore_and_dot(string raw) =>
        Assert.True(Pseudo.Create(raw).IsSuccess);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("abcdefghijklmnopqrstu")]
    public void Rejects_bad_lengths(string? raw) =>
        Assert.Equal(PseudoErrors.Length, Pseudo.Create(raw).Error);

    [Theory]
    [InlineData("léa m")]
    [InlineData("tom-b")]
    [InlineData("<script>")]
    [InlineData("1234")]
    [InlineData("___")]
    public void Rejects_other_characters_and_names_without_letters(string raw) =>
        Assert.Equal(PseudoErrors.Characters, Pseudo.Create(raw).Error);

    [Theory]
    [InlineData("veilleur.01", true)]
    [InlineData("Veilleur_X", true)]
    [InlineData("VÉILLEUR", true)]
    [InlineData("le_veilleur", false)]
    public void Watcher_pseudos_are_reserved(string raw, bool reserved) =>
        Assert.Equal(reserved, Pseudo.Create(raw).Value.IsReservedForWatchers);

    [Fact]
    public void Surrounding_spaces_are_trimmed() =>
        Assert.Equal("yuki", Pseudo.Create("  yuki ").Value.Value);

    [Fact]
    public void The_key_ignores_case_and_accents()
    {
        var a = Pseudo.Create("Léa_M").Value;
        var b = Pseudo.Create("lea_m").Value;

        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Value, b.Value);
    }

    [Fact]
    public void Composed_and_decomposed_forms_are_the_same_name()
    {
        var composed = Pseudo.Create("léa").Value;
        var decomposed = Pseudo.Create("léa").Value;

        Assert.Equal(composed.Value, decomposed.Value);
    }

    [Fact]
    public void Player_ids_sort_like_postgres_uuids()
    {
        // PostgreSQL compares uuids byte by byte, which is the order of their text form.
        var ids = Enumerable.Range(0, 200).Select(_ => new PlayerId(Guid.NewGuid())).ToList();

        var byDomain = ids.Order().Select(i => i.ToString());
        var byText = ids.Select(i => i.ToString()).Order(StringComparer.Ordinal);

        Assert.Equal(byText, byDomain);
        Assert.True(Ids.Numbered(1) < Ids.Numbered(2));
    }
}
