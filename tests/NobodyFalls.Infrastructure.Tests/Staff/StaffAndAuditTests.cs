using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Staff;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Security;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Infrastructure.Tests.Support;
using NobodyFalls.Testing;
using NobodyFalls.Testing.Postgres;
using Npgsql;

namespace NobodyFalls.Infrastructure.Tests.Staff;

/// <summary>The team, two-factor sign-in and the append-only, hash-chained audit log (ADR 0018–0020).</summary>
public sealed class StaffAndAuditTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly PlayerId Boss = Ids.Of("boss");
    private static readonly PlayerId Mod = Ids.Of("mod");
    private static readonly PlayerId Lea = Ids.Of("lea");

    [Fact]
    public async Task The_audit_log_refuses_updates_deletes_and_truncation()
    {
        await using var app = await StartAsync();
        await app.Get<IAuditLog>().AppendAsync(Record("test.action"), Ct);

        foreach (var sql in new[] { "update audit_log set action = 'x'", "delete from audit_log", "truncate audit_log" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => app.Seed.ExecAsync(sql));
            Assert.Contains("append-only", error.MessageText, StringComparison.Ordinal);
        }

        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from audit_log"));
    }

    [Fact]
    public async Task Each_entry_chains_to_the_previous_one_and_the_chain_verifies()
    {
        await using var app = await StartAsync();
        var audit = app.Get<IAuditLog>();
        await audit.AppendAsync(Record("first", before: new { a = 1 }, after: new { a = 2, note = "ünïcode | pipes" }), Ct);
        await audit.AppendAsync(Record("second", reason: "because"), Ct);
        await audit.AppendAsync(Record("third"), Ct);

        var verification = await audit.VerifyAsync(Ct);

        Assert.Equal(new AuditVerification(true, 3, null), verification);
        Assert.True(await app.Seed.ScalarAsync<bool>("select prev_hash is null from audit_log order by id limit 1"));
    }

    [Fact]
    public async Task Tampering_by_a_database_superuser_is_detected()
    {
        await using var app = await StartAsync();
        var audit = app.Get<IAuditLog>();
        await audit.AppendAsync(Record("staff.added", after: new { roles = new[] { "support" } }), Ct);
        await audit.AppendAsync(Record("staff.roles_changed"), Ct);
        await audit.AppendAsync(Record("staff.removed"), Ct);
        var second = await app.Seed.ScalarAsync<long>("select id from audit_log order by id offset 1 limit 1");

        // Someone with full rights disables the trigger and rewrites history.
        await app.Seed.ExecAsync(
            """
            alter table audit_log disable trigger audit_log_no_update_or_delete;
            update audit_log set after = '{"roles":["admin"]}' where id = @id;
            alter table audit_log enable trigger audit_log_no_update_or_delete;
            """,
            ("id", second));

        var verification = await audit.VerifyAsync(Ct);

        Assert.False(verification.Valid);
        Assert.Equal(second, verification.FirstBrokenId);
    }

    [Fact]
    public async Task Concurrent_appends_keep_a_single_valid_chain()
    {
        await using var app = await StartAsync();
        var audit = app.Get<IAuditLog>();

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => audit.AppendAsync(Record($"parallel.{i}"), Ct)));

        Assert.Equal(new AuditVerification(true, 40, null), await audit.VerifyAsync(Ct));
        Assert.Equal(39L, await app.Seed.ScalarAsync<long>("select count(distinct prev_hash) from audit_log"));
    }

    [Fact]
    public async Task Audit_pages_filter_by_action_prefix_and_continue_with_a_cursor()
    {
        await using var app = await StartAsync();
        var audit = app.Get<IAuditLog>();
        for (var i = 0; i < 5; i++)
        {
            await audit.AppendAsync(Record("staff.added"), Ct);
        }

        await audit.AppendAsync(Record("staffing.other"), Ct);

        var first = await audit.ListAsync(new AuditQuery(Action: "staff", Limit: 3), Ct);
        var second = await audit.ListAsync(new AuditQuery(Action: "staff", Limit: 3, BeforeId: first.NextBeforeId), Ct);

        Assert.Equal(3, first.Entries.Count);
        Assert.NotNull(first.NextBeforeId);
        Assert.Equal(2, second.Entries.Count);
        Assert.Null(second.NextBeforeId);
        Assert.All(first.Entries.Concat(second.Entries), e => Assert.Equal("staff.added", e.Action));
        Assert.Equal("boss", (await audit.ListAsync(new AuditQuery(Limit: 1), Ct)).Entries[0].ActorPseudo);
    }

    [Fact]
    public async Task Team_changes_are_audited_in_the_same_transaction()
    {
        await using var app = await StartAsync();
        var staff = app.Get<IStaffStore>();
        await staff.BootstrapAdminAsync(PseudoOf(Boss), "first admin", Ct);

        var added = await staff.AddAsync(PseudoOf(Mod), [StaffRole.Moderator], Actor(Boss), Ct);
        await staff.ChangeRolesAsync(Mod, [StaffRole.Moderator, StaffRole.Support], Actor(Boss), Ct);
        await staff.RemoveAsync(Mod, Actor(Boss), Ct);

        Assert.True(added.IsSuccess);
        var actions = await app.Seed.QueryAsync("select action from audit_log order by id", r => r.GetString(0));
        Assert.Equal(["staff.bootstrapped", "staff.added", "staff.roles_changed", "staff.removed"], actions);
        Assert.Null(await staff.GetAccessAsync(Mod, Ct));
        Assert.True((await app.Get<IAuditLog>().VerifyAsync(Ct)).Valid);
    }

    [Fact]
    public async Task Bootstrap_works_only_while_there_is_no_admin()
    {
        await using var app = await StartAsync();
        var staff = app.Get<IStaffStore>();

        Assert.True((await staff.BootstrapAdminAsync(PseudoOf(Boss), "first", Ct)).IsSuccess);
        Assert.Equal("staff.admin_exists", (await staff.BootstrapAdminAsync(PseudoOf(Mod), "second", Ct)).Error?.Code);
    }

    [Fact]
    public async Task The_last_admin_stays_and_nobody_edits_themselves()
    {
        await using var app = await StartAsync();
        var staff = app.Get<IStaffStore>();
        await staff.BootstrapAdminAsync(PseudoOf(Boss), "first", Ct);
        await staff.AddAsync(PseudoOf(Mod), [StaffRole.Admin], Actor(Boss), Ct);

        Assert.Equal(StaffErrors.Self, (await staff.RemoveAsync(Boss, Actor(Boss), Ct)).Error);
        Assert.True((await staff.RemoveAsync(Mod, Actor(Boss), Ct)).IsSuccess);
        Assert.Equal(StaffErrors.LastAdmin, (await staff.ChangeRolesAsync(Boss, [StaffRole.Support], Actor(Mod), Ct)).Error);
        Assert.Equal(StaffErrors.AlreadyMember, (await staff.AddAsync(PseudoOf(Boss), [StaffRole.Support], Actor(Mod), Ct)).Error);
        Assert.Equal(StaffErrors.PlayerNotFound, (await staff.AddAsync("nobody-here", [StaffRole.Support], Actor(Boss), Ct)).Error);
    }

    [Fact]
    public async Task Enrolment_then_sign_in_with_a_code_that_works_once()
    {
        await using var app = await StartAsync();
        var (auth, member, time) = await EnrolledAsync(app);

        var secret = await SecretAsync(app);
        time.Advance(TimeSpan.FromSeconds(30));
        var code = Totp.Code(secret, Totp.StepAt(time.GetUtcNow()));

        Assert.Equal(AdminSignInMethod.Authenticator, (await auth.SignInAsync(member, code, null, Actor(Boss), Ct)).Value);
        Assert.Equal(AdminErrors.TotpInvalid, (await auth.SignInAsync(member, code, null, Actor(Boss), Ct)).Error);
    }

    [Fact]
    public async Task A_recovery_code_works_once()
    {
        await using var app = await StartAsync();
        var (auth, member, _, codes) = await EnrolledWithCodesAsync(app);

        Assert.Equal(AdminSignInMethod.RecoveryCode, (await auth.SignInAsync(member, null, codes[3], Actor(Boss), Ct)).Value);
        Assert.Equal(AdminErrors.TotpInvalid, (await auth.SignInAsync(member, null, codes[3], Actor(Boss), Ct)).Error);
        Assert.Equal(9L, await app.Seed.ScalarAsync<long>("select count(*) from totp_recovery_codes where used_at is null"));
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_authenticator_for_fifteen_minutes()
    {
        await using var app = await StartAsync();
        var (auth, member, time) = await EnrolledAsync(app);
        var secret = await SecretAsync(app);

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(AdminErrors.TotpInvalid, (await auth.SignInAsync(member, "000000", null, Actor(Boss), Ct)).Error);
        }

        Assert.Equal(AdminErrors.TotpLocked, (await auth.SignInAsync(member, "000000", null, Actor(Boss), Ct)).Error);
        time.Advance(TimeSpan.FromSeconds(30));
        var good = Totp.Code(secret, Totp.StepAt(time.GetUtcNow()));
        Assert.Equal(AdminErrors.TotpLocked, (await auth.SignInAsync(member, good, null, Actor(Boss), Ct)).Error);

        time.Advance(TimeSpan.FromMinutes(15));
        good = Totp.Code(secret, Totp.StepAt(time.GetUtcNow()));
        Assert.True((await auth.SignInAsync(member, good, null, Actor(Boss), Ct)).IsSuccess);

        var actions = await app.Seed.QueryAsync("select action from audit_log where action like 'admin.%' order by id", r => r.GetString(0));
        Assert.Equal(4, actions.Count(a => a == AuditActions.SignInFailed));
        Assert.Equal(1, actions.Count(a => a == AuditActions.SignInLocked));
        Assert.Equal(AuditActions.SignedIn, actions[^1]);
    }

    [Fact]
    public async Task Secrets_are_encrypted_at_rest()
    {
        await using var app = await StartAsync();
        var (_, member, _) = await EnrolledAsync(app);

        var stored = await app.Seed.ScalarAsync<byte[]>("select secret from totp_credentials where player_id = @id", ("id", member.PlayerId.Value));

        Assert.NotNull(stored);
        Assert.NotEqual(Totp.SecretBytes, stored.Length);
    }

    [Fact]
    public async Task Resetting_an_authenticator_ends_the_members_sessions()
    {
        await using var app = await StartAsync();
        await EnrolledAsync(app);
        await app.Get<IStaffStore>().AddAsync(PseudoOf(Mod), [StaffRole.Admin], Actor(Boss), Ct);
        var before = await app.Get<ISessionVersions>().GetAsync(Boss, Ct);

        var reset = await app.Get<ITotpStore>().ResetAsync(Boss, Actor(Mod), Ct);

        Assert.True(reset.IsSuccess);
        Assert.Equal(before + 1, await app.Get<ISessionVersions>().GetAsync(Boss, Ct));
        Assert.False((await app.Get<IStaffStore>().GetAccessAsync(Boss, Ct))!.TotpEnrolled);
        Assert.Equal("staff.self", (await app.Get<ITotpStore>().ResetAsync(Mod, Actor(Mod), Ct)).Error?.Code);
    }

    private async Task<TestApp> StartAsync()
    {
        var app = await TestApp.StartAsync(postgres, Now, s => s.AddSingleton<TimeProvider>(new ManualTimeProvider(Now)));
        foreach (var player in new[] { Boss, Mod, Lea })
        {
            await app.Seed.PlayerAsync(player);
        }

        await app.Seed.ExecAsync("update players set pseudo = 'boss', pseudo_key = 'boss' where id = @id", ("id", Boss.Value));
        return app;
    }

    private static async Task<(AdminAuthentication Auth, StaffAccess Member, ManualTimeProvider Time)> EnrolledAsync(TestApp app)
    {
        var (auth, member, time, _) = await EnrolledWithCodesAsync(app);
        return (auth, member, time);
    }

    private static async Task<(AdminAuthentication Auth, StaffAccess Member, ManualTimeProvider Time, IReadOnlyList<string> Codes)> EnrolledWithCodesAsync(TestApp app)
    {
        await app.Get<IStaffStore>().BootstrapAdminAsync("boss", "first", Ct);
        var member = (await app.Get<IStaffStore>().GetAccessAsync(Boss, Ct))!;
        var time = (ManualTimeProvider)app.Get<TimeProvider>();
        using var scope = app.Get<IServiceScopeFactory>().CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<AdminAuthentication>();

        var enrollment = (await auth.BeginEnrollmentAsync(member, Ct)).Value;
        var code = Totp.Code(Base32.Decode(enrollment.Secret), Totp.StepAt(time.GetUtcNow()));
        var codes = (await auth.ConfirmEnrollmentAsync(member, code, Actor(Boss), Ct)).Value;
        return (auth, member, time, codes);
    }

    private static async Task<byte[]> SecretAsync(TestApp app)
    {
        var stored = await app.Seed.ScalarAsync<byte[]>("select secret from totp_credentials where player_id = @id", ("id", Boss.Value));
        return app.Get<ISecretProtector>().Unprotect(stored!);
    }

    private static string PseudoOf(PlayerId id) => id == Boss ? "boss" : "p" + id.Value.ToString("N")[12..];

    private static AuditActor Actor(PlayerId id) => new(id, [StaffRole.Admin], "203.0.113.7", "req-1");

    private static AuditRecord Record(string action, object? before = null, object? after = null, string? reason = null) =>
        new(Actor(Boss), action, AuditTargets.Player, Lea.ToString(), before, after, reason);
}
