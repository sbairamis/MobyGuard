using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MobyGuardApi.Database.Models;

namespace MobyGuardApi.Database.Context;

public class DatabaseContext(DbContextOptions<DatabaseContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<RepositoryPermission> RepositoryPermissions { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<UserType> UserTypes { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // PostgreSQL's timestamptz only accepts UTC values.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<RepositoryPermission>()
            .Property(permission => permission.RepositoryPattern)
            .HasColumnType("citext");

        modelBuilder.Entity<RepositoryPermission>()
            .HasIndex(permission => new { permission.UserId, permission.RepositoryPattern })
            .IsUnique();
    }
}
