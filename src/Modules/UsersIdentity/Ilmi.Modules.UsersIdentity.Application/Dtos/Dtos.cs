using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;

namespace Ilmi.Modules.UsersIdentity.Application.Dtos;

public sealed record OtpRequestedDto(DateTime ExpiresAt, int ResendsRemaining);
public sealed record SessionDto(Guid SessionId, Guid UserId, string Token, DateTime IssuedAt, DateTime? ExpiresAt);
/// <param name="PasswordEmailed">False when the email could not be sent (the user can use "forgot password").</param>
public sealed record RegisteredDto(Guid UserId, string Email, bool PasswordEmailed);
public sealed record RuleDto(short Id, string Code, string NameEn, string? NameAr);
public sealed record DisabilityTypeDto(short Id, string Code, string NameEn, string NameAr, byte SortOrder);
public sealed record UserSettingsDto(string Language, bool EmailNotificationsEnabled);
public sealed record QuickLoginRegisteredDto(Guid RegistrationId, string DeviceId, DateTime EnabledAt, string KeyFingerprint);
public sealed record QuickLoginChallengeDto(string Challenge, DateTime ExpiresAt);

public sealed record UserProfileDto(
    Guid Id, string UserKind, string FirstName, string LastName, string Email,
    string? MobileCountryCode, string? MobileNationalNumber, DateOnly? DateOfBirth, string? Gender, string? UserType,
    string AccountStatus, bool IsActive, string? QrCode, short? AvatarId, bool ReceiveNewsletter, bool NotifyIlmiSpecialNeeds,
    DateTime? PendingActivationExpiresAt, IReadOnlyList<RuleDto> Rules);

public static class Mapping
{
    public static string ToCode(this Language language) => language == Language.Ar ? "ar" : "en";

    public static bool TryParseLanguage(string? code, out Language language)
    {
        language = default;
        if (string.Equals(code, "ar", StringComparison.OrdinalIgnoreCase)) { language = Language.Ar; return true; }
        if (string.Equals(code, "en", StringComparison.OrdinalIgnoreCase)) { language = Language.En; return true; }
        return false;
    }

    public static RuleDto ToDto(this Rule r) => new(r.Id, r.Code, r.NameEn, r.NameAr);
    public static DisabilityTypeDto ToDto(this DisabilityType d) => new(d.Id, d.Code, d.NameEn, d.NameAr, d.SortOrder);

    public static UserProfileDto ToProfile(this User u, IReadOnlyList<Rule> allRules) => new(
        u.Id, u.UserKind.ToString(), u.FirstName, u.LastName, u.Email!, u.MobileCountryCode, u.MobileNationalNumber,
        u.DateOfBirth, u.Gender?.ToString(), u.UserType?.ToString(), u.AccountStatus.ToString(), u.IsActive, u.QrCode,
        u.AvatarId, u.ReceiveNewsletter, u.NotifyIlmiSpecialNeeds, u.PendingActivationExpiresAt,
        allRules.Where(r => u.HasRule(r.Id)).OrderBy(r => r.SortOrder).Select(r => r.ToDto()).ToList());
}
