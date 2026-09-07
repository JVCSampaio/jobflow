namespace JobFlow.Api.Data;

using Microsoft.EntityFrameworkCore;
using JobFlow.Api.Data.Entities;

public class JobFlowDbContext : DbContext
{
    public JobFlowDbContext(DbContextOptions<JobFlowDbContext> options)
        : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.Property(u => u.Email).HasMaxLength(255).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(120).IsRequired();
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
        });

        builder.Entity<Job>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Name).HasMaxLength(200).IsRequired();
            // JobStatus is stored as its underlying int (default EF enum mapping).
            e.Property(j => j.Payload).HasMaxLength(65535);
            e.Property(j => j.Result).HasMaxLength(65535);
            e.Property(j => j.Error).HasMaxLength(65535);
            e.Property(j => j.MaxRetries).HasDefaultValue(3);

            // Partial index: only rows that are due to run (Queued or Failed with retries left).
            e.HasIndex(j => new { j.Status, j.NextRunAt })
                .HasDatabaseName("ix_jobs_status_next_run")
                .HasFilter("status IN (0, 3)");
        });
    }
}
