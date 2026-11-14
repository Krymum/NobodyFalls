using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using NobodyFalls.Application.Push;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Push;

public sealed class PushOptions
{
    public const string Section = "Push";

    /// <summary>VAPID public key (base64url, uncompressed P-256 point). Empty: generated once and kept in the database.</summary>
    public string PublicKey { get; set; } = "";

    public string PrivateKey { get; set; } = "";

    /// <summary>Contact for push services, e.g. <c>mailto:contact@nobodyfalls.com</c>.</summary>
    public string Subject { get; set; } = "mailto:contact@nobodyfalls.com";
}

public sealed record VapidKeyPair(string PublicKey, string PrivateKey);

/// <summary>
/// VAPID keys identify the server to browser push services. Production sets them in
/// configuration; development generates a pair once and keeps it in <c>vapid_keys</c>,
/// so that subscriptions survive restarts.
/// </summary>
internal sealed class VapidKeyStore(NpgsqlDataSource dataSource, IOptions<PushOptions> options, TimeProvider time) : IPushKeys
{
    private VapidKeyPair? _keys;

    public async Task<VapidKeyPair> GetAsync(CancellationToken cancellationToken)
    {
        if (_keys is not null)
        {
            return _keys;
        }

        if (!string.IsNullOrEmpty(options.Value.PublicKey) && !string.IsNullOrEmpty(options.Value.PrivateKey))
        {
            return _keys = new VapidKeyPair(options.Value.PublicKey, options.Value.PrivateKey);
        }

        var generated = Generate();
        await using (var insert = dataSource.CreateCommand(
                         "insert into vapid_keys (id, public_key, private_key, created_at) values (1, @public, @private, @now) on conflict do nothing"))
        {
            insert.Parameters.AddWithValue("public", generated.PublicKey);
            insert.Parameters.AddWithValue("private", generated.PrivateKey);
            insert.Parameters.AddWithValue("now", Sql.Utc(time.GetUtcNow()));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        // Another instance may have won the race: always use what is stored.
        await using var select = dataSource.CreateCommand("select public_key, private_key from vapid_keys where id = 1");
        await using var r = await select.ExecuteReaderAsync(cancellationToken);
        await r.ReadAsync(cancellationToken);
        return _keys = new VapidKeyPair(r.GetString(0), r.GetString(1));
    }

    public async Task<string> PublicKeyAsync(CancellationToken cancellationToken) =>
        (await GetAsync(cancellationToken)).PublicKey;

    public static VapidKeyPair Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        var publicKey = new byte[65];
        publicKey[0] = 0x04;
        parameters.Q.X!.CopyTo(publicKey, 1);
        parameters.Q.Y!.CopyTo(publicKey, 33);
        return new VapidKeyPair(Base64Url(publicKey), Base64Url(parameters.D!));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
