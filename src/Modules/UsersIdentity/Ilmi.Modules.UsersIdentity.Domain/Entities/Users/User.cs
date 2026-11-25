using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.Rules;
using Ilmi.Modules.UsersIdentity.Domain.ValueObjects;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Domain.Entities;

/// <summary>Data needed to create an External account (self registration or dependent).</summary>
public sealed record NewExternalUser(
    string FirstName, string LastName, string Email, string MobileCountryCode, string MobileNationalNumber,
    DateOnly DateOfBirth, Gender? Gender, bool ReceiveNewsletter, bool NotifyIlmiSpecialNeeds,
    string QrCode, short? AvatarId, DateTime TermsAcceptedAt, Language Language);

/// <summary>
/// The user account of the domain. It is a plain class: it knows nothing about ASP.NET Core Identity. The Infrastructure layer maps
/// it onto the Identity users table (<c>AspNetUsers</c>), where it shares one row with Identity's own user entity (table splitting).
/// Passwords, lockout and external logins are Identity concerns handled behind <c>IUserAccountManager</c>.
/// UserKind, UserType and Rule are three separate concepts: UserKind = External/Internal, UserType = age band (External only),
/// Rule = business category (UserRuleAssignment). AccountStatus (lifecycle) and IsActive (administrative access gate) are independent.
/// The mobile number lives here, next to UserType, so that one database index can enforce its uniqueness across Internal and External users.
/// </summary>
public sealed class User : IAuditable
{
    private readonly List<UserAccessibilityNeed> _accessibilityNeeds = [];
    private readonly List<UserRuleAssignment> _ruleAssignments = [];
    private readonly List<UserRuleHistory> _ruleHistory = [];

    private User() { }

    public Guid Id { get; private set; }
    public UserKind UserKind { get; private set; }
    public string FirstName { get; private set; } = "";
    public string LastName { get; private set; } = "";
    /// <summary>The login identifier (email only), as entered.</summary>
    public string Email { get; private set; } = "";
    public string NormalizedEmail { get; private set; } = "";
    public string? MobileCountryCode { get; private set; }
    public string? MobileNationalNumber { get; private set; }
    /// <summary>Age classification: Child 6-12, Teen 13-16, Adult 17+. Null for Internal users. Not a Rule.</summary>
    public UserType? UserType { get; private set; }
    public AccountStatus AccountStatus { get; private set; }
    /// <summary>End of the 10-minute Pending Activation window. Nothing is decided about what happens after it expires.</summary>
    public DateTime? PendingActivationExpiresAt { get; private set; }
    /// <summary>Administrative access gate. Separate from <see cref="AccountStatus"/>.</summary>
    public bool IsActive { get; private set; } = true;

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    /// <summary>Maintained automatically by the persistence interceptor whenever the user (or its profile) is modified.</summary>
    public DateTime UpdatedOn { get; set; }
    public Guid? UpdatedBy { get; set; }
    public byte[] RowVersion { get; private set; } = [];

    public ExternalUser? ExternalUser { get; private set; }
    public InternalUser? InternalUser { get; private set; }
    public UserSettings? Settings { get; private set; }
    public IReadOnlyCollection<UserAccessibilityNeed> AccessibilityNeeds => _accessibilityNeeds;
    public IReadOnlyCollection<UserRuleAssignment> RuleAssignments => _ruleAssignments;
    public IReadOnlyCollection<UserRuleHistory> RuleHistory => _ruleHistory;

    // ---- read-only views of the profile tables (not mapped)
    public DateOnly? DateOfBirth => ExternalUser?.DateOfBirth ?? InternalUser?.DateOfBirth;
    public Gender? Gender => ExternalUser?.Gender;
    public string? QrCode => ExternalUser?.QrCode;
    public short? AvatarId => ExternalUser?.AvatarId;
    public bool ReceiveNewsletter => ExternalUser?.ReceiveNewsletter ?? false;
    public bool NotifyIlmiSpecialNeeds => ExternalUser?.NotifyIlmiSpecialNeeds ?? false;
    public DateTime? TermsAcceptedAt => ExternalUser?.TermsAcceptedAt;

    public bool IsExternal => UserKind == UserKind.External;

