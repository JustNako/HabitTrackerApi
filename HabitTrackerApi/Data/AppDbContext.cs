using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApi.Entities;

namespace HabitTrackerApi.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public DbSet<Habit> Habits {get;set;}
    public DbSet<HabitLog> HabitLogs {get;set;}

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Habit>()
            .HasMany(h => h.Logs)
            .WithOne(l => l.Habit)
            .HasForeignKey(l => l.HabitId);

        modelBuilder.Entity<Habit>().HasQueryFilter(h => !h.IsDeleted);
        modelBuilder.Entity<HabitLog>().HasQueryFilter(l => !l.IsDeleted);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is Habit || e.Entity is HabitLog)
            .Where(e => e.State == EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Property("UpdatedAt").IsModified)
            {
                continue;
            }

            if (entry.Entity is Habit habit)
            {
                habit.UpdatedAt = DateTime.UtcNow;
            }
            else if (entry.Entity is HabitLog log)
            {
                log.UpdatedAt = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}