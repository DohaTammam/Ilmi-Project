using FluentValidation;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Common;

public static class IdentityErrors
{
    public static readonly Error InvalidCredentials = Error.Unauthorized("auth.invalid_credentials", "The email or password is incorrect.");
    public static readonly Error AccountLocked = Error.Locked("auth.account_locked", "The account is temporarily locked. Try again later.");
    public static readonly Error AccountInactive = Error.Forbidden("auth.account_inactive", "The account cannot sign in.");
    public static readonly Error Unauthenticated = Error.Unauthorized("auth.unauthenticated", "Authentication is required.");
    public static readonly Error OtpInvalid = Error.Validation("otp.invalid_or_expired", "The verification code is invalid or has expired.");
    public static readonly Error OtpLocked = Error.Locked("otp.locked", "Too many failed attempts. Verification is temporarily locked.");
    public static readonly Error OtpResendLimit = Error.Validation("otp.resend_limit_reached", "The maximum number of resends has been reached.");
    public static readonly Error OtpNothingToResend = Error.NotFound("otp.nothing_to_resend", "There is no verification to resend.");
    public static readonly Error EmailTaken = Error.Conflict("registration.email_taken", "This email address is already registered.");
    public static readonly Error MobileTaken = Error.Conflict("registration.mobile_taken", "This mobile number is already in use.");
    public static readonly Error UnderMinimumAge = Error.Validation("registration.under_minimum_age", "You must be at least 13 years old to register.");
    public static readonly Error UnknownDisabilityType = Error.Validation("registration.unknown_disability_type", "An unknown disability type was supplied.");
    public static readonly Error UnknownAvatar = Error.Validation("profile.unknown_avatar", "The avatar does not exist.");
    public static readonly Error ExternalTokenInvalid = Error.Unauthorized("auth.external_token_invalid", "The social login token could not be verified.");
    public static readonly Error ExternalEmailUnverified = Error.Validation("auth.external_email_unverified", "The provider did not verify the email address.");
    public static readonly Error ExternalLoginNotRegistered = Error.NotFound("auth.external_login_not_registered", "This social account is not registered.");
    public static readonly Error ExternalAlreadyRegistered = Error.Conflict("registration.external_already_registered", "This social account is already registered. Sign in instead.");
    public static readonly Error PasswordPolicy = Error.Validation("password.policy", "The password does not meet the password policy.");
    public static readonly Error PasswordSameAsCurrent = Error.Validation("password.same_as_current", "The new password must differ from the current password.");
    public static readonly Error PasswordNotApplicable = Error.Forbidden("password.not_applicable", "Accounts created with Google or Apple do not use a password.");
    public static readonly Error UserNotFound = Error.NotFound("user.not_found", "The user was not found.");
    public static readonly Error SessionNotFound = Error.NotFound("session.not_found", "The session was not found.");
    public static readonly Error QuickLoginNotRegistered = Error.NotFound("quick_login.not_registered", "Quick Login is not enabled on this device.");
    public static readonly Error QuickLoginKeyInvalid = Error.Validation("quick_login.public_key_invalid", "The public key is not a valid key for the requested algorithm.");
    public static readonly Error QuickLoginChallengeInvalid = Error.Unauthorized("quick_login.challenge_invalid", "The challenge is missing, used or expired.");
    public static readonly Error QuickLoginProofInvalid = Error.Unauthorized("quick_login.proof_invalid", "The signature could not be verified.");
    public static readonly Error ConcurrentUpdate = Error.Conflict("concurrency.conflict", "The record was changed by someone else. Reload and try again.");

    public static Error ValidationFailed(FluentValidation.Results.ValidationResult result) =>
        Error.Validation("validation.failed", string.Join(" ", result.Errors.Select(e => e.ErrorMessage).Distinct()));
}

internal static class ValidationExtensions
{
    /// <summary>Returns null when valid; otherwise a single validation error carrying every message.</summary>
    public static async Task<Error?> CheckAsync<T>(this IValidator<T> validator, T instance, CancellationToken ct)
    {
        var result = await validator.ValidateAsync(instance, ct);
        return result.IsValid ? null : IdentityErrors.ValidationFailed(result);
    }
}
