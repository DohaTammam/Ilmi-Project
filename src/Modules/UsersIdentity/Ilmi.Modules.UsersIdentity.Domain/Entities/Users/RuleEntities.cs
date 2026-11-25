using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Domain.Entities;

/// <summary>Junction: a user's declared special need. Composite key (UserId, DisabilityTypeId).</summary>
public sealed class UserAccessibilityNeed
{
    private UserAccessibilityNeed() { }

    public UserAccessibilityNeed(Guid userId, short disabilityTypeId, DateTime createdAt)
    {
        UserId = userId; DisabilityTypeId = disabilityTypeId; CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }
    public short DisabilityTypeId { get; private set; }
    public DateTime CreatedAt { get; private set; }
}

/// <summary>
/// A Rule currently assigned to a user. Primary key (UserId, RuleId) prevents assigning the same Rule twice.
/// Whether a user may hold several Rules at once is an OPEN business decision: the model neither forbids nor requires it.
/// </summary>
public sealed class UserRuleAssignment
{
    private UserRuleAssignment() { }

    internal UserRuleAssignment(Guid userId, short ruleId, DateTime assignedAt, Guid? assignedBy)
    {
        UserId = userId; RuleId = ruleId; AssignedAt = assignedAt; AssignedBy = assignedBy;
    }

    public Guid UserId { get; private set; }
    public short RuleId { get; private set; }
    public DateTime AssignedAt { get; private set; }
    /// <summary>Null when the system made the assignment.</summary>
    public Guid? AssignedBy { get; private set; }
}

/// <summary>Append-only record of every Rule change.</summary>
public sealed class UserRuleHistory : IAppendOnly
{
    private UserRuleHistory() { }

    public long Id { get; private set; }
    public Guid UserId { get; private set; }
    /// <summary>Null for the first assignment of a Rule.</summary>
    public short? FromRuleId { get; private set; }
    public short ToRuleId { get; private set; }
    public RuleChangeReason Reason { get; private set; }
    /// <summary>Logical reference to family.family_links (no foreign key across modules).</summary>
    public Guid? RelatedFamilyLinkId { get; private set; }
    public DateTime ChangedAt { get; private set; }
    public Guid? ChangedBy { get; private set; }

    internal static UserRuleHistory Record(Guid userId, short? from, short to, RuleChangeReason reason, DateTime at, Guid? by, Guid? linkId) =>
        new() { UserId = userId, FromRuleId = from, ToRuleId = to, Reason = reason, ChangedAt = at, ChangedBy = by, RelatedFamilyLinkId = linkId };
}
