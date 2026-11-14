using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using static NobodyFalls.Infrastructure.Persistence.DbValues;

namespace NobodyFalls.Infrastructure.Persistence;

/// <summary>
/// The schema, owned by EF Core migrations. Hot paths (giving hands, midnight) use
/// hand-written SQL on the same tables; EF serves the migrations and simple CRUD.
/// </summary>
internal sealed class NobodyFallsDbContext(DbContextOptions<NobodyFallsDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    /// <summary>Cookie encryption keys, shared by every instance and kept across restarts.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<PlayerRow> Players => Set<PlayerRow>();
    public DbSet<SeasonRow> Seasons => Set<SeasonRow>();
    public DbSet<GameDayRow> GameDays => Set<GameDayRow>();
    public DbSet<StormRow> Storms => Set<StormRow>();
    public DbSet<PlayerDayRow> PlayerDays => Set<PlayerDayRow>();
    public DbSet<HandRow> Hands => Set<HandRow>();
    public DbSet<LifeDebtRow> LifeDebts => Set<LifeDebtRow>();
    public DbSet<PlayerFallRow> PlayerFalls => Set<PlayerFallRow>();
    public DbSet<SeasonScoreRow> SeasonScores => Set<SeasonScoreRow>();
    public DbSet<PlayerStatsRow> PlayerStats => Set<PlayerStatsRow>();
    public DbSet<FriendshipRow> Friendships => Set<FriendshipRow>();
    public DbSet<BlockRow> Blocks => Set<BlockRow>();
    public DbSet<HoldPairRow> HoldPairs => Set<HoldPairRow>();
    public DbSet<SkinItemRow> SkinItems => Set<SkinItemRow>();
    public DbSet<SkinToneRow> SkinTones => Set<SkinToneRow>();
    public DbSet<PlayerSkinUnlockRow> PlayerSkinUnlocks => Set<PlayerSkinUnlockRow>();
    public DbSet<NotificationOutboxRow> NotificationOutbox => Set<NotificationOutboxRow>();
    public DbSet<GameSettingsRow> GameSettings => Set<GameSettingsRow>();
    public DbSet<DevClockRow> DevClock => Set<DevClockRow>();
    public DbSet<ExternalLoginRow> ExternalLogins => Set<ExternalLoginRow>();
    public DbSet<AppearanceRow> Appearances => Set<AppearanceRow>();
    public DbSet<FriendRequestRow> FriendRequests => Set<FriendRequestRow>();
    public DbSet<PushSubscriptionRow> PushSubscriptions => Set<PushSubscriptionRow>();
    public DbSet<NotificationPreferencesRow> NotificationPreferences => Set<NotificationPreferencesRow>();
    public DbSet<VapidKeysRow> VapidKeys => Set<VapidKeysRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigurePlayers(modelBuilder.Entity<PlayerRow>());
        ConfigureSeasons(modelBuilder.Entity<SeasonRow>());
        ConfigureGameDays(modelBuilder.Entity<GameDayRow>());
        ConfigurePlayerDays(modelBuilder.Entity<PlayerDayRow>());
        ConfigureHands(modelBuilder.Entity<HandRow>());
        ConfigureSocial(modelBuilder);
        ConfigureProgress(modelBuilder);
        ConfigureSkins(modelBuilder);
        ConfigureOperations(modelBuilder);
        ConfigureAccounts(modelBuilder);
        ConfigurePush(modelBuilder);
    }

    private static void ConfigurePlayers(EntityTypeBuilder<PlayerRow> e)
    {
        e.ToTable("players", t =>
        {
            t.HasCheckConstraint("ck_players_kind", $"kind in ({In(Human, Watcher)})");
            t.HasCheckConstraint("ck_players_status", $"status in ({In(Waitlisted, Active, Fallen)})");
            t.HasCheckConstraint("ck_players_map_name_visibility", "map_name_visibility in ('friends', 'everyone', 'nobody')");
        });
        e.HasKey(p => p.Id);
        e.Property(p => p.Kind).HasMaxLength(16);
        e.Property(p => p.Status).HasMaxLength(16);
        e.Property(p => p.Pseudo).HasMaxLength(64);
        e.Property(p => p.PseudoKey).HasMaxLength(64);
        e.Property(p => p.Locale).HasMaxLength(8);
        e.Property(p => p.InviteCode).HasMaxLength(32);
        e.Property(p => p.MapNameVisibility).HasMaxLength(16).HasDefaultValue("friends");
        e.Property(p => p.GuardianScore).HasDefaultValue(0);
        e.Property(p => p.FallsCount).HasDefaultValue(0);
        e.HasIndex(p => p.PseudoKey).IsUnique();
        e.HasIndex(p => p.InviteCode).IsUnique();
        e.HasIndex(p => new { p.Kind, p.Status });
        e.HasIndex(p => p.InvitedById);
        e.HasIndex(p => new { p.TileX, p.TileY });
        e.HasOne<PlayerRow>().WithMany().HasForeignKey(p => p.InvitedById).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureSeasons(EntityTypeBuilder<SeasonRow> e)
    {
        e.ToTable("seasons", t => t.HasCheckConstraint("ck_seasons_dates", "last_day is null or last_day >= opening_day"));
        e.HasKey(s => s.Id);
        e.Property(s => s.Name).HasMaxLength(100);
        e.Property(s => s.Deaths).HasDefaultValue(0L);
        e.HasIndex(s => s.Number).IsUnique();
    }

    private static void ConfigureGameDays(EntityTypeBuilder<GameDayRow> e)
    {
        e.ToTable("game_days");
        e.HasKey(d => d.Day);
        e.HasOne<SeasonRow>().WithMany().HasForeignKey(d => d.SeasonId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(d => d.ResolvedAt).HasFilter("resolved_at is null");
    }

    private static void ConfigurePlayerDays(EntityTypeBuilder<PlayerDayRow> e)
    {
        e.ToTable("player_days", t =>
        {
            t.HasCheckConstraint("ck_player_days_outcome", $"outcome is null or outcome in ({In(Survived, Fell, Immune)})");
            t.HasCheckConstraint("ck_player_days_counts", "required >= 0 and received >= 0");
        });
        e.HasKey(d => new { d.Day, d.PlayerId });
        e.Property(d => d.Outcome).HasMaxLength(16);
        e.Property(d => d.Received).HasDefaultValue(0);
        e.Property(d => d.ScoreGained).HasDefaultValue(0);
        e.HasOne<PlayerRow>().WithMany().HasForeignKey(d => d.PlayerId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<GameDayRow>().WithMany().HasForeignKey(d => d.Day).OnDelete(DeleteBehavior.Restrict);

        // The Edge of the void: who is still below their threshold today.
        e.HasIndex(d => d.Day).HasFilter("received < required").HasDatabaseName("ix_player_days_edge");
        e.HasIndex(d => d.PlayerId);
    }

    private static void ConfigureHands(EntityTypeBuilder<HandRow> e)
    {
        e.ToTable("hands", t =>
        {
            t.HasCheckConstraint("ck_hands_not_self", "giver_id <> receiver_id");
            t.HasCheckConstraint("ck_hands_slot", "slot between 1 and 10");
            t.HasCheckConstraint("ck_hands_giver_kind", $"giver_kind in ({In(Human, Watcher)})");
        });
        e.HasKey(h => h.Id);
        e.Property(h => h.Id).UseIdentityAlwaysColumn();
        e.Property(h => h.GiverKind).HasMaxLength(16);

        // The two rules that must survive any race: three slots per giver and day,
        // and never twice the same receiver on the same day (ADR 0004).
        e.HasIndex(h => new { h.GiverId, h.Day, h.Slot }).IsUnique().HasDatabaseName("ux_hands_giver_day_slot");
        e.HasIndex(h => new { h.GiverId, h.ReceiverId, h.Day }).IsUnique().HasDatabaseName("ux_hands_giver_receiver_day");
        e.HasIndex(h => new { h.Day, h.ReceiverId, h.GivenAt });

        e.HasOne<PlayerRow>().WithMany().HasForeignKey(h => h.GiverId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<PlayerRow>().WithMany().HasForeignKey(h => h.ReceiverId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<GameDayRow>().WithMany().HasForeignKey(h => h.Day).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureSocial(ModelBuilder b)
    {
        b.Entity<FriendshipRow>(e =>
        {
            e.ToTable("friendships", t =>
            {
                t.HasCheckConstraint("ck_friendships_order", "player_low < player_high");
                t.HasCheckConstraint("ck_friendships_source", "source in ('request', 'invite')");
            });
            e.HasKey(f => new { f.PlayerLow, f.PlayerHigh });
            e.Property(f => f.Source).HasMaxLength(16);
            e.HasIndex(f => f.PlayerHigh);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(f => f.PlayerLow).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(f => f.PlayerHigh).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BlockRow>(e =>
        {
            e.ToTable("blocks", t => t.HasCheckConstraint("ck_blocks_not_self", "blocker_id <> blocked_id"));
            e.HasKey(x => new { x.BlockerId, x.BlockedId });
            e.HasIndex(x => x.BlockedId);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(x => x.BlockerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(x => x.BlockedId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<HoldPairRow>(e =>
        {
            e.ToTable("hold_pairs", t => t.HasCheckConstraint("ck_hold_pairs_order", "player_low < player_high"));
            e.HasKey(x => new { x.PlayerLow, x.PlayerHigh });
            e.HasIndex(x => x.PlayerHigh);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(x => x.PlayerLow).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(x => x.PlayerHigh).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureProgress(ModelBuilder b)
    {
        b.Entity<StormRow>(e =>
        {
            e.ToTable("storms");
            e.HasKey(s => s.Day);
        });

        b.Entity<LifeDebtRow>(e =>
        {
            e.ToTable("life_debts");
            e.HasKey(d => new { d.Day, d.SavedId, d.SaverId });
            e.HasIndex(d => d.SavedId);
            e.HasIndex(d => d.SaverId);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(d => d.SavedId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(d => d.SaverId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PlayerFallRow>(e =>
        {
            e.ToTable("player_falls");
            e.HasKey(f => new { f.PlayerId, f.Day });
            e.HasIndex(f => f.SeasonId);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(f => f.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SeasonRow>().WithMany().HasForeignKey(f => f.SeasonId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SeasonScoreRow>(e =>
        {
            e.ToTable("season_scores");
            e.HasKey(s => new { s.SeasonId, s.PlayerId });
            e.HasIndex(s => new { s.SeasonId, s.Points });
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(s => s.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SeasonRow>().WithMany().HasForeignKey(s => s.SeasonId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PlayerStatsRow>(e =>
        {
            e.ToTable("player_stats");
            e.HasKey(s => s.PlayerId);
            e.HasIndex(s => s.UpdatedDay);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(s => s.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureSkins(ModelBuilder b)
    {
        b.Entity<SkinItemRow>(e =>
        {
            e.ToTable("skin_items", t =>
                t.HasCheckConstraint("ck_skin_items_condition", "(unlock_metric is null) = (unlock_threshold is null)"));
            e.HasKey(i => i.Id);
            e.Property(i => i.Id).HasMaxLength(64);
            e.Property(i => i.Slot).HasMaxLength(16);
            e.Property(i => i.Rarity).HasMaxLength(16);
            e.Property(i => i.RenderKey).HasMaxLength(64);
            e.Property(i => i.UnlockMetric).HasMaxLength(32);
        });

        b.Entity<SkinToneRow>(e =>
        {
            e.ToTable("skin_tones");
            e.HasKey(t => t.Color);
            e.Property(t => t.Color).HasMaxLength(7);
            e.HasData(SeedData.SkinTones.Select((color, i) => new SkinToneRow { Color = color, Sort = i }));
        });

        b.Entity<PlayerSkinUnlockRow>(e =>
        {
            e.ToTable("player_skin_unlocks");
            e.HasKey(u => new { u.PlayerId, u.ItemId });
            e.Property(u => u.Reason).HasMaxLength(32);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(u => u.PlayerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SkinItemRow>().WithMany().HasForeignKey(u => u.ItemId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureOperations(ModelBuilder b)
    {
        b.Entity<NotificationOutboxRow>(e =>
        {
            e.ToTable("notification_outbox");
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).UseIdentityAlwaysColumn();
            e.Property(n => n.DedupeKey).HasMaxLength(200);
            e.Property(n => n.Kind).HasMaxLength(64);
            e.Property(n => n.Payload).HasColumnType("jsonb");
            e.Property(n => n.Attempts).HasDefaultValue(0);
            e.HasIndex(n => n.DedupeKey).IsUnique();
            e.HasIndex(n => n.Id).HasFilter("sent_at is null").HasDatabaseName("ix_notification_outbox_pending");
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(n => n.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GameSettingsRow>(e =>
        {
            e.ToTable("game_settings");
            e.HasKey(s => s.Version);
            e.Property(s => s.Version).ValueGeneratedNever();
            e.Property(s => s.Document).HasColumnType("jsonb");
            e.Property(s => s.Comment).HasMaxLength(500);
            e.HasData(SeedData.FirstSettings);
        });

        b.Entity<DevClockRow>(e =>
        {
            e.ToTable("dev_clock", t => t.HasCheckConstraint("ck_dev_clock_single_row", "id = 1"));
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).ValueGeneratedNever();
        });
    }

    private static void ConfigureAccounts(ModelBuilder b)
    {
        b.Entity<ExternalLoginRow>(e =>
        {
            e.ToTable("external_logins");
            e.HasKey(l => new { l.Provider, l.ProviderKey });
            e.Property(l => l.Provider).HasMaxLength(32);
            e.Property(l => l.ProviderKey).HasMaxLength(256);
            e.HasIndex(l => l.PlayerId);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(l => l.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppearanceRow>(e =>
        {
            e.ToTable("appearances");
            e.HasKey(a => a.PlayerId);
            e.Property(a => a.Document).HasColumnType("jsonb");
            e.HasOne<PlayerRow>().WithOne().HasForeignKey<AppearanceRow>(a => a.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FriendRequestRow>(e =>
        {
            e.ToTable("friend_requests", t =>
            {
                t.HasCheckConstraint("ck_friend_requests_status", "status in ('pending', 'accepted', 'declined', 'cancelled')");
                t.HasCheckConstraint("ck_friend_requests_not_self", "from_id <> to_id");
            });
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).UseIdentityAlwaysColumn();
            e.Property(r => r.Status).HasMaxLength(16);
            e.HasIndex(r => new { r.FromId, r.ToId }).IsUnique().HasFilter("status = 'pending'").HasDatabaseName("ux_friend_requests_pending");
            e.HasIndex(r => r.ToId).HasFilter("status = 'pending'");
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(r => r.FromId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(r => r.ToId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurePush(ModelBuilder b)
    {
        b.Entity<PushSubscriptionRow>(e =>
        {
            e.ToTable("push_subscriptions");
            e.HasKey(p => p.Endpoint);
            e.Property(p => p.Endpoint).HasMaxLength(1000);
            e.Property(p => p.P256dh).HasMaxLength(200);
            e.Property(p => p.Auth).HasMaxLength(100);
            e.Property(p => p.UserAgent).HasMaxLength(300);
            e.HasIndex(p => p.PlayerId);
            e.HasOne<PlayerRow>().WithMany().HasForeignKey(p => p.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<NotificationPreferencesRow>(e =>
        {
            e.ToTable("notification_preferences");
            e.HasKey(p => p.PlayerId);
            e.HasOne<PlayerRow>().WithOne().HasForeignKey<NotificationPreferencesRow>(p => p.PlayerId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<VapidKeysRow>(e =>
        {
            e.ToTable("vapid_keys", t => t.HasCheckConstraint("ck_vapid_keys_single_row", "id = 1"));
            e.HasKey(k => k.Id);
            e.Property(k => k.Id).ValueGeneratedNever();
        });
    }
}
