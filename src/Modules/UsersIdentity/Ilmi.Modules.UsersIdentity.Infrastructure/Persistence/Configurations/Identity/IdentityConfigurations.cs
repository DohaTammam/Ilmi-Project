using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Configurations;

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> b)
    {
        b.ToTable("otp_challenges", t =>
        {
            t.HasCheckConstraint("ck_otp_challenges_purpose", SqlChecks.In<OtpPurpose>("purpose"));
            t.HasCheckConstraint("ck_otp_challenges_channel", SqlChecks.In<OtpChannel>("channel"));   // Email is the only channel
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(25).IsUnicode(false).IsRequired();
        b.Property(x => x.Channel).HasConversion<string>().HasMaxLength(5).IsUnicode(false).IsRequired();
        b.Property(x => x.TargetEmail).HasMaxLength(250).IsRequired();
        b.Property(x => x.CodeHash).HasColumnType("binary(32)").IsRequired();                       // keyed hash, never the code
        b.Property(x => x.PendingPayload).HasColumnType("varbinary(max)");                           // ciphertext bytes
        b.HasIndex(x => new { x.TargetEmail, x.Purpose, x.CreatedAt }).IsDescending(false, false, true).HasDatabaseName("ix_otp_challenges_target_purpose_created");
        b.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_otp_challenges_expires_at");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.InitiatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Primary key (Purpose, ScopeKey). No foreign key: the scope is a hash of an email, or a Parent's id.</summary>
public sealed class VerificationLockoutConfiguration : IEntityTypeConfiguration<VerificationLockout>
{
    public void Configure(EntityTypeBuilder<VerificationLockout> b)
    {
        b.ToTable("verification_lockouts", t => t.HasCheckConstraint("ck_verification_lockouts_purpose", SqlChecks.In<OtpPurpose>("purpose")));
        b.HasKey(x => new { x.Purpose, x.ScopeKey });
        b.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(25).IsUnicode(false);
        b.Property(x => x.ScopeKey).HasMaxLength(100).IsUnicode(false);
    }
}

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b)
    {
        b.ToTable("user_sessions", t =>
        {
            t.HasCheckConstraint("ck_user_sessions_client_type", SqlChecks.In<ClientType>("client_type"));
            t.HasCheckConstraint("ck_user_sessions_revoked_reason", SqlChecks.In<SessionRevokedReason>("revoked_reason", nullable: true));
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ClientType).HasConversion<string>().HasMaxLength(6).IsUnicode(false).IsRequired();
        b.Property(x => x.DeviceId).HasMaxLength(100).IsUnicode(false);
        b.Property(x => x.RefreshTokenHash).HasColumnType("binary(32)").IsRequired();                // hash of the token, never the token
        b.Property(x => x.RevokedReason).HasConversion<string>().HasMaxLength(30).IsUnicode(false);
        b.HasIndex(x => x.RefreshTokenHash).IsUnique().HasDatabaseName("ux_user_sessions_token_hash");
        b.HasIndex(x => x.UserId).HasFilter("[revoked_at] IS NULL").HasDatabaseName("ix_user_sessions_user_active");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Device-bound public key. No PIN, no passcode hash, no biometric data is stored.</summary>
public sealed class QuickLoginRegistrationConfiguration : IEntityTypeConfiguration<QuickLoginRegistration>
{
    public void Configure(EntityTypeBuilder<QuickLoginRegistration> b)
    {
        b.ToTable("quick_login_registrations", t =>
        {
            t.HasCheckConstraint("ck_quick_login_platform", SqlChecks.In<DevicePlatform>("platform"));
            t.HasCheckConstraint("ck_quick_login_disabled_reason", SqlChecks.In<QuickLoginDisabledReason>("disabled_reason", nullable: true));
            t.HasCheckConstraint("ck_quick_login_state",
                "([is_enabled] = 1 AND [disabled_at] IS NULL) OR ([is_enabled] = 0 AND [disabled_at] IS NOT NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.DeviceId).HasMaxLength(100).IsUnicode(false).IsRequired();
        b.Property(x => x.Platform).HasConversion<string>().HasMaxLength(7).IsUnicode(false).IsRequired();
        b.Property(x => x.PublicKey).HasColumnType("varbinary(512)").IsRequired();
        b.Property(x => x.KeyAlgorithm).HasMaxLength(10).IsUnicode(false).IsRequired();
        b.Property(x => x.KeyFingerprint).HasColumnType("binary(32)").IsRequired();
        b.Property(x => x.ChallengeNonce).HasColumnType("binary(32)");
        b.Property(x => x.DisabledReason).HasConversion<string>().HasMaxLength(25).IsUnicode(false);
        b.HasIndex(x => x.KeyFingerprint).IsUnique().HasDatabaseName("ux_quick_login_key_fingerprint");
        // Only one enabled registration per device: enabling a different user on a device disables the previous one first.
        b.HasIndex(x => x.DeviceId).IsUnique().HasFilter("[is_enabled] = 1").HasDatabaseName("ux_quick_login_device_enabled");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Append-only: the persistence interceptor rejects any update or delete.</summary>
public sealed class LoginAuditConfiguration : IEntityTypeConfiguration<LoginAudit>
{
    public void Configure(EntityTypeBuilder<LoginAudit> b)
    {
        b.ToTable("login_audit", t =>
        {
            t.HasCheckConstraint("ck_login_audit_method", SqlChecks.In<LoginMethod>("login_method"));
            t.HasCheckConstraint("ck_login_audit_outcome", SqlChecks.In<LoginOutcome>("outcome"));
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
        b.Property(x => x.LoginMethod).HasConversion<string>().HasMaxLength(12).IsUnicode(false).IsRequired();
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(25).IsUnicode(false).IsRequired();
        b.Property(x => x.DeviceId).HasMaxLength(100).IsUnicode(false);
        b.HasIndex(x => new { x.UserId, x.OccurredAt }).IsDescending(false, true).HasDatabaseName("ix_login_audit_user_occurred");
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
