using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Configurations;

public sealed class AvatarConfiguration : IEntityTypeConfiguration<Avatar>
{
    public void Configure(EntityTypeBuilder<Avatar> b)
    {
        b.ToTable("avatars");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(40).IsUnicode(false).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(100).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(100);
        b.Property(x => x.ImageKey).HasMaxLength(250).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_avatars_code");
        // No seed: the five avatar names and image keys are not defined (Arabic names are an open question).
    }
}

/// <summary>Lookup from SRS DDL02. Seeded with HasData, so the migration is idempotent by primary key.</summary>
public sealed class DisabilityTypeConfiguration : IEntityTypeConfiguration<DisabilityType>
{
    public void Configure(EntityTypeBuilder<DisabilityType> b)
    {
        b.ToTable("disability_types");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(40).IsUnicode(false).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(100).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_disability_types_code");

        b.HasData(
            new DisabilityType(1, "UNABLE_TO_WALK", "Unable to Walk", "عدم القدرة على المشي", 1),
            new DisabilityType(2, "UNABLE_TO_WALK_LONG_DISTANCES", "Unable to Walk Long Distances", "عدم القدرة على المشي لمسافات طويلة", 2),
            new DisabilityType(3, "VISUAL_IMPAIRMENT_BLINDNESS", "Visual Impairment / Blindness", "ضعف البصر / العمى", 3),
            new DisabilityType(4, "HEARING_LOSS_DEAFNESS", "Hearing Loss / Deafness", "فقد حاسة السمع", 4));
    }
}

/// <summary>The five business Rules. Arabic names are not provided by the requirements, so NameAr stays null (open question).</summary>
public sealed class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> b)
    {
        b.ToTable("rules");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(30).IsUnicode(false).IsRequired();
        b.Property(x => x.NameEn).HasMaxLength(100).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(100);
        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_rules_code");

        b.HasData(
            new Rule(RuleIds.Child, RuleCodes.Child, "Child", null, isAgeBased: true, isStaff: false, sortOrder: 1),
            new Rule(RuleIds.Teen, RuleCodes.Teen, "Teen", null, isAgeBased: true, isStaff: false, sortOrder: 2),
            new Rule(RuleIds.Adult, RuleCodes.Adult, "Adult", null, isAgeBased: true, isStaff: false, sortOrder: 3),
            new Rule(RuleIds.Parent, RuleCodes.Parent, "Parent", null, isAgeBased: false, isStaff: false, sortOrder: 4),
            new Rule(RuleIds.RegistrationOfficer, RuleCodes.RegistrationOfficer, "Registration Officer", null, isAgeBased: false, isStaff: true, sortOrder: 5));
    }
}
