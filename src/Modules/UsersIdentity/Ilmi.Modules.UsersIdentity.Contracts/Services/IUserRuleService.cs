using Ilmi.Modules.UsersIdentity.Contracts.Dtos;

namespace Ilmi.Modules.UsersIdentity.Contracts.Services;

/// <summary>
/// Integration boundary for the Family module. Family decides WHEN a user becomes a Parent or returns to Adult
/// (after a dependent is linked, or the last active dependent is unlinked); this module owns HOW the Rule changes.
/// UserType never changes here: a Parent stays an Adult.
/// </summary>
public interface IUserRuleService
{
    /// <summary>Adult -> Parent. Idempotent when the user is already a Parent.</summary>
    Task<RuleTransitionOutcome> MarkAsParentAsync(Guid userId, Guid? relatedFamilyLinkId, Guid? changedBy, CancellationToken ct = default);

    /// <summary>Parent -> Adult. The caller guarantees that no active dependent remains. Idempotent when already Adult.</summary>
    Task<RuleTransitionOutcome> RevertParentToAdultAsync(Guid userId, Guid? relatedFamilyLinkId, Guid? changedBy, CancellationToken ct = default);
}
