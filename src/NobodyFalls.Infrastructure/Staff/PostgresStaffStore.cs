using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Staff;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Infrastructure.Audit;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Staff;

/// <summary>
/// The team. Each change locks the team rows, applies the domain rules (no self-edit,
/// never zero admins) and writes its audit entry in the same transaction.
/// </summary>
internal sealed class PostgresStaffStore(NpgsqlDataSource dataSource, TimeProvider time, StaffAccessCache cache) : IStaffStore
{
    private const string MemberColumns =
        """
        m.player_id, p.pseudo, m.roles, m.granted_at,
        exists (select 1 from totp_credentials t where t.player_id = m.player_id and t.confirmed_at is not null)
        """;

    public Task<StaffAccess?> GetAccessAsync(PlayerId player, CancellationToken cancellationToken) =>
        cache.GetAsync(player, async ct =>
        {
            await using var command = dataSource.CreateCommand(
                $"select {MemberColumns} from staff_members m join players p on p.id = m.player_id where m.player_id = @id and p.deleted_at is null");
            command.Parameters.AddWithValue("id", player.Value);
            await using var r = await command.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
            {
                return null;
            }

            var member = ReadMember(r);
            return new StaffAccess(member.PlayerId, member.Pseudo, member.Roles, member.TotpEnrolled);
        }, cancellationToken);

