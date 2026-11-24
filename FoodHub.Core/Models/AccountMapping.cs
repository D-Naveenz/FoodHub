using Microsoft.EntityFrameworkCore;

namespace FoodHub.Core.Models;

/// <summary>Maintained account mappings supplement the original scaffold.</summary>
public partial class FoodHub_DevContext
{
    /// <summary>Registered local accounts.</summary>
    public DbSet<User> Users => Set<User>();
    /// <summary>Hashed opaque session tokens.</summary>
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureBaseline(modelBuilder);
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(u => u.UserId);
            e.Property(u => u.Email).HasMaxLength(254).IsRequired();
            e.Property(u => u.NormalizedEmail).HasMaxLength(254).IsRequired();
            e.HasIndex(u => u.NormalizedEmail).IsUnique();
            e.Property(u => u.DisplayName).HasMaxLength(80).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
            e.Property(u => u.CreatedAtUtc).HasPrecision(3);
        });
        modelBuilder.Entity<Customer>().HasOne(c => c.User).WithOne(u => u.Customer)
            .HasForeignKey<Customer>(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Customer>().HasIndex(c => c.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        modelBuilder.Entity<UserSession>(e =>
        {
            e.ToTable("UserSessions");
            e.HasKey(s => s.UserSessionId);
            e.Property(s => s.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.Property(s => s.CreatedAtUtc).HasPrecision(3);
            e.Property(s => s.ExpiresAtUtc).HasPrecision(3);
            e.Property(s => s.RevokedAtUtc).HasPrecision(3);
            e.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}



