using Ilmi.Modules.UsersIdentity.Application.Common;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Configurations;

/// <summary>
/// The Identity side of <c>users.AspNetUsers</c>. The Domain <see cref="User"/> maps the same table and shares the row
/// (table splitting on the primary key). Email is the login identifier.
/// </summary>
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.ToTable("AspNetUsers");
        b.Property(x => x.Id).ValueGeneratedNever();     // sequential GUIDs are created by the domain (SequentialGuid)

        // Identity's EmailIndex, made unique: the login identifier.
        b.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName(ConstraintNames.UniqueNormalizedEmail).HasFilter("[NormalizedEmail] IS NOT NULL");

        // Email and normalized email are the two columns that both entities map: same name, same definition, same value.
        b.Property(x => x.Email).HasColumnName("Email").HasMaxLength(256);
        b.Property(x => x.NormalizedEmail).HasColumnName("NormalizedEmail").HasMaxLength(256);

        // Table splitting: one AspNetUsers row = one Identity user + one Domain user. The Domain user is required, so its columns are NOT NULL.
        b.HasOne(x => x.Account).WithOne().HasForeignKey<User>(u => u.Id);
        b.Navigation(x => x.Account).IsRequired();
    }
}

/// <summary>The business side of <c>users.AspNetUsers</c>: the shared account fields of both user kinds.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("AspNetUsers", t =>
        {
            t.HasCheckConstraint("ck_users_user_kind", SqlChecks.In<UserKind>("UserKind"));
            t.HasCheckConstraint("ck_users_account_status", SqlChecks.In<AccountStatus>("AccountStatus"));
            t.HasCheckConstraint("ck_users_user_type", SqlChecks.In<UserType>("UserType", nullable: true));
            t.HasCheckConstraint("ck_users_pending_activation_expiry",
                "[AccountStatus] <> 'PendingActivation' OR [PendingActivationExpiresAt] IS NOT NULL");
            // External users carry a UserType and a mobile number; Internal users have no UserType and an optional mobile number.
            t.HasCheckConstraint("ck_users_kind_profile",
                "([UserKind] = 'External' AND [UserType] IS NOT NULL AND [MobileCountryCode] IS NOT NULL AND [MobileNationalNumber] IS NOT NULL) OR " +
                "([UserKind] = 'Internal' AND [UserType] IS NULL AND (([MobileCountryCode] IS NULL AND [MobileNationalNumber] IS NULL) OR " +
                "([MobileCountryCode] IS NOT NULL AND [MobileNationalNumber] IS NOT NULL)))");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.UserKind).HasConversion<string>().HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.FirstName).HasMaxLength(250).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(250).IsRequired();
        // Email and normalized email are the two columns shared with Identity's user entity (identical definition).
        b.Property(x => x.Email).HasColumnName("Email").HasMaxLength(256).IsRequired(false);
        b.Property(x => x.NormalizedEmail).HasColumnName("NormalizedEmail").HasMaxLength(256).IsRequired(false);
        b.Property(x => x.MobileCountryCode).HasMaxLength(5).IsUnicode(false);
        b.Property(x => x.MobileNationalNumber).HasMaxLength(15).IsUnicode(false);
        b.Property(x => x.UserType).HasConversion<string>().HasMaxLength(5).IsUnicode(false);
        b.Property(x => x.AccountStatus).HasConversion<string>().HasMaxLength(17).IsUnicode(false).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Ignore(x => x.IsExternal);
        b.Ignore(x => x.CanSignIn);
        b.Ignore(x => x.DateOfBirth).Ignore(x => x.Gender).Ignore(x => x.QrCode).Ignore(x => x.AvatarId)
         .Ignore(x => x.ReceiveNewsletter).Ignore(x => x.NotifyIlmiSpecialNeeds).Ignore(x => x.TermsAcceptedAt);

        // ---- global mobile-number uniqueness, enforced by ONE unique index on the one shared users table.
        // Internal and External users cannot share a number. Children may share one: for a Child the computed key is the user's own id
        // (so Children never collide), for everyone else it is '' (so Teens, Adults and Internal users are unique together).
        b.Property<string>("MobileUniquenessKey").HasMaxLength(36).IsUnicode(false)
            .HasComputedColumnSql("CASE WHEN [UserType] = 'Child' THEN CONVERT([varchar](36), [Id]) ELSE '' END", stored: true);
        b.HasIndex("MobileCountryCode", "MobileNationalNumber", "MobileUniquenessKey").IsUnique()
            .HasDatabaseName(ConstraintNames.UniqueMobile).HasFilter("[MobileNationalNumber] IS NOT NULL");

        // The target of the composite foreign keys that tie each profile table to the right UserKind.
        b.HasAlternateKey(x => new { x.Id, x.UserKind });

        b.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedBy).OnDelete(DeleteBehavior.Restrict);

        // 1 : 0..1 with each profile. The composite key (UserId, UserKind) makes an External profile impossible for an Internal user and vice versa.
        b.HasOne(x => x.ExternalUser).WithOne().HasForeignKey<ExternalUser>(e => new { e.UserId, e.UserKind })
            .HasPrincipalKey<User>(u => new { u.Id, u.UserKind }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.InternalUser).WithOne().HasForeignKey<InternalUser>(e => new { e.UserId, e.UserKind })
            .HasPrincipalKey<User>(u => new { u.Id, u.UserKind }).OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Settings).WithOne().HasForeignKey<UserSettings>(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.AccessibilityNeeds).WithOne().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.RuleAssignments).WithOne().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.RuleHistory).WithOne().HasForeignKey(h => h.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.AccessibilityNeeds).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.RuleAssignments).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.RuleHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>users.external_users: profile of External users, with a foreign key to AspNetUsers.</summary>
