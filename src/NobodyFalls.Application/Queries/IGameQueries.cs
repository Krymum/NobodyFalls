using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Skins;

namespace NobodyFalls.Application.Queries;

/// <summary>
/// Read side of the game screens. Blocks are applied everywhere: two players who
/// blocked each other never see each other.
/// </summary>
public interface IGameQueries
{
    Task<SessionPlayer?> SessionAsync(PlayerId player, CancellationToken cancellationToken);

    Task<TonightView> TonightAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken);

    Task<EdgePage> EdgeAsync(PlayerId viewer, DateTimeOffset now, int offset, int limit, CancellationToken cancellationToken);

    Task<WorldView> WorldAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task<MyProfileView?> MyProfileAsync(PlayerId player, CancellationToken cancellationToken);

    /// <summary>Null when the player does not exist or a block stands between them.</summary>
    Task<PublicProfileView?> PublicProfileAsync(PlayerId viewer, PlayerId target, CancellationToken cancellationToken);

    Task<InvitePreview?> InviteAsync(string code, CancellationToken cancellationToken);

    Task<CatalogView> CatalogAsync(PlayerId? player, CancellationToken cancellationToken);
}

/// <summary>The skin catalogue, cached: it changes only through the admin.</summary>
public interface ISkinCatalogProvider
{
    Task<SkinCatalog> GetAsync(CancellationToken cancellationToken);
}
