using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Configurations;

/// <summary>Junction: User (1) -> (N) UserAccessibilityNeeds (N) <- (1) DisabilityType. Composite key (UserId, DisabilityTypeId).</summary>
public sealed class UserAccessibilityNeedConfiguration : IEntityTypeConfiguration<UserAccessibilityNeed>
{
    public void Configure(EntityTypeBuilder<UserAccessibilityNeed> b)
    {
        b.ToTable("user_accessibility_needs");
        b.HasKey(x => new { x.UserId, x.DisabilityTypeId });
        b.HasOne<DisabilityType>().WithMany().HasForeignKey(x => x.DisabilityTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// The Rules currently assigned to a user. PK (UserId, RuleId) prevents assigning the same Rule twice.
/// There is deliberately NO unique constraint on UserId: whether a user may hold one Rule or several is an open business decision.
/// </summary>
public sealed class UserRuleAssignmentConfiguration : IEntityTypeConfiguration<UserRuleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRuleAssignment> b)
    {
        b.ToTable("user_rule_assignments");
        b.HasKey(x => new { x.UserId, x.RuleId });
        b.HasOne<Rule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.RuleId).HasDatabaseName("ix_user_rule_assignments_rule_id");
    }
}

/// <summary>Append-only: the persistence interceptor rejects any update or delete.</summary>
public sealed class UserRuleHistoryConfiguration : IEntityTypeConfiguration<UserRuleHistory>
{
    public void Configure(EntityTypeBuilder<UserRuleHistory> b)
    {
        b.ToTable("user_rule_history", t => t.HasCheckConstraint("ck_user_rule_history_reason", SqlChecks.In<RuleChangeReason>("reason")));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd();
        b.Property(x => x.Reason).HasConversion<string>().HasMaxLength(30).IsUnicode(false).IsRequired();
        b.HasOne<Rule>().WithMany().HasForeignKey(x => x.FromRuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Rule>().WithMany().HasForeignKey(x => x.ToRuleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.Restrict);
        // RelatedFamilyLinkId is a logical reference to the Family module: no foreign key.
        b.HasIndex(x => new { x.UserId, x.ChangedAt }).IsDescending(false, true).HasDatabaseName("ix_user_rule_history_user_changed");
    }
}