public sealed class ExternalUserConfiguration : IEntityTypeConfiguration<ExternalUser>
{
    public void Configure(EntityTypeBuilder<ExternalUser> b)
    {
        b.ToTable("external_users", t =>
        {
            t.HasCheckConstraint("ck_external_users_user_kind", "[user_kind] = 'External'");
            t.HasCheckConstraint("ck_external_users_gender", SqlChecks.In<Gender>("gender", nullable: true));
            t.HasCheckConstraint("ck_external_users_qr_code", "LEN([qr_code]) = 10 AND [qr_code] NOT LIKE '%[^0-9A-Z]%' COLLATE Latin1_General_BIN");
        });
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();
        b.Property(x => x.UserKind).HasConversion<string>().HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.Gender).HasConversion<string>().HasMaxLength(6).IsUnicode(false);
        b.Property(x => x.QrCode).HasColumnType("char(10)").UseCollation("Latin1_General_BIN").IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.QrCode).IsUnique().HasDatabaseName(ConstraintNames.UniqueQrCode);
        b.HasOne<Avatar>().WithMany().HasForeignKey(x => x.AvatarId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>users.internal_users: profile of Internal (staff) users, with a foreign key to AspNetUsers. No employee attributes: none are defined.</summary>
public sealed class InternalUserConfiguration : IEntityTypeConfiguration<InternalUser>
{
    public void Configure(EntityTypeBuilder<InternalUser> b)
    {
        b.ToTable("internal_users", t => t.HasCheckConstraint("ck_internal_users_user_kind", "[user_kind] = 'Internal'"));
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();
        b.Property(x => x.UserKind).HasConversion<string>().HasMaxLength(8).IsUnicode(false).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}

public sealed class UserSettingsConfiguration : IEntityTypeConfiguration<UserSettings>
{
    public void Configure(EntityTypeBuilder<UserSettings> b)
    {
        b.ToTable("user_settings", t => t.HasCheckConstraint("ck_user_settings_language", "[language] IN ('ar','en')"));
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();
        // Only language and the email-notification preference are stored. Accessibility modes belong to the Frontend.
        b.Property(x => x.Language).HasColumnType("char(2)")
            .HasConversion(v => v == Language.Ar ? "ar" : "en", v => v == "ar" ? Language.Ar : Language.En).IsRequired();
        b.Property(x => x.UpdatedOn).HasColumnName("updated_at");
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}
