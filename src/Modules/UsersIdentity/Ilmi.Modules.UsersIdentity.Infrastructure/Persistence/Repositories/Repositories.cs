using System.Text.RegularExpressions;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence;

public sealed partial class UnitOfWork(UsersIdentityDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.GetBaseException() is SqlException { Number: 2601 or 2627 } sql)
        {
            var match = ConstraintName().Match(sql.Message);
            throw new UniqueConstraintViolationException(match.Success ? match.Groups[1].Value : sql.Message);
        }
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await work(ct);
            await tx.CommitAsync(ct);
        });

    [GeneratedRegex(@"(?:unique index|constraint) '([^']+)'")]
    private static partial Regex ConstraintName();
}

public sealed class UserRepository(UsersIdentityDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, UserIncludes includes, CancellationToken ct) =>
        Include(includes).FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, UserIncludes includes, CancellationToken ct) =>
        Include(includes).FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    private IQueryable<User> Include(UserIncludes includes)
    {
        IQueryable<User> q = db.Accounts.Include(u => u.ExternalUser).Include(u => u.InternalUser);
        if (includes.HasFlag(UserIncludes.Settings)) q = q.Include(u => u.Settings);
        if (includes.HasFlag(UserIncludes.AccessibilityNeeds)) q = q.Include(u => u.AccessibilityNeeds);
        if (includes.HasFlag(UserIncludes.Rules)) q = q.Include(u => u.RuleAssignments);
        return q;
    }

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        db.Accounts.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct);

    /// <summary>True when a Teen, Adult or Internal user already uses this number (Children may share numbers). The unique index enforces the same rule.</summary>
    public Task<bool> MobileInUseAsync(string countryCode, string nationalNumber, CancellationToken ct) =>
        db.Accounts.AnyAsync(u => u.MobileCountryCode == countryCode && u.MobileNationalNumber == nationalNumber &&
                                  (u.UserType == null || u.UserType != UserType.Child), ct);

    public Task<bool> QrCodeExistsAsync(string qrCode, CancellationToken ct) => db.ExternalUsers.AnyAsync(e => e.QrCode == qrCode, ct);
    public Task<bool> AvatarExistsAsync(short avatarId, CancellationToken ct) => db.Avatars.AnyAsync(a => a.Id == avatarId && a.IsActive, ct);
    public async Task<IReadOnlyList<DisabilityType>> GetDisabilityTypesAsync(CancellationToken ct) =>
        await db.DisabilityTypes.AsNoTracking().OrderBy(t => t.SortOrder).ToListAsync(ct);
    public async Task<IReadOnlyList<Rule>> GetRulesAsync(CancellationToken ct) =>
        await db.Rules.AsNoTracking().OrderBy(r => r.SortOrder).ToListAsync(ct);
}

public sealed class OtpRepository(UsersIdentityDbContext db) : IOtpRepository
{
    public void Add(OtpChallenge challenge) => db.OtpChallenges.Add(challenge);
    public void Add(VerificationLockout lockout) => db.VerificationLockouts.Add(lockout);

    public Task<OtpChallenge?> GetLatestAsync(OtpPurpose purpose, string targetEmail, CancellationToken ct) =>
        db.OtpChallenges.Where(c => c.Purpose == purpose && c.TargetEmail == targetEmail)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id).FirstOrDefaultAsync(ct);

    public Task<VerificationLockout?> GetLockoutAsync(OtpPurpose purpose, string scopeKey, CancellationToken ct) =>
        db.VerificationLockouts.FirstOrDefaultAsync(l => l.Purpose == purpose && l.ScopeKey == scopeKey, ct);
}

public sealed class SessionRepository(UsersIdentityDbContext db) : ISessionRepository
{
    public void Add(UserSession session) => db.UserSessions.Add(session);
    public Task<UserSession?> GetByIdAsync(Guid sessionId, CancellationToken ct) => db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
    public Task<UserSession?> GetByTokenHashAsync(byte[] tokenHash, CancellationToken ct) =>
        db.UserSessions.FirstOrDefaultAsync(s => s.RefreshTokenHash == tokenHash, ct);
    public async Task<IReadOnlyList<UserSession>> GetActiveForUserAsync(Guid userId, CancellationToken ct) =>
        await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct);
}

public sealed class QuickLoginRepository(UsersIdentityDbContext db) : IQuickLoginRepository
{
    public void Add(QuickLoginRegistration registration) => db.QuickLoginRegistrations.Add(registration);

    public Task<QuickLoginRegistration?> GetEnabledByDeviceAsync(string deviceId, CancellationToken ct) =>
        db.QuickLoginRegistrations.FirstOrDefaultAsync(r => r.DeviceId == deviceId && r.IsEnabled, ct);

    public async Task<IReadOnlyList<QuickLoginRegistration>> GetEnabledForUserAsync(Guid userId, CancellationToken ct) =>
        await db.QuickLoginRegistrations.Where(r => r.UserId == userId && r.IsEnabled).ToListAsync(ct);

    public async Task<bool> TryConsumeChallengeAsync(Guid registrationId, byte[] nonce, DateTime now, CancellationToken ct) =>
        await db.QuickLoginRegistrations
            .Where(r => r.Id == registrationId && r.ChallengeNonce == nonce && r.ChallengeExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ChallengeNonce, (byte[]?)null)
                                      .SetProperty(r => r.ChallengeExpiresAt, (DateTime?)null), ct) == 1;
}

public sealed class LoginAuditRepository(UsersIdentityDbContext db) : ILoginAuditRepository
{
    public void Add(LoginAudit audit) => db.LoginAudits.Add(audit);
}
