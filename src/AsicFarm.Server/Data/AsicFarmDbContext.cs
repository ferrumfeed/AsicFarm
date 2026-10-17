using Microsoft.EntityFrameworkCore;

namespace AsicFarm.Server.Data;

public sealed class AsicFarmDbContext(DbContextOptions<AsicFarmDbContext> options) : DbContext(options)
{
    public DbSet<MinerEntity> Miners => Set<MinerEntity>();
    public DbSet<WatchdogEntity> Watchdogs => Set<WatchdogEntity>();
    public DbSet<ScheduleEntity> Schedules => Set<ScheduleEntity>();
    public DbSet<ScheduleRunEntity> ScheduleRuns => Set<ScheduleRunEntity>();
    public DbSet<AlertRuleEntity> AlertRules => Set<AlertRuleEntity>();
    public DbSet<AlertEventEntity> AlertEvents => Set<AlertEventEntity>();
    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();
    public DbSet<AutoActionEntity> AutoActions => Set<AutoActionEntity>();
    public DbSet<AutoActionEventEntity> AutoActionEvents => Set<AutoActionEventEntity>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<MinerEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.LastSeen);
            e.HasIndex(x => x.WatchdogId);
        });

        b.Entity<WatchdogEntity>(e => e.HasKey(x => x.Id));
        b.Entity<ScheduleEntity>(e => e.HasKey(x => x.Id));

        b.Entity<ScheduleRunEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ScheduleId, x.RunAt });
        });

        b.Entity<AlertRuleEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Enabled);
        });

        b.Entity<AlertEventEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FiredAt);
        });

        b.Entity<AppSettingEntity>(e => e.HasKey(x => x.Key));

        b.Entity<AutoActionEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Enabled);
        });

        b.Entity<AutoActionEventEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.FiredAt);
        });
    }
}