    /// <summary>A user may sign in only when the administrative gate is open AND the lifecycle status is Active.</summary>
    public bool CanSignIn => IsActive && AccountStatus == AccountStatus.Active;

    // ---------------------------------------------------------------- creation
    public static Result<User> CreateExternal(NewExternalUser d, DateOnly today, DateTime now, TimeSpan? pendingActivationWindow = null)
    {
        if (!IsName(d.FirstName)) return UserErrors.FirstNameRequired;
        if (!IsName(d.LastName)) return UserErrors.LastNameRequired;
        if (!EmailAddress.IsValid(d.Email)) return UserErrors.EmailInvalid;
        if (!IsMobile(d.MobileCountryCode, d.MobileNationalNumber)) return UserErrors.MobileInvalid;
        if (d.DateOfBirth > today) return UserErrors.DateOfBirthInvalid;
        var type = AgeBands.Classify(d.DateOfBirth, today);
        if (type is null) return UserErrors.BelowMinimumAge;
        if (!Domain.ValueObjects.QrCode.IsValid(d.QrCode)) return UserErrors.QrCodeInvalid;

        var user = NewAccount(UserKind.External, d.FirstName, d.LastName, d.Email, now);
        user.MobileCountryCode = d.MobileCountryCode.Trim();
        user.MobileNationalNumber = d.MobileNationalNumber.Trim();
        user.UserType = type;
        user.AccountStatus = pendingActivationWindow is null ? AccountStatus.Active : AccountStatus.PendingActivation;
        user.PendingActivationExpiresAt = pendingActivationWindow is null ? null : now + pendingActivationWindow.Value;
        user.ExternalUser = ExternalUser.Create(user.Id, d.DateOfBirth, d.Gender, d.QrCode, d.AvatarId, d.ReceiveNewsletter,
            d.NotifyIlmiSpecialNeeds, d.TermsAcceptedAt);
        user.Settings = UserSettings.CreateDefault(user.Id, d.Language, now);
        user.AssignRule(RuleIds.ForUserType(type.Value), RuleChangeReason.Registered, now, null, null);
        return user;
    }

    /// <summary>
    /// Internal accounts have no age classification. Their optional contact data is the mobile number (on the shared account, so it is
    /// unique across Internal and External users) and the date of birth (internal profile). Employee attributes are not defined by the
    /// requirements and are not invented.
    /// </summary>
    public static Result<User> CreateInternal(string firstName, string lastName, string email, Language language, DateTime now,
        string? mobileCountryCode = null, string? mobileNationalNumber = null, DateOnly? dateOfBirth = null)
    {
        if (!IsName(firstName)) return UserErrors.FirstNameRequired;
        if (!IsName(lastName)) return UserErrors.LastNameRequired;
        if (!EmailAddress.IsValid(email)) return UserErrors.EmailInvalid;
        var hasMobile = mobileCountryCode is not null || mobileNationalNumber is not null;
        if (hasMobile && !IsMobile(mobileCountryCode, mobileNationalNumber)) return UserErrors.MobileInvalid;

        var user = NewAccount(UserKind.Internal, firstName, lastName, email, now);
        user.MobileCountryCode = mobileCountryCode?.Trim();
        user.MobileNationalNumber = mobileNationalNumber?.Trim();
        user.AccountStatus = AccountStatus.Active;
        user.InternalUser = InternalUser.Create(user.Id, dateOfBirth);
        user.Settings = UserSettings.CreateDefault(user.Id, language, now);
        return user;
    }

    private static User NewAccount(UserKind kind, string firstName, string lastName, string email, DateTime now) => new()
    {
        Id = SequentialGuid.Create(),
        UserKind = kind,
        FirstName = firstName.Trim(),
        LastName = lastName.Trim(),
        Email = email.Trim(),
        NormalizedEmail = EmailAddress.Normalize(email),
        CreatedAt = now,
        UpdatedOn = now
    };

    // ---------------------------------------------------------------- profile
    public Result Rename(string firstName, string lastName)
    {
        if (!IsName(firstName)) return UserErrors.FirstNameRequired;
        if (!IsName(lastName)) return UserErrors.LastNameRequired;
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        return Result.Success();
    }

