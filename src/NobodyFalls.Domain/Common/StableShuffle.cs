using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NobodyFalls.Domain.Common;

/// <summary>
/// A per-day pseudo-random key, identical in C# and in SQL, used to break ties
/// in lists (the Edge, Watcher assignment) so that help is spread out instead of
/// always landing on the same players.
/// </summary>
/// <remarks>
/// SQL equivalent:
/// <c>('x' || substr(md5(day::text || ':' || player_id::text), 1, 16))::bit(64)::bigint</c>.
/// </remarks>
public static class StableShuffle
{
    public static long Key(GameDay day, PlayerId player)
    {
        var input = Encoding.UTF8.GetBytes($"{day}:{player}");
#pragma warning disable CA5351 // Not a security use: MD5 is chosen because PostgreSQL has it built in.
        var hash = MD5.HashData(input);
#pragma warning restore CA5351
        return BinaryPrimitives.ReadInt64BigEndian(hash);
    }
}
