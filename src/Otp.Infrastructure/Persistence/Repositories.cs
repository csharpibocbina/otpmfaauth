using Microsoft.EntityFrameworkCore;
using Otp.Application.Abstractions;
using Otp.Domain.Entities;

namespace Otp.Infrastructure.Persistence;

public class UserRepository(OtpDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task AddAsync(User user, CancellationToken ct) => await db.Users.AddAsync(user, ct);
    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public class VerificationRepository(OtpDbContext db) : IVerificationRepository
{
    public async Task AddAsync(VerificationAttempt attempt, CancellationToken ct) =>
        await db.VerificationAttempts.AddAsync(attempt, ct);

    public Task<VerificationAttempt?> GetAsync(Guid id, CancellationToken ct) =>
        db.VerificationAttempts.FirstOrDefaultAsync(v => v.Id == id, ct);

    public Task<int> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken ct) =>
        db.VerificationAttempts.CountAsync(v => v.UserId == userId && v.CreatedAt >= since, ct);

    public async Task<DateTimeOffset?> GetLastCreatedAsync(Guid userId, CancellationToken ct) =>
        await db.VerificationAttempts.Where(v => v.UserId == userId)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => (DateTimeOffset?)v.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
