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
    }
}