using Ilmi.Modules.UsersIdentity.Domain;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.Rules;
using Ilmi.Modules.UsersIdentity.Domain.ValueObjects;

namespace Ilmi.Modules.UsersIdentity.Tests;

public class AgeBandsTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);

    [Theory]
    [InlineData("2020-11-26", null)]            // turns 6 tomorrow: below the minimum account age
    [InlineData("2020-11-25", "Child")]         // 6th birthday today
    [InlineData("2013-11-26", "Child")]         // 12 years old, one day before the 13th birthday: still a Child
    [InlineData("2013-11-25", "Teen")]          // 13th birthday today
    [InlineData("2009-11-26", "Teen")]          // 16 years old, one day before the 17th birthday
    [InlineData("2009-11-25", "Adult")]         // 17th birthday today
    [InlineData("1980-01-01", "Adult")]
    public void Classifies_by_completed_years(string dob, string? expected) =>
        Assert.Equal(expected, AgeBands.Classify(DateOnly.Parse(dob), Today)?.ToString());

    [Fact]
    public void A_twelve_year_old_is_a_child() =>
        Assert.Equal(UserType.Child, AgeBands.Classify(new DateOnly(2014, 1, 1), Today));

    [Fact]
    public void Age_counts_completed_years_only() =>
        Assert.Equal(12, AgeBands.AgeInYears(new DateOnly(2013, 11, 26), Today));   // the 13th birthday is tomorrow
}

public class EmailAddressTests
{
    [Theory]
    [InlineData("  Ali.Mohamed@Example.COM ", "ALI.MOHAMED@EXAMPLE.COM")]
    [InlineData("a@b.co", "A@B.CO")]
    public void Normalizes_for_uniqueness_and_login(string input, string expected) => Assert.Equal(expected, EmailAddress.Normalize(input));

    [Theory]
    [InlineData("a@b.co", true)]
    [InlineData("no-at-sign", false)]
    [InlineData("two@@signs.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_format(string? email, bool valid) => Assert.Equal(valid, EmailAddress.IsValid(email));
}

public class QrCodeTests
{
    [Fact]
    public void Generated_codes_are_ten_characters_of_digits_and_capitals()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = QrCode.Generate();
            Assert.Equal(10, code.Length);
            Assert.True(QrCode.IsValid(code));
        }
    }

    [Theory]
    [InlineData("abcdefghij")]
    [InlineData("ABC123")]
    [InlineData("ABCDE-1234")]
    public void Rejects_invalid_codes(string code) => Assert.False(QrCode.IsValid(code));
}

public class UserTests
{
    private static readonly DateOnly Today = new(2026, 11, 25);
    private static readonly DateTime Now = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

    private static NewExternalUser Data(DateOnly? dob = null, string email = "sara@example.com") => new(
        "Sara", "Alharbi", email, "+966", "501234567", dob ?? new DateOnly(1990, 5, 20), Gender.Female, true, false,
        QrCode.Generate(), null, Now, Language.En);

    private static User External(DateOnly? dob = null) => User.CreateExternal(Data(dob), Today, Now).Value;

    [Fact]
    public void External_user_gets_type_rule_settings_and_history_from_the_date_of_birth()
    {
        var adult = External(new DateOnly(1990, 5, 20));
        Assert.Equal(UserKind.External, adult.UserKind);
        Assert.Equal(UserType.Adult, adult.UserType);
        Assert.True(adult.HasRule(RuleIds.Adult));
        Assert.NotNull(adult.Settings);
        Assert.Equal(AccountStatus.Active, adult.AccountStatus);
        var history = Assert.Single(adult.RuleHistory);
        Assert.Equal(RuleChangeReason.Registered, history.Reason);
        Assert.Null(history.FromRuleId);

        Assert.Equal(UserType.Teen, External(new DateOnly(2010, 11, 26)).UserType);
        Assert.True(External(new DateOnly(2010, 11, 26)).HasRule(RuleIds.Teen));
        Assert.Equal(UserType.Child, External(new DateOnly(2014, 1, 1)).UserType);
    }

