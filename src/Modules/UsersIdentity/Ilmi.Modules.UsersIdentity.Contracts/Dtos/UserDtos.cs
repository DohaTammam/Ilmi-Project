namespace Ilmi.Modules.UsersIdentity.Contracts.Dtos;

/// <summary>Minimal, stable view of a user for other modules. Values are the enum names used by the module.</summary>
/// <param name="UserKind">External | Internal</param>
/// <param name="UserType">Child | Teen | Adult (null for Internal users)</param>
/// <param name="AccountStatus">Active | PendingActivation | Deactivated</param>
/// <param name="RuleCodes">CHILD | TEEN | ADULT | PARENT | REGISTRATION_OFFICER</param>
public sealed record UserSummaryDto(
    Guid Id, string UserKind, string? UserType, string AccountStatus, bool IsActive,
    string FirstName, string LastName, string Email, IReadOnlyList<string> RuleCodes);

public enum RuleTransitionOutcome
{
    /// <summary>The Rule was changed.</summary>
    Changed,
    /// <summary>Nothing to do (already in the requested state).</summary>
    NoChange,
    UserNotFound,
    /// <summary>The user is not in a state that allows the transition (for example not an Adult).</summary>
    NotAllowed
}

/// <param name="IsLocked">True while the lockout is in force.</param>
/// <param name="LockedUntil">UTC end of the lockout, when locked.</param>
/// <param name="ThresholdConfigured">False when the failed-attempt threshold has not been decided (open business question).</param>
public sealed record LockoutStatusDto(bool IsLocked, DateTime? LockedUntil, int FailedAttempts, bool ThresholdConfigured);