    public Result UpdateExternalProfile(string firstName, string lastName, Gender? gender, short? avatarId, bool receiveNewsletter, bool notifyIlmiSpecialNeeds)
    {
        if (ExternalUser is null) return UserErrors.ExternalOnly;
        var renamed = Rename(firstName, lastName);
        if (renamed.IsFailure) return renamed;
        ExternalUser.UpdatePreferences(gender, avatarId, receiveNewsletter, notifyIlmiSpecialNeeds);
        return Result.Success();
    }

    public Result ReplaceAccessibilityNeeds(IReadOnlyCollection<short> disabilityTypeIds, DateTime now)
    {
        if (!IsExternal) return UserErrors.ExternalOnly;
        if (disabilityTypeIds.Distinct().Count() != disabilityTypeIds.Count) return UserErrors.DuplicateDisabilityType;
        _accessibilityNeeds.RemoveAll(n => !disabilityTypeIds.Contains(n.DisabilityTypeId));
        foreach (var id in disabilityTypeIds.Where(id => _accessibilityNeeds.All(n => n.DisabilityTypeId != id)))
            _accessibilityNeeds.Add(new UserAccessibilityNeed(Id, id, now));
        return Result.Success();
    }

    // ---------------------------------------------------------------- lifecycle (only transitions the SRS defines)
    /// <summary>PendingActivation -> Active, within the activation window.</summary>
    public Result ActivatePendingAccount(DateTime now)
    {
        if (AccountStatus != AccountStatus.PendingActivation) return UserErrors.NotPendingActivation;
        if (PendingActivationExpiresAt is { } expires && now > expires) return UserErrors.ActivationExpired;
        AccountStatus = AccountStatus.Active;
        PendingActivationExpiresAt = null;
        return Result.Success();
    }

    /// <summary>Accounts are deactivated, never hard-deleted. Reactivation is not defined by the requirements and is not offered.</summary>
    public void Deactivate()
    {
        AccountStatus = AccountStatus.Deactivated;
        PendingActivationExpiresAt = null;
    }

    public void SetAdministrativeAccess(bool isActive) => IsActive = isActive;

    // ---------------------------------------------------------------- Rules (separate from UserType and from security roles)
    public Result AssignRule(short ruleId, RuleChangeReason reason, DateTime now, Guid? changedBy, Guid? relatedFamilyLinkId)
    {
        if (_ruleAssignments.Any(a => a.RuleId == ruleId)) return UserErrors.RuleAlreadyAssigned;
        _ruleAssignments.Add(new UserRuleAssignment(Id, ruleId, now, changedBy));
        _ruleHistory.Add(UserRuleHistory.Record(Id, null, ruleId, reason, now, changedBy, relatedFamilyLinkId));
        return Result.Success();
    }

    /// <summary>Swaps one assigned Rule for another and records the change. It does not assume the user holds only one Rule.</summary>
    public Result ReplaceRule(short fromRuleId, short toRuleId, RuleChangeReason reason, DateTime now, Guid? changedBy, Guid? relatedFamilyLinkId)
    {
        var current = _ruleAssignments.FirstOrDefault(a => a.RuleId == fromRuleId);
        if (current is null) return UserErrors.RuleNotAssigned;
        if (_ruleAssignments.Any(a => a.RuleId == toRuleId)) return UserErrors.RuleAlreadyAssigned;
        _ruleAssignments.Remove(current);
        _ruleAssignments.Add(new UserRuleAssignment(Id, toRuleId, now, changedBy));
        _ruleHistory.Add(UserRuleHistory.Record(Id, fromRuleId, toRuleId, reason, now, changedBy, relatedFamilyLinkId));
        return Result.Success();
    }

    public bool HasRule(short ruleId) => _ruleAssignments.Any(a => a.RuleId == ruleId);

    private static bool IsName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 250;

    private static bool IsMobile(string? code, string? number) =>
        code is { Length: >= 2 and <= 5 } && code[0] == '+' && code.Skip(1).All(char.IsAsciiDigit) &&
        number is { Length: >= 4 and <= 15 } && number.All(char.IsAsciiDigit);
}