    [Fact]
    public void Email_is_stored_with_its_normalized_form()
    {
        var user = User.CreateExternal(Data(email: "  Sara@Example.com "), Today, Now).Value;
        Assert.Equal("Sara@Example.com", user.Email);
        Assert.Equal("SARA@EXAMPLE.COM", user.NormalizedEmail);
    }

    [Fact]
    public void Internal_user_has_no_age_classification_profile_data_or_rule()
    {
        var user = User.CreateInternal("Omar", "Staff", "omar@ilmi.example", Language.Ar, Now).Value;
        Assert.Equal(UserKind.Internal, user.UserKind);
        Assert.Null(user.UserType);
        Assert.Null(user.DateOfBirth);
        Assert.Null(user.QrCode);
        Assert.Null(user.MobileNationalNumber);
        Assert.Empty(user.RuleAssignments);
        Assert.False(user.IsExternal);
    }

    [Fact]
    public void The_user_is_an_Identity_user_with_exactly_one_matching_profile()
    {
        var external = External();
        // the Domain user is a plain class: no ASP.NET Core Identity base type, no Identity members
        Assert.Equal(typeof(object), typeof(User).BaseType);
        Assert.DoesNotContain(typeof(User).Assembly.GetReferencedAssemblies(), a => a.Name!.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(external.ExternalUser);
        Assert.Null(external.InternalUser);
        Assert.Equal(external.Id, external.ExternalUser!.UserId);
        Assert.Equal(UserKind.External, external.ExternalUser.UserKind);

        var staff = User.CreateInternal("Omar", "Staff", "omar@ilmi.example", Language.Ar, Now, "+966", "500000001", new DateOnly(1988, 3, 4)).Value;
        Assert.NotNull(staff.InternalUser);
        Assert.Null(staff.ExternalUser);
        Assert.Equal(UserKind.Internal, staff.InternalUser!.UserKind);
        Assert.Equal(("+966", "500000001", new DateOnly(1988, 3, 4)), (staff.MobileCountryCode, staff.MobileNationalNumber, staff.DateOfBirth!.Value));
        Assert.Null(staff.UserType);                                       // no age classification for staff
    }

    [Fact]
    public void Internal_user_cannot_hold_external_only_data()
    {
        var user = User.CreateInternal("Omar", "Staff", "omar@ilmi.example", Language.Ar, Now).Value;
        Assert.Equal(UserErrors.ExternalOnly, user.ReplaceAccessibilityNeeds([1], Now).Error);
        Assert.Equal(UserErrors.ExternalOnly, user.UpdateExternalProfile("A", "B", null, null, false, false).Error);
    }

    [Theory]
    [InlineData("2020-11-26")]   // under 6
    [InlineData("2030-01-01")]   // future
    public void Rejects_dates_of_birth_without_a_valid_age(string dob) =>
        Assert.True(User.CreateExternal(Data(DateOnly.Parse(dob)), Today, Now).IsFailure);

    [Theory]
    [InlineData("966", "501234567")]
    [InlineData("+966", "12ab")]
    [InlineData("+966", "123")]
    public void Rejects_invalid_mobile_numbers(string code, string number)
    {
        var data = Data() with { MobileCountryCode = code, MobileNationalNumber = number };
        Assert.Equal(UserErrors.MobileInvalid, User.CreateExternal(data, Today, Now).Error);
    }

    [Fact]
    public void UserKind_UserType_and_Rule_are_three_separate_things()
    {
        var parent = External(new DateOnly(1985, 1, 1));
        Assert.True(parent.ReplaceRule(RuleIds.Adult, RuleIds.Parent, RuleChangeReason.DependentAdded, Now, null, Guid.NewGuid()).IsSuccess);
        Assert.Equal(UserKind.External, parent.UserKind);
        Assert.Equal(UserType.Adult, parent.UserType);          // a Parent stays an Adult
        Assert.True(parent.HasRule(RuleIds.Parent));
        Assert.False(parent.HasRule(RuleIds.Adult));
    }

    [Fact]
    public void Rule_assignment_rejects_duplicates_and_records_append_only_history()
    {
        var user = External();
        Assert.Equal(UserErrors.RuleAlreadyAssigned, user.AssignRule(RuleIds.Adult, RuleChangeReason.Registered, Now, null, null).Error);

        var linkId = Guid.NewGuid();
        Assert.True(user.ReplaceRule(RuleIds.Adult, RuleIds.Parent, RuleChangeReason.DependentAdded, Now, null, linkId).IsSuccess);
        Assert.True(user.ReplaceRule(RuleIds.Parent, RuleIds.Adult, RuleChangeReason.LastDependentUnlinked, Now, null, linkId).IsSuccess);
        Assert.Equal(3, user.RuleHistory.Count);
        Assert.Equal(RuleChangeReason.DependentAdded, user.RuleHistory.ElementAt(1).Reason);
        Assert.Equal(linkId, user.RuleHistory.ElementAt(1).RelatedFamilyLinkId);
    }

    [Fact]
    public void Replacing_a_rule_that_is_not_assigned_fails() =>
        Assert.Equal(UserErrors.RuleNotAssigned, External().ReplaceRule(RuleIds.Parent, RuleIds.Adult, RuleChangeReason.LastDependentUnlinked, Now, null, null).Error);

    [Fact]
    public void The_model_does_not_force_a_single_Rule()
    {
        // Whether a user may hold several Rules is an open business decision: the domain neither forbids nor requires it.
        var user = External();
        Assert.True(user.AssignRule(RuleIds.RegistrationOfficer, RuleChangeReason.OfficerAssigned, Now, null, null).IsSuccess);
        Assert.Equal(2, user.RuleAssignments.Count);
    }

    [Fact]
    public void Declared_needs_reject_duplicates_and_can_be_replaced()
    {
        var user = External();
        Assert.Equal(UserErrors.DuplicateDisabilityType, user.ReplaceAccessibilityNeeds([1, 1], Now).Error);
        Assert.True(user.ReplaceAccessibilityNeeds([1, 3], Now).IsSuccess);
        Assert.True(user.ReplaceAccessibilityNeeds([3, 4], Now).IsSuccess);
        Assert.Equal(new short[] { 3, 4 }, user.AccessibilityNeeds.Select(n => n.DisabilityTypeId).Order().ToArray());
    }

    [Fact]
    public void Pending_activation_lasts_ten_minutes()
    {
        var user = User.CreateExternal(Data(new DateOnly(2014, 1, 1), "kid@example.com"), Today, Now, TimeSpan.FromMinutes(10)).Value;
        Assert.Equal(AccountStatus.PendingActivation, user.AccountStatus);
        Assert.Equal(Now.AddMinutes(10), user.PendingActivationExpiresAt);
        Assert.False(user.CanSignIn);

        var late = User.CreateExternal(Data(new DateOnly(2014, 1, 1), "kid2@example.com"), Today, Now, TimeSpan.FromMinutes(10)).Value;
        Assert.Equal(UserErrors.ActivationExpired, late.ActivatePendingAccount(Now.AddMinutes(10).AddSeconds(1)).Error);

        Assert.True(user.ActivatePendingAccount(Now.AddMinutes(9)).IsSuccess);
        Assert.Equal(AccountStatus.Active, user.AccountStatus);
        Assert.Null(user.PendingActivationExpiresAt);
        Assert.Equal(UserErrors.NotPendingActivation, user.ActivatePendingAccount(Now).Error);
    }

    [Fact]
    public void IsActive_and_AccountStatus_are_independent()
    {
        var user = External();
        user.SetAdministrativeAccess(false);
        Assert.Equal(AccountStatus.Active, user.AccountStatus);   // lifecycle untouched
        Assert.False(user.CanSignIn);                              // but the gate is closed
        user.SetAdministrativeAccess(true);
        Assert.True(user.CanSignIn);

        user.Deactivate();
        Assert.Equal(AccountStatus.Deactivated, user.AccountStatus);
        Assert.True(user.IsActive);                                // the gate is untouched
        Assert.False(user.CanSignIn);
    }

    [Fact]
    public void A_new_user_can_sign_in_only_when_status_is_Active_and_the_gate_is_open()
    {
        Assert.True(External().CanSignIn);
        Assert.True(User.CreateInternal("O", "S", "o@ilmi.example", Language.En, Now).Value.CanSignIn);
    }
}

public class SessionAndQuickLoginDomainTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Session_never_expires_by_itself_and_revocation_is_idempotent()
    {
        var session = UserSession.Create(Guid.NewGuid(), ClientType.Web, null, [1, 2, 3], Now);
        Assert.Null(session.ExpiresAt);
        Assert.True(session.IsActive(Now.AddDays(3650)));            // no inactivity or age limit

        session.Revoke(SessionRevokedReason.UserLogout, Now.AddHours(1));
        session.Revoke(SessionRevokedReason.SystemRevoked, Now.AddHours(2));
        Assert.False(session.IsActive(Now.AddHours(1)));
        Assert.Equal(SessionRevokedReason.UserLogout, session.RevokedReason);
        Assert.Equal(Now.AddHours(1), session.RevokedAt);
    }

    [Fact]
    public void Quick_login_registration_holds_only_a_public_key_and_can_be_disabled_once()
    {
        var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256).ExportSubjectPublicKeyInfo();
        var registration = QuickLoginRegistration.Register(Guid.NewGuid(), "device-1", DevicePlatform.iOS, key, KeyAlgorithms.Es256, Now);
        Assert.True(registration.IsEnabled);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(key), registration.KeyFingerprint);

        registration.IssueChallenge([1, 2], Now.AddMinutes(1));
        registration.Disable(QuickLoginDisabledReason.PasswordChanged, Now);
        registration.Disable(QuickLoginDisabledReason.UserDisabled, Now.AddMinutes(5));
        Assert.False(registration.IsEnabled);
        Assert.Equal(QuickLoginDisabledReason.PasswordChanged, registration.DisabledReason);
        Assert.Null(registration.ChallengeNonce);   // a disabled registration holds no challenge
    }
}

public class OtpDomainTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

    private static OtpChallenge Issue(byte[]? payload = null) =>
        OtpChallenge.Issue(OtpPurpose.Login, "a@b.co", null, null, [1, 2, 3], Now, TimeSpan.FromSeconds(180), payload);

    [Fact]
    public void Channel_is_always_email() => Assert.Equal(OtpChannel.Email, Issue().Channel);

    [Fact]
    public void Expires_after_the_lifetime_and_is_single_use()
    {
        var otp = Issue([9, 9]);
        Assert.True(otp.IsUsable(Now.AddSeconds(179)));
        Assert.False(otp.IsUsable(Now.AddSeconds(180)));

        otp.Consume(Now.AddSeconds(10));
        Assert.False(otp.IsUsable(Now.AddSeconds(11)));
        Assert.Null(otp.PendingPayload);            // the encrypted payload is wiped on use
    }

    [Fact]
    public void Resend_reuses_the_challenge_and_does_not_reset_attempts()
    {
        var otp = Issue();
        otp.RegisterFailedAttempt();
        otp.RegisterFailedAttempt();
        otp.Reissue([4, 5, 6], Now.AddSeconds(60), TimeSpan.FromSeconds(180));
        Assert.Equal(2, otp.AttemptCount);
        Assert.Equal(1, otp.ResendCount);
        Assert.Equal(Now.AddSeconds(240), otp.ExpiresAt);
        Assert.True(otp.CanResend(3));
        otp.Reissue([7], Now, TimeSpan.FromSeconds(180));
        otp.Reissue([8], Now, TimeSpan.FromSeconds(180));
        Assert.False(otp.CanResend(3));
    }

    [Fact]
    public void Lockout_without_a_threshold_counts_but_never_locks()
    {
        var lockout = VerificationLockout.Create(OtpPurpose.DependentVerification, "parent");
        for (var i = 0; i < 20; i++) Assert.False(lockout.RegisterFailure(Now, null, TimeSpan.FromMinutes(10)));
        Assert.False(lockout.IsLocked(Now));
    }

    [Fact]
    public void Lockout_with_a_threshold_locks_for_the_duration()
    {
        var lockout = VerificationLockout.Create(OtpPurpose.DependentVerification, "parent");
        Assert.False(lockout.RegisterFailure(Now, 2, TimeSpan.FromMinutes(10)));
        Assert.True(lockout.RegisterFailure(Now, 2, TimeSpan.FromMinutes(10)));
        Assert.True(lockout.IsLocked(Now.AddMinutes(9)));
        Assert.False(lockout.IsLocked(Now.AddMinutes(10)));
    }
}
