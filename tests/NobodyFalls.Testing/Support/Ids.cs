using System.Security.Cryptography;
using System.Text;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Testing;

/// <summary>Readable, deterministic player ids for tests: <c>Ids.Of("lea")</c>.</summary>
public static class Ids
{
    public static PlayerId Of(string name)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(name)).AsSpan(0, 16);
        return new PlayerId(new Guid(bytes));
    }

    public static PlayerId Numbered(int n) =>
        new(Guid.Parse($"00000000-0000-0000-0000-{n:D12}"));
}
