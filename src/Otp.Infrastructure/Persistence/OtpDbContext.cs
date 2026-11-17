using Microsoft.EntityFrameworkCore;
using Otp.Domain.Entities;

namespace Otp.Infrastructure.Persistence;

public class OtpDbContext(DbContextOptions<OtpDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<VerificationAttempt> VerificationAttempts => Set<VerificationAttempt>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).IsRequired();
        });
        b.Entity<VerificationAttempt>(e =>
        {
            e.ToTable("verification_attempts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });
    }
}