    public async Task<IReadOnlyList<StaffMember>> ListAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"select {MemberColumns} from staff_members m join players p on p.id = m.player_id order by p.pseudo_key");
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        var members = new List<StaffMember>();
        while (await r.ReadAsync(cancellationToken))
        {
            members.Add(ReadMember(r));
        }

        return members;
    }

    public async Task<Result<StaffMember>> AddAsync(string pseudo, IReadOnlyList<StaffRole> roles, AuditActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var target = await FindHumanAsync(tx, pseudo, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error!;
        }

        if (actor.Id == target.Value)
        {
            return StaffErrors.Self;
        }

        var inserted = await tx.ExecuteAsync(
            "insert into staff_members (player_id, roles, granted_by, granted_at) values (@id, @roles, @by, @now) on conflict do nothing",
            cancellationToken,
            ("id", target.Value.Value),
            ("roles", Names(roles)),
            ("by", actor.Id?.Value),
            ("now", Sql.Utc(time.GetUtcNow())));
        if (inserted == 0)
        {
            return StaffErrors.AlreadyMember;
        }

        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(actor, AuditActions.StaffAdded, AuditTargets.Player, target.Value.ToString(), After: new { roles = Names(roles) }),
            time.GetUtcNow(),
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        cache.Forget(target.Value);
        return await GetMemberAsync(target.Value, cancellationToken);
    }

    public async Task<Result<StaffMember>> ChangeRolesAsync(PlayerId target, IReadOnlyList<StaffRole> roles, AuditActor actor, CancellationToken cancellationToken)
    {
        var result = await ChangeAsync(target, roles, actor, AuditActions.StaffRolesChanged, cancellationToken);
        return result.IsFailure ? result.Error! : await GetMemberAsync(target, cancellationToken);
    }

    public Task<Result> RemoveAsync(PlayerId target, AuditActor actor, CancellationToken cancellationToken) =>
        ChangeAsync(target, [], actor, AuditActions.StaffRemoved, cancellationToken);

    public async Task<Result<StaffMember>> BootstrapAdminAsync(string pseudo, string reason, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await LockTeamAsync(tx, cancellationToken);
        if (await AdminCountAsync(tx, cancellationToken) > 0)
        {
            return new DomainError("staff.admin_exists", "An admin already exists: grant roles from the admin.");
        }

        var target = await FindHumanAsync(tx, pseudo, cancellationToken);
        if (target.IsFailure)
        {
            return target.Error!;
        }

        string[] roles = [StaffRole.Admin.Name()];
        await tx.ExecuteAsync(
            """
            insert into staff_members (player_id, roles, granted_at) values (@id, @roles, @now)
            on conflict (player_id) do update set roles = excluded.roles
            """,
            cancellationToken,
            ("id", target.Value.Value),
            ("roles", roles),
            ("now", Sql.Utc(time.GetUtcNow())));
        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(AuditActor.System, AuditActions.StaffBootstrapped, AuditTargets.Player, target.Value.ToString(), After: new { roles }, Reason: reason),
            time.GetUtcNow(),
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        cache.Forget(target.Value);
        return await GetMemberAsync(target.Value, cancellationToken);
    }

    private async Task<Result> ChangeAsync(PlayerId target, IReadOnlyList<StaffRole> roles, AuditActor actor, string action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await LockTeamAsync(tx, cancellationToken);

        var current = await tx.ScalarAsync<string[]>(
            "select roles from staff_members where player_id = @id", cancellationToken, ("id", target.Value));
        if (current is null)
        {
            return StaffErrors.NotFound;
        }

        var currentRoles = current.Select(StaffRoles.Parse).OfType<StaffRole>().ToList();
        var check = StaffRoles.CanChange(actor.Id ?? default, target, currentRoles, roles, await AdminCountAsync(tx, cancellationToken));
        if (check.IsFailure)
        {
            return check;
        }

        if (roles.Count == 0)
        {
            await tx.ExecuteAsync("delete from staff_members where player_id = @id", cancellationToken, ("id", target.Value));
        }
        else
        {
            await tx.ExecuteAsync(
                "update staff_members set roles = @roles where player_id = @id",
                cancellationToken,
                ("id", target.Value),
                ("roles", Names(roles)));
        }

        await AuditWriter.AppendAsync(
            tx,
            new AuditRecord(
                actor,
                action,
                AuditTargets.Player,
                target.ToString(),
                Before: new { roles = current },
                After: roles.Count == 0 ? null : new { roles = Names(roles) }),
            time.GetUtcNow(),
            cancellationToken);
        await tx.CommitAsync(cancellationToken);
        cache.Forget(target);
        return Result.Success();
    }

    private async Task<Result<StaffMember>> GetMemberAsync(PlayerId player, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"select {MemberColumns} from staff_members m join players p on p.id = m.player_id where m.player_id = @id");
        command.Parameters.AddWithValue("id", player.Value);
        await using var r = await command.ExecuteReaderAsync(cancellationToken);
        return await r.ReadAsync(cancellationToken) ? Result.Success(ReadMember(r)) : StaffErrors.NotFound;
    }

    private static async Task<Result<PlayerId>> FindHumanAsync(NpgsqlTransaction tx, string pseudo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pseudo))
        {
            return StaffErrors.PlayerNotFound;
        }

        await using var r = await tx.ReaderAsync(
            "select id, kind from players where pseudo_key = @key and deleted_at is null",
            cancellationToken,
            ("key", Pseudo.FromTrusted(pseudo.Trim()).Key));
        if (!await r.ReadAsync(cancellationToken))
        {
            return StaffErrors.PlayerNotFound;
        }

        return r.GetString(1) == DbValues.Human ? Result.Success(new PlayerId(r.GetGuid(0))) : StaffErrors.NotHuman;
    }

    /// <summary>Team changes are rare: one lock serializes them, so "never zero admins" holds under concurrency.</summary>
    private static async Task LockTeamAsync(NpgsqlTransaction tx, CancellationToken cancellationToken) =>
        await tx.ExecuteAsync("lock table staff_members in share row exclusive mode", cancellationToken);

    private static async Task<int> AdminCountAsync(NpgsqlTransaction tx, CancellationToken cancellationToken) =>
        (int)(await tx.ScalarAsync<long>("select count(*) from staff_members where 'admin' = any(roles)", cancellationToken));

    private static string[] Names(IEnumerable<StaffRole> roles) => [.. roles.Select(r => r.Name())];

    private static StaffMember ReadMember(NpgsqlDataReader r) => new(
        new PlayerId(r.GetGuid(0)),
        r.GetString(1),
        [.. r.GetFieldValue<string[]>(2).Select(StaffRoles.Parse).OfType<StaffRole>().Order()],
        r.GetBoolean(4),
        r.GetFieldValue<DateTimeOffset>(3));
}

/// <summary>
/// Team access is read on every admin request; a short cache spares the database, and
/// changes made by this instance forget the entry at once (ADR 0018).
/// </summary>
internal sealed class StaffAccessCache(TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(15);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<PlayerId, (StaffAccess? Access, DateTimeOffset Until)> _entries = new();

    public async Task<StaffAccess?> GetAsync(PlayerId player, Func<CancellationToken, Task<StaffAccess?>> load, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(load);
        var now = time.GetUtcNow();
        if (_entries.TryGetValue(player, out var entry) && entry.Until > now)
        {
            return entry.Access;
        }

        var access = await load(cancellationToken);
        _entries[player] = (access, now + Lifetime);
        return access;
    }

    public void Forget(PlayerId player) => _entries.TryRemove(player, out _);
}
