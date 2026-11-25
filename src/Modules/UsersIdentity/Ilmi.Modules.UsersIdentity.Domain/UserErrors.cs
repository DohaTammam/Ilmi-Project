using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Domain;

/// <summary>Domain-level failures. Codes are stable and surfaced to API clients.</summary>
public static class UserErrors
{
    public static readonly Error FirstNameRequired = Error.Validation("user.first_name_required", "First name is required (max 250 characters).");
    public static readonly Error LastNameRequired = Error.Validation("user.last_name_required", "Last name is required (max 250 characters).");
    public static readonly Error EmailInvalid = Error.Validation("user.email_invalid", "A valid email address is required (max 250 characters).");
    public static readonly Error MobileInvalid = Error.Validation("user.mobile_invalid", "Mobile country code and national number are required and must be numeric.");
    public static readonly Error DateOfBirthInvalid = Error.Validation("user.date_of_birth_invalid", "The date of birth cannot be in the future.");
    public static readonly Error BelowMinimumAge = Error.Validation("user.below_minimum_age", "An account requires a minimum age of 6.");
    public static readonly Error QrCodeInvalid = Error.Validation("user.qr_code_invalid", "The QR code must be 10 characters (0-9, A-Z).");
    public static readonly Error ExternalOnly = Error.Validation("user.external_only", "This information applies to External users only.");
    public static readonly Error NotPendingActivation = Error.Conflict("user.not_pending_activation", "The account is not pending activation.");
    public static readonly Error ActivationExpired = Error.Conflict("user.activation_expired", "The activation period has expired.");
    public static readonly Error RuleAlreadyAssigned = Error.Conflict("user.rule_already_assigned", "The Rule is already assigned to this user.");
    public static readonly Error RuleNotAssigned = Error.NotFound("user.rule_not_assigned", "The Rule is not assigned to this user.");
    public static readonly Error DuplicateDisabilityType = Error.Validation("user.disability_type_duplicate", "A disability type was supplied more than once.");
}
