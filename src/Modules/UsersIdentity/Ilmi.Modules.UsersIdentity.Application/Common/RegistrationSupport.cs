using System.Text.Json;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.ValueObjects;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Common;

/// <summary>Database index names the application reacts to (the Infrastructure configuration uses the same constants).</summary>
public static class ConstraintNames
{
    public const string UniqueNormalizedEmail = "ux_users_normalized_email";
    /// <summary>One unique index on the shared users table: Internal and External users cannot share a mobile number; Children may.</summary>
    public const string UniqueMobile = "ux_users_mobile";
    public const string UniqueQrCode = "ux_external_users_qr_code";
    /// <summary>Identity's own unique index on the normalized user name (the user name is the email address).</summary>
    public const string UniqueUserName = "UserNameIndex";
}

/// <summary>The registration form, kept encrypted on the OTP challenge until the email is verified.</summary>
public sealed record RegistrationData(
    string FirstName, string LastName, string Email, string MobileCountryCode, string MobileNationalNumber,
    DateOnly DateOfBirth, Gender? Gender, bool ReceiveNewsletter, bool NotifyIlmiSpecialNeeds,
    List<short> DisabilityTypeIds, Language Language, DateTime TermsAcceptedAt);

public static class RegistrationPayload
{
    public static byte[] Serialize(RegistrationData data) => JsonSerializer.SerializeToUtf8Bytes(data);
    public static RegistrationData? Deserialize(byte[] json) => JsonSerializer.Deserialize<RegistrationData>(json);
}

/// <summary>Rules shared by every way of registering: age, unique email, unique mobile (except Children), known lookups.</summary>
public sealed class RegistrationChecks(IUserRepository users, TimeProvider clock, IdentitySecurityOptions options)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<Error?> CheckAsync(RegistrationData d, CancellationToken ct)
    {
        if (AgeBands.AgeInYears(d.DateOfBirth, Today) < options.SelfRegistrationMinimumAge) return IdentityErrors.UnderMinimumAge;
        if (await users.EmailExistsAsync(EmailAddress.Normalize(d.Email), ct)) return IdentityErrors.EmailTaken;
        // Self-registered users are Teen or Adult, so the mobile number must be unique among non-Children.
        if (await users.MobileInUseAsync(d.MobileCountryCode, d.MobileNationalNumber, ct)) return IdentityErrors.MobileTaken;
        if (d.DisabilityTypeIds.Count > 0)
        {
            var known = (await users.GetDisabilityTypesAsync(ct)).Where(t => t.IsActive).Select(t => t.Id).ToHashSet();
            if (d.DisabilityTypeIds.Any(id => !known.Contains(id))) return IdentityErrors.UnknownDisabilityType;
        }
        return null;
    }
}

/// <summary>Builds the External user, its profile, settings, Rule and declared needs, with a unique QR code. Does not save: the account manager creates it.</summary>
public sealed class ExternalUserProvisioner(IUserRepository users, RegistrationChecks checks, TimeProvider clock)
{
    public async Task<Result<User>> CreateAsync(RegistrationData d, CancellationToken ct)
    {
        string? qr = null;
        for (var attempt = 0; attempt < 5 && qr is null; attempt++)
        {
            var candidate = QrCode.Generate();
            if (!await users.QrCodeExistsAsync(candidate, ct)) qr = candidate;
        }
        if (qr is null) return Error.Failure("registration.qr_code_unavailable", "A QR code could not be generated. Try again.");

        var now = clock.GetUtcNow().UtcDateTime;
        var created = User.CreateExternal(new NewExternalUser(d.FirstName, d.LastName, d.Email, d.MobileCountryCode,
            d.MobileNationalNumber, d.DateOfBirth, d.Gender, d.ReceiveNewsletter, d.NotifyIlmiSpecialNeeds, qr, null,
            d.TermsAcceptedAt, d.Language), checks.Today, now);
        if (created.IsFailure) return created.Error!;

        var needs = created.Value.ReplaceAccessibilityNeeds(d.DisabilityTypeIds, now);
        if (needs.IsFailure) return needs.Error!;
        return created.Value;
    }

    /// <summary>Maps a unique-index violation raised while saving to the matching business error.</summary>
    public static Error MapUniqueViolation(UniqueConstraintViolationException ex) =>
        ex.ConstraintName.Contains(ConstraintNames.UniqueNormalizedEmail, StringComparison.OrdinalIgnoreCase)
            || ex.ConstraintName.Contains(ConstraintNames.UniqueUserName, StringComparison.OrdinalIgnoreCase) ? IdentityErrors.EmailTaken
        : ex.ConstraintName.Contains(ConstraintNames.UniqueMobile, StringComparison.OrdinalIgnoreCase) ? IdentityErrors.MobileTaken
        : Error.Conflict("registration.conflict", "The account could not be created because of a conflict. Try again.");
}
