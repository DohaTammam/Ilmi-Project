using System.Security.Cryptography;
using System.Text;
using Ilmi.Modules.UsersIdentity.Application.Commands.Identity;
using Ilmi.Modules.UsersIdentity.Application.Commands.Users;
using Ilmi.Modules.UsersIdentity.Application.Queries.Users;
using Ilmi.Modules.UsersIdentity.Application.Services;
using Ilmi.Modules.UsersIdentity.Contracts.Dtos;
using Ilmi.Modules.UsersIdentity.Contracts.Services;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.Rules;
using Ilmi.Modules.UsersIdentity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ilmi.Modules.UsersIdentity.Tests;

[Collection("sql")]
public class RegistrationFlowTests(SqlServerFixture db)
{
    [Fact]
    public async Task Registration_creates_the_account_hashes_the_generated_password_and_assigns_the_age_Rule()
    {
        await using var app = new TestApp(db);
        var (userId, email, password) = await app.RegisterAsync();

        Assert.Equal(16, password.Length);
        await using var ctx = db.NewContext();
        var user = await ctx.Accounts.Include(u => u.ExternalUser).Include(u => u.Settings).Include(u => u.RuleAssignments).AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal(UserKind.External, user.UserKind);
        Assert.Equal(UserType.Adult, user.UserType);
        Assert.Equal(AccountStatus.Active, user.AccountStatus);
        Assert.True(user.IsActive);
        Assert.Equal(EmailAddress.Normalize(email), user.NormalizedEmail);
        Assert.Equal(10, user.QrCode!.Length);
        Assert.Equal([RuleIds.Adult], user.RuleAssignments.Select(a => a.RuleId).ToArray());
        Assert.Equal(Language.En, user.Settings!.Language);
        Assert.Equal(1, await ctx.UserRuleHistory.CountAsync(h => h.UserId == userId && h.Reason == RuleChangeReason.Registered));

        var identityUser = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == userId);    // Identity's side of the same row
        Assert.False(string.IsNullOrEmpty(identityUser.PasswordHash));
        Assert.DoesNotContain(password, identityUser.PasswordHash);        // only Identity's salted hash is stored, never the password
    }

    [Fact]
    public async Task The_pending_form_is_stored_encrypted_and_the_otp_code_only_as_a_hash()
    {
        await using var app = new TestApp(db);
        var form = app.RegistrationForm();
        Assert.True((await app.Send(form)).IsSuccess);
        await using var ctx = db.NewContext();
        var challenge = await ctx.OtpChallenges.AsNoTracking().SingleAsync(c => c.TargetEmail == form.Email);
        Assert.Equal(OtpChannel.Email, challenge.Channel);
        Assert.Equal(32, challenge.CodeHash.Length);
        Assert.NotEqual(Encoding.UTF8.GetBytes(app.Email.LastOtp!), challenge.CodeHash.Take(4).ToArray());
        Assert.NotNull(challenge.PendingPayload);
        Assert.DoesNotContain(form.Email, Encoding.UTF8.GetString(challenge.PendingPayload!));   // ciphertext, not JSON text
        Assert.DoesNotContain("Alharbi", Encoding.UTF8.GetString(challenge.PendingPayload!));
        Assert.Equal(0, await ctx.Accounts.CountAsync(u => u.NormalizedEmail == EmailAddress.Normalize(form.Email)));   // no account before verification
    }

    [Fact]
    public async Task Registration_stores_declared_special_needs()
    {
        await using var app = new TestApp(db);
        var (userId, _, _) = await app.RegisterAsync(app.RegistrationForm(needs: [1, 3]));
        await using var ctx = db.NewContext();
        Assert.Equal(new short[] { 1, 3 }, await ctx.UserAccessibilityNeeds.Where(n => n.UserId == userId).Select(n => n.DisabilityTypeId).OrderBy(i => i).ToArrayAsync());
    }

    [Fact]
    public async Task Unknown_disability_types_are_rejected()
    {
        await using var app = new TestApp(db);
        var result = await app.Send(app.RegistrationForm(needs: [1, 42]));
        Assert.Equal("registration.unknown_disability_type", result.Error!.Code);
    }

    [Fact]
    public async Task Teens_register_with_the_Teen_Rule_and_children_under_thirteen_cannot_register_themselves()
    {
        await using var app = new TestApp(db);
        var (teenId, _, _) = await app.RegisterAsync(app.RegistrationForm(dob: new DateOnly(2012, 6, 1)));   // 14 years old
        await using var ctx = db.NewContext();
        var teen = await ctx.Accounts.Include(u => u.ExternalUser).Include(u => u.RuleAssignments).AsNoTracking().SingleAsync(u => u.Id == teenId);
        Assert.Equal(UserType.Teen, teen.UserType);
        Assert.Equal([RuleIds.Teen], teen.RuleAssignments.Select(a => a.RuleId).ToArray());

        var before = app.Email.OtpCount;
        var child = await app.Send(app.RegistrationForm(dob: new DateOnly(2014, 3, 1)));                    // 12 years old
        Assert.Equal("registration.under_minimum_age", child.Error!.Code);
        Assert.Equal(before, app.Email.OtpCount);                                                            // no code was sent
    }

    [Fact]
    public async Task Email_uniqueness_ignores_case_and_surrounding_spaces()
    {
        await using var app = new TestApp(db);
        var (_, email, _) = await app.RegisterAsync();
        var again = await app.Send(app.RegistrationForm(email: "  " + email.ToUpperInvariant() + " "));
        Assert.Equal("registration.email_taken", again.Error!.Code);
    }

    [Fact]
    public async Task Mobile_number_must_be_unique_among_adults()
    {
        await using var app = new TestApp(db);
        var mobile = TestApp.NewMobile();
        await app.RegisterAsync(app.RegistrationForm(mobile: mobile));
        var again = await app.Send(app.RegistrationForm(mobile: mobile));
        Assert.Equal("registration.mobile_taken", again.Error!.Code);
    }

    [Fact]
    public async Task A_number_used_by_an_Internal_user_cannot_be_used_to_register()
    {
        await using var app = new TestApp(db);
        var mobile = TestApp.NewMobile();
        var staff = User.CreateInternal("Omar", "Staff", TestApp.NewEmail(), Language.En, app.Clock.Now.UtcDateTime, "+966", mobile).Value;
        await using (var ctx = db.NewContext()) { ctx.AddAccount(staff); await ctx.SaveChangesAsync(); }
        var result = await app.Send(app.RegistrationForm(mobile: mobile));
        Assert.Equal("registration.mobile_taken", result.Error!.Code);
    }

    [Fact]
    public async Task Login_identifier_is_email_not_mobile()
    {
        await using var app = new TestApp(db);
        var (_, _, password) = await app.RegisterAsync();
        var byMobile = await app.Send(new LoginWithPasswordCommand("+966501234567", password));
        Assert.Equal("validation.failed", byMobile.Error!.Code);
    }

    [Fact]
    public async Task Registration_code_is_single_use_and_a_wrong_code_does_not_create_the_account()
    {
        await using var app = new TestApp(db);
        var form = app.RegistrationForm();
        await app.Send(form);
        var good = app.Email.LastOtp!;
        var wrong = ((int.Parse(good) + 1) % 10000).ToString("D4");

        Assert.Equal("otp.invalid_or_expired", (await app.Send(new VerifyRegistrationCommand(form.Email, wrong))).Error!.Code);
        Assert.True((await app.Send(new VerifyRegistrationCommand(form.Email, good))).IsSuccess);
        Assert.Equal("otp.invalid_or_expired", (await app.Send(new VerifyRegistrationCommand(form.Email, good))).Error!.Code);   // replay
    }

    [Fact]
    public async Task Otp_expires_after_180_seconds()
    {
        await using var app = new TestApp(db);
        var form = app.RegistrationForm();
        await app.Send(form);
        app.Clock.Advance(TimeSpan.FromSeconds(181));
        Assert.Equal("otp.invalid_or_expired", (await app.Send(new VerifyRegistrationCommand(form.Email, app.Email.LastOtp!))).Error!.Code);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_verification_for_ten_minutes()
    {
        await using var app = new TestApp(db);
        var form = app.RegistrationForm();
        await app.Send(form);
        var good = app.Email.LastOtp!;
        var wrong = ((int.Parse(good) + 1) % 10000).ToString("D4");

        for (var i = 0; i < 4; i++) Assert.Equal("otp.invalid_or_expired", (await app.Send(new VerifyRegistrationCommand(form.Email, wrong))).Error!.Code);
        Assert.Equal("otp.locked", (await app.Send(new VerifyRegistrationCommand(form.Email, wrong))).Error!.Code);       // 5th failure locks
        Assert.Equal("otp.locked", (await app.Send(new VerifyRegistrationCommand(form.Email, good))).Error!.Code);        // even the right code
        Assert.Equal("otp.locked", (await app.Send(form)).Error!.Code);                                                   // and a new code cannot be requested

        app.Clock.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));
        Assert.True((await app.Send(form)).IsSuccess);                                                                    // the lock is temporary
    }

    [Fact]
    public async Task Resend_is_limited_to_three_and_reuses_the_same_challenge()
    {
        await using var app = new TestApp(db);
        var form = app.RegistrationForm();
        await app.Send(form);
        for (var i = 0; i < 3; i++) Assert.True((await app.Send(new ResendOtpCommand(OtpPurpose.Registration, form.Email))).IsSuccess);
        Assert.Equal("otp.resend_limit_reached", (await app.Send(new ResendOtpCommand(OtpPurpose.Registration, form.Email))).Error!.Code);
        await using var ctx = db.NewContext();
        Assert.Equal(1, await ctx.OtpChallenges.CountAsync(c => c.TargetEmail == form.Email));
        Assert.True((await app.Send(new VerifyRegistrationCommand(form.Email, app.Email.LastOtp!))).IsSuccess);          // the newest code works
    }
}

[Collection("sql")]
public class LoginAndSessionFlowTests(SqlServerFixture db)
{
    [Fact]
    public async Task Login_is_email_and_password_then_an_email_code()
    {
        await using var app = new TestApp(db);
        var (userId, email, password) = await app.RegisterAsync();
        var step1 = await app.Send(new LoginWithPasswordCommand(email.ToUpperInvariant(), password));
        Assert.True(step1.IsSuccess);
        Assert.Equal(OtpPurpose.Login, app.Email.LastOtpPurpose);
        var session = await app.Send(new VerifyLoginOtpCommand(email, app.Email.LastOtp!, ClientType.Web, null));
        Assert.True(session.IsSuccess);
        Assert.Equal(userId, session.Value.UserId);
        Assert.NotEmpty(session.Value.Token);

        await using var ctx = db.NewContext();
        var stored = await ctx.UserSessions.AsNoTracking().SingleAsync(s => s.Id == session.Value.SessionId);
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(session.Value.Token)), stored.RefreshTokenHash);   // only the hash is stored
        Assert.Null(stored.ExpiresAt);
        Assert.Equal(1, await ctx.LoginAudits.CountAsync(a => a.UserId == userId && a.Outcome == LoginOutcome.Success && a.LoginMethod == LoginMethod.PasswordOtp));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_until_the_lock_expires()
    {
        await using var app = new TestApp(db);
        var (_, email, password) = await app.RegisterAsync();
        for (var i = 0; i < 4; i++) Assert.Equal("auth.invalid_credentials", (await app.Send(new LoginWithPasswordCommand(email, "wrong-password-123"))).Error!.Code);
        Assert.Equal("auth.account_locked", (await app.Send(new LoginWithPasswordCommand(email, "wrong-password-123"))).Error!.Code);
        Assert.Equal("auth.account_locked", (await app.Send(new LoginWithPasswordCommand(email, password))).Error!.Code);
        app.Clock.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1)));
        Assert.True((await app.Send(new LoginWithPasswordCommand(email, password))).IsSuccess);
    }

    [Fact]
    public async Task Login_failures_are_audited_without_revealing_whether_the_email_exists()
    {
        await using var app = new TestApp(db);
        var unknown = await app.Send(new LoginWithPasswordCommand(TestApp.NewEmail(), "whatever-password-1"));
        var (_, email, _) = await app.RegisterAsync();
        var wrong = await app.Send(new LoginWithPasswordCommand(email, "whatever-password-1"));
        Assert.Equal(unknown.Error, wrong.Error);                       // same answer for an unknown email and a wrong password
        await using var ctx = db.NewContext();
        Assert.True(await ctx.LoginAudits.AnyAsync(a => a.Outcome == LoginOutcome.InvalidCredentials));
    }

    [Fact]
    public async Task Session_stays_active_until_logout_with_no_inactivity_timeout()
    {
        await using var app = new TestApp(db);
        var (userId, email, password) = await app.RegisterAsync();
        var session = await app.LoginAsync(email, password);

        await using var scope = app.Root.CreateAsyncScope();
        var authenticator = scope.ServiceProvider.GetRequiredService<ISessionAuthenticator>();
        Assert.NotNull(await authenticator.AuthenticateAsync(session.Token, default));

        app.Clock.Advance(TimeSpan.FromDays(400));                      // a year of silence
        var stillValid = await authenticator.AuthenticateAsync(session.Token, default);
        Assert.NotNull(stillValid);
        Assert.Equal(userId, stillValid!.UserId);
        Assert.Null(await authenticator.AuthenticateAsync("not-a-session-token", default));
    }

    [Fact]
    public async Task Logout_revokes_only_the_current_session()
    {
        await using var app = new TestApp(db);
        var (userId, email, password) = await app.RegisterAsync();
        var first = await app.LoginAsync(email, password);
        var second = await app.LoginAsync(email, password);

        app.Current.UserId = userId;
        app.Current.SessionId = first.SessionId;
        Assert.True((await app.Send(new LogoutCommand())).IsSuccess);

        await using var scope = app.Root.CreateAsyncScope();
        var authenticator = scope.ServiceProvider.GetRequiredService<ISessionAuthenticator>();
        Assert.Null(await authenticator.AuthenticateAsync(first.Token, default));
        Assert.NotNull(await authenticator.AuthenticateAsync(second.Token, default));
        await using var ctx = db.NewContext();
        Assert.Equal(SessionRevokedReason.UserLogout, (await ctx.UserSessions.AsNoTracking().SingleAsync(s => s.Id == first.SessionId)).RevokedReason);
    }

    [Fact]
    public async Task Logout_requires_a_session()
    {
        await using var app = new TestApp(db);
        Assert.Equal("auth.unauthenticated", (await app.Send(new LogoutCommand())).Error!.Code);
    }

    [Fact]
    public async Task Deactivating_an_account_ends_its_sessions_but_never_deletes_the_user()
    {
        await using var app = new TestApp(db);
        var (userId, email, password) = await app.RegisterAsync();
        var session = await app.LoginAsync(email, password);
        Assert.True((await app.Send(new DeactivateAccountCommand(userId))).IsSuccess);

        await using var scope = app.Root.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ISessionAuthenticator>().AuthenticateAsync(session.Token, default));
        Assert.Equal("auth.account_inactive", (await app.Send(new LoginWithPasswordCommand(email, password))).Error!.Code);
        await using var ctx = db.NewContext();
        var user = await ctx.Accounts.AsNoTracking().SingleAsync(u => u.Id == userId);       // still there
        Assert.Equal(AccountStatus.Deactivated, user.AccountStatus);
        Assert.True(user.IsActive);                                                       // the other flag is untouched
        Assert.Equal(SessionRevokedReason.SystemRevoked, (await ctx.UserSessions.AsNoTracking().SingleAsync(s => s.Id == session.SessionId)).RevokedReason);
    }

    [Fact]
    public async Task Closing_the_administrative_gate_blocks_sign_in_without_changing_the_lifecycle_status()
    {
        await using var app = new TestApp(db);
        var (userId, email, password) = await app.RegisterAsync();
        Assert.True((await app.Send(new SetUserAccessCommand(userId, false))).IsSuccess);
        Assert.Equal("auth.account_inactive", (await app.Send(new LoginWithPasswordCommand(email, password))).Error!.Code);
        await using var ctx = db.NewContext();
        var user = await ctx.Accounts.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.False(user.IsActive);
        Assert.Equal(AccountStatus.Active, user.AccountStatus);
        Assert.True((await app.Send(new SetUserAccessCommand(userId, true))).IsSuccess);
        Assert.True((await app.Send(new LoginWithPasswordCommand(email, password))).IsSuccess);
    }

    [Fact]
    public async Task Only_a_pending_account_can_be_activated_and_only_within_ten_minutes()
    {
        await using var app = new TestApp(db);
        var (activeId, _, _) = await app.RegisterAsync();
        Assert.Equal("user.not_pending_activation", (await app.Send(new ActivateAccountCommand(activeId))).Error!.Code);

        // a dependent waiting for activation (created here directly; the Family module will drive this flow)
        var now = app.Clock.Now.UtcDateTime;
        var pending = User.CreateExternal(new NewExternalUser("Kid", "One", TestApp.NewEmail(), "+966", TestApp.NewMobile(), new DateOnly(2016, 1, 1), null,
            false, false, QrCode.Generate(), null, now, Language.En), DateOnly.FromDateTime(now), now, TimeSpan.FromMinutes(10)).Value;
        await using (var ctx = db.NewContext()) { ctx.AddAccount(pending); await ctx.SaveChangesAsync(); }

        app.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal("user.activation_expired", (await app.Send(new ActivateAccountCommand(pending.Id))).Error!.Code);
        app.Clock.Advance(TimeSpan.FromMinutes(-5));
        Assert.True((await app.Send(new ActivateAccountCommand(pending.Id))).IsSuccess);
    }
}

[Collection("sql")]
public class PasswordAndSocialFlowTests(SqlServerFixture db)
{
    [Fact]
    public async Task Changing_the_password_checks_the_policy_rejects_the_same_password_and_disables_Quick_Login()
    {
        await using var app = new TestApp(db);
        var (userId, email, password) = await app.RegisterAsync();
        app.Current.UserId = userId;
        var device = $"device-{Guid.NewGuid():N}";
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.True((await app.Send(new RegisterQuickLoginCommand(device, DevicePlatform.Android, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())))).IsSuccess);

        Assert.Equal("password.policy", (await app.Send(new ChangePasswordCommand(password, "short"))).Error!.Code);
        Assert.Equal("password.same_as_current", (await app.Send(new ChangePasswordCommand(password, password))).Error!.Code);
        Assert.Equal("auth.invalid_credentials", (await app.Send(new ChangePasswordCommand("not-the-password", "A-brand-new-password-1"))).Error!.Code);
        Assert.True((await app.Send(new ChangePasswordCommand(password, "A-brand-new-password-1"))).IsSuccess);

        await using var ctx = db.NewContext();
        var registration = await ctx.QuickLoginRegistrations.AsNoTracking().SingleAsync(r => r.DeviceId == device);
        Assert.False(registration.IsEnabled);
        Assert.Equal(QuickLoginDisabledReason.PasswordChanged, registration.DisabledReason);
        await app.LoginAsync(email, "A-brand-new-password-1");      // the new password works
    }

    [Fact]
    public async Task Forgot_password_resets_through_an_email_code_and_disables_Quick_Login()
    {
        await using var app = new TestApp(db);
        var (userId, email, _) = await app.RegisterAsync();
        app.Current.UserId = userId;
        var device = $"device-{Guid.NewGuid():N}";
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await app.Send(new RegisterQuickLoginCommand(device, DevicePlatform.iOS, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));

        Assert.True((await app.Send(new RequestPasswordResetCommand(email))).IsSuccess);
        Assert.Equal(OtpPurpose.PasswordReset, app.Email.LastOtpPurpose);
        var code = app.Email.LastOtp!;
        Assert.Equal("password.policy", (await app.Send(new ResetPasswordCommand(email, code, "short"))).Error!.Code);
        Assert.True((await app.Send(new ResetPasswordCommand(email, code, "My-reset-password-42"))).IsSuccess);
        Assert.Equal("otp.invalid_or_expired", (await app.Send(new ResetPasswordCommand(email, code, "Another-password-4242"))).Error!.Code);   // single use
        await app.LoginAsync(email, "My-reset-password-42");
        await using var ctx = db.NewContext();
        Assert.False((await ctx.QuickLoginRegistrations.AsNoTracking().SingleAsync(r => r.DeviceId == device)).IsEnabled);
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_looks_successful_and_sends_nothing()
    {
        await using var app = new TestApp(db);
        var before = app.Email.OtpCount;
        Assert.True((await app.Send(new RequestPasswordResetCommand(TestApp.NewEmail()))).IsSuccess);
        Assert.Equal(before, app.Email.OtpCount);
    }

    [Fact]
    public async Task Social_registration_creates_no_password_credentials_and_cannot_use_password_flows()
    {
        await using var app = new TestApp(db);
        var email = TestApp.NewEmail();
        var subject = $"google-{Guid.NewGuid():N}";
        var token = app.External.Issue(subject, email);
        var register = await app.Send(new RegisterWithExternalProviderCommand(ExternalProvider.Google, token, "Noor", "Salem", "+966", TestApp.NewMobile(),
            new DateOnly(1992, 2, 2), Gender.Female, false, false, null, "ar", true, ClientType.Mobile, null));
        Assert.True(register.IsSuccess, register.Error?.Message);
        var userId = register.Value.UserId;

        await using (var ctx = db.NewContext())
        {
            Assert.Null((await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).PasswordHash);   // no password at all
            var login = await ctx.UserLogins.AsNoTracking().SingleAsync(l => l.UserId == userId);           // AspNetUserLogins
            Assert.Equal(("Google", subject), (login.LoginProvider, login.ProviderKey));
            Assert.Equal(Language.Ar, (await ctx.UserSettings.AsNoTracking().SingleAsync(s => s.UserId == userId)).Language);
        }

        Assert.Equal("auth.invalid_credentials", (await app.Send(new LoginWithPasswordCommand(email, "any-password-123456"))).Error!.Code);
        var before = app.Email.OtpCount;
        Assert.True((await app.Send(new RequestPasswordResetCommand(email))).IsSuccess);
        Assert.Equal(before, app.Email.OtpCount);                                                        // no reset code for a social-only account
        app.Current.UserId = userId;
        Assert.Equal("password.not_applicable", (await app.Send(new ChangePasswordCommand("a", "b-long-enough-password"))).Error!.Code);

        var again = await app.Send(new LoginWithExternalProviderCommand(ExternalProvider.Google, app.External.Issue(subject, email), ClientType.Web, null));
        Assert.True(again.IsSuccess);                                                                    // Google/Apple sign-in needs no OTP
        Assert.Equal(userId, again.Value.UserId);
    }

    [Fact]
    public async Task Social_registration_rejects_duplicates_unverified_emails_and_existing_accounts()
    {
        await using var app = new TestApp(db);
        var subject = $"apple-{Guid.NewGuid():N}";
        RegisterWithExternalProviderCommand Form(string token, string? mobile = null) => new(ExternalProvider.Apple, token, "A", "B", "+966", mobile ?? TestApp.NewMobile(),
            new DateOnly(1990, 1, 1), null, false, false, null, "en", true, ClientType.Web, null);

        Assert.Equal("auth.external_email_unverified", (await app.Send(Form(app.External.Issue(subject, TestApp.NewEmail(), verified: false)))).Error!.Code);
        Assert.Equal("auth.external_token_invalid", (await app.Send(Form("forged-token"))).Error!.Code);

        var email = TestApp.NewEmail();
        Assert.True((await app.Send(Form(app.External.Issue(subject, email)))).IsSuccess);
        Assert.Equal("registration.external_already_registered", (await app.Send(Form(app.External.Issue(subject, email)))).Error!.Code);

        var (_, passwordEmail, _) = await app.RegisterAsync();       // an email that already has a password account
        Assert.Equal("registration.email_taken", (await app.Send(Form(app.External.Issue($"apple-{Guid.NewGuid():N}", passwordEmail)))).Error!.Code);
    }

    [Fact]
    public async Task Unregistered_social_login_is_refused()
    {
        await using var app = new TestApp(db);
        var result = await app.Send(new LoginWithExternalProviderCommand(ExternalProvider.Google, app.External.Issue("nobody", TestApp.NewEmail()), ClientType.Web, null));
        Assert.Equal("auth.external_login_not_registered", result.Error!.Code);
    }
}

[Collection("sql")]
public class QuickLoginFlowTests(SqlServerFixture db)
{
    private static string NewDevice() => $"device-{Guid.NewGuid():N}";

    private static byte[] Sign(ECDsa key, string challengeBase64, string deviceId) =>
        key.SignData(Convert.FromBase64String(challengeBase64).Concat(Encoding.UTF8.GetBytes(deviceId)).ToArray(), HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

    private async Task<(TestApp App, Guid UserId, string Email, string Password)> NewUser()
    {
        var app = new TestApp(db);
        var (id, email, password) = await app.RegisterAsync();
        return (app, id, email, password);
    }

    [Fact]
    public async Task A_device_proves_possession_of_its_key_by_signing_a_one_time_challenge()
    {
        var (app, userId, _, _) = await NewUser();
        await using var _ = app;
        var device = NewDevice();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        app.Current.UserId = userId;
        var registered = await app.Send(new RegisterQuickLoginCommand(device, DevicePlatform.iOS, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
        Assert.True(registered.IsSuccess, registered.Error?.Message);

        var challenge = (await app.Send(new RequestQuickLoginChallengeCommand(device))).Value;
        var signature = Convert.ToBase64String(Sign(key, challenge.Challenge, device));
        var session = await app.Send(new QuickLoginCommand(device, challenge.Challenge, signature));
        Assert.True(session.IsSuccess, session.Error?.Message);
        Assert.Equal(userId, session.Value.UserId);

        // replaying the same challenge and signature fails: the challenge is single use
        Assert.Equal("quick_login.challenge_invalid", (await app.Send(new QuickLoginCommand(device, challenge.Challenge, signature))).Error!.Code);
    }

    [Fact]
    public async Task A_signature_from_another_key_or_an_expired_challenge_is_refused()
    {
        var (app, userId, _, _) = await NewUser();
        await using var _ = app;
        var device = NewDevice();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        app.Current.UserId = userId;
        await app.Send(new RegisterQuickLoginCommand(device, DevicePlatform.Android, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));

        var challenge = (await app.Send(new RequestQuickLoginChallengeCommand(device))).Value;
        var forged = Convert.ToBase64String(Sign(attacker, challenge.Challenge, device));
        Assert.Equal("quick_login.proof_invalid", (await app.Send(new QuickLoginCommand(device, challenge.Challenge, forged))).Error!.Code);

        var expired = (await app.Send(new RequestQuickLoginChallengeCommand(device))).Value;
        app.Clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal("quick_login.challenge_invalid",
            (await app.Send(new QuickLoginCommand(device, expired.Challenge, Convert.ToBase64String(Sign(key, expired.Challenge, device))))).Error!.Code);
    }

    [Fact]
    public async Task Unsigned_or_unknown_requests_are_never_accepted()
    {
        var (app, _, _, _) = await NewUser();
        await using var _ = app;
        Assert.Equal("quick_login.not_registered", (await app.Send(new RequestQuickLoginChallengeCommand(NewDevice()))).Error!.Code);
        Assert.Equal("quick_login.not_registered", (await app.Send(new QuickLoginCommand(NewDevice(), Convert.ToBase64String(new byte[32]), Convert.ToBase64String(new byte[64])))).Error!.Code);
    }

    [Fact]
    public async Task Only_valid_P256_public_keys_are_accepted_and_no_pin_or_passcode_is_involved()
    {
        var (app, userId, _, _) = await NewUser();
        await using var _ = app;
        app.Current.UserId = userId;
        Assert.Equal("quick_login.public_key_invalid", (await app.Send(new RegisterQuickLoginCommand(NewDevice(), DevicePlatform.iOS, Convert.ToBase64String(RandomNumberGenerator.GetBytes(91))))).Error!.Code);
        using var wrongCurve = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Equal("quick_login.public_key_invalid", (await app.Send(new RegisterQuickLoginCommand(NewDevice(), DevicePlatform.iOS, Convert.ToBase64String(wrongCurve.ExportSubjectPublicKeyInfo())))).Error!.Code);
        Assert.DoesNotContain(typeof(RegisterQuickLoginCommand).GetProperties(),
            p => new[] { "pin", "passcode", "password", "biometric" }.Any(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Enabling_Quick_Login_for_another_user_on_the_same_device_disables_the_previous_registration()
    {
        var (app, userA, _, _) = await NewUser();
        await using var _ = app;
        var (userB, _, _) = await app.RegisterAsync();
        var device = NewDevice();
        using var keyA = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var keyB = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        app.Current.UserId = userA;
        Assert.True((await app.Send(new RegisterQuickLoginCommand(device, DevicePlatform.iOS, Convert.ToBase64String(keyA.ExportSubjectPublicKeyInfo())))).IsSuccess);
        app.Current.UserId = userB;
        Assert.True((await app.Send(new RegisterQuickLoginCommand(device, DevicePlatform.iOS, Convert.ToBase64String(keyB.ExportSubjectPublicKeyInfo())))).IsSuccess);

        await using var ctx = db.NewContext();
        var rows = await ctx.QuickLoginRegistrations.AsNoTracking().Where(r => r.DeviceId == device).ToListAsync();
        Assert.Equal(2, rows.Count);                                                  // history is kept
        Assert.Equal(userB, Assert.Single(rows, r => r.IsEnabled).UserId);            // exactly one enabled user per device
        var previous = Assert.Single(rows, r => !r.IsEnabled);
        Assert.Equal(userA, previous.UserId);
        Assert.Equal(QuickLoginDisabledReason.OtherUserEnabled, previous.DisabledReason);
        Assert.NotNull(previous.DisabledAt);

        // the signed challenge now belongs to user B
        var challenge = (await app.Send(new RequestQuickLoginChallengeCommand(device))).Value;
        var session = await app.Send(new QuickLoginCommand(device, challenge.Challenge, Convert.ToBase64String(Sign(keyB, challenge.Challenge, device))));
        Assert.Equal(userB, session.Value.UserId);
    }

    [Fact]
    public async Task A_different_user_signing_in_on_the_device_turns_the_previous_Quick_Login_off()
    {
        var (app, userA, _, _) = await NewUser();
        await using var _ = app;
        var (_, emailB, passwordB) = await app.RegisterAsync();
        var device = NewDevice();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        app.Current.UserId = userA;
        await app.Send(new RegisterQuickLoginCommand(device, DevicePlatform.Android, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));

        await app.LoginAsync(emailB, passwordB, ClientType.Mobile, device);
        await using var ctx = db.NewContext();
        var registration = await ctx.QuickLoginRegistrations.AsNoTracking().SingleAsync(r => r.DeviceId == device);
        Assert.False(registration.IsEnabled);
        Assert.Equal(QuickLoginDisabledReason.OtherUserLoggedIn, registration.DisabledReason);
    }

    [Fact]
    public async Task A_user_can_disable_Quick_Login_and_it_stops_working()
    {
        var (app, userId, _, _) = await NewUser();
        await using var _ = app;
        var device = NewDevice();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        app.Current.UserId = userId;
        await app.Send(new RegisterQuickLoginCommand(device, DevicePlatform.iOS, Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())));
        Assert.True((await app.Send(new DisableQuickLoginCommand(device))).IsSuccess);
        Assert.Equal("quick_login.not_registered", (await app.Send(new RequestQuickLoginChallengeCommand(device))).Error!.Code);
        Assert.Equal("quick_login.not_registered", (await app.Send(new DisableQuickLoginCommand(device))).Error!.Code);
    }

    [Fact]
    public async Task Registering_Quick_Login_requires_a_signed_in_user()
    {
        var (app, _, _, _) = await NewUser();
        await using var _ = app;
        app.Current.UserId = null;
        Assert.Equal("auth.unauthenticated", (await app.Send(new RegisterQuickLoginCommand(NewDevice(), DevicePlatform.iOS, "AAAA"))).Error!.Code);
    }
}

[Collection("sql")]
public class UserDataAndContractsTests(SqlServerFixture db)
{
    [Fact]
    public async Task Settings_hold_language_and_email_preference_only()
    {
        await using var app = new TestApp(db);
        var (userId, _, _) = await app.RegisterAsync();
        app.Current.UserId = userId;
        Assert.Equal("en", (await app.Ask(new GetMySettingsQuery())).Value.Language);
        var updated = await app.Send(new UpdateMySettingsCommand("ar", false));
        Assert.Equal(("ar", false), (updated.Value.Language, updated.Value.EmailNotificationsEnabled));
        Assert.Equal("validation.failed", (await app.Send(new UpdateMySettingsCommand("fr", true))).Error!.Code);
        Assert.DoesNotContain(typeof(Ilmi.Modules.UsersIdentity.Application.Dtos.UserSettingsDto).GetProperties(), p => p.Name.Contains("Accessib"));
    }

    [Fact]
    public async Task Profile_update_validates_the_avatar_and_is_limited_for_Internal_users()
    {
        await using var app = new TestApp(db);
        var (userId, _, _) = await app.RegisterAsync();
        app.Current.UserId = userId;
        Assert.Equal("profile.unknown_avatar", (await app.Send(new UpdateMyProfileCommand("A", "B", null, 77, false, false))).Error!.Code);
        var profile = await app.Send(new UpdateMyProfileCommand("Lina", "Omar", Gender.Female, null, true, true));
        Assert.Equal(("Lina", "Omar", "Adult"), (profile.Value.FirstName, profile.Value.LastName, profile.Value.UserType));
        Assert.Equal(["ADULT"], profile.Value.Rules.Select(r => r.Code).ToArray());

        var staff = User.CreateInternal("Omar", "Staff", TestApp.NewEmail(), Language.En, app.Clock.Now.UtcDateTime).Value;
        await using (var ctx = db.NewContext()) { ctx.AddAccount(staff); await ctx.SaveChangesAsync(); }
        app.Current.UserId = staff.Id;
        Assert.Equal("user.external_only", (await app.Send(new UpdateMyProfileCommand("Omar", "Staff", Gender.Male, null, false, false))).Error!.Code);
        var renamed = await app.Send(new UpdateMyProfileCommand("Omar", "Renamed", null, null, false, false));
        Assert.Equal(("Internal", null), (renamed.Value.UserKind, renamed.Value.UserType));
        Assert.Equal("user.external_only", (await app.Send(new ReplaceMyAccessibilityNeedsCommand([1]))).Error!.Code);
    }

    [Fact]
    public async Task Declared_needs_can_be_read_and_replaced_through_the_lookup()
    {
        await using var app = new TestApp(db);
        var (userId, _, _) = await app.RegisterAsync();
        app.Current.UserId = userId;
        Assert.Equal(4, (await app.Ask(new GetDisabilityTypesQuery())).Value.Count);
        Assert.Empty((await app.Ask(new GetMyAccessibilityNeedsQuery())).Value);

        var replaced = await app.Send(new ReplaceMyAccessibilityNeedsCommand([4, 2]));
        Assert.Equal(new short[] { 2, 4 }, replaced.Value.Select(d => d.Id).ToArray());
        Assert.Equal(new short[] { 4 }, (await app.Send(new ReplaceMyAccessibilityNeedsCommand([4]))).Value.Select(d => d.Id).ToArray());
        Assert.Equal("registration.unknown_disability_type", (await app.Send(new ReplaceMyAccessibilityNeedsCommand([9]))).Error!.Code);
        Assert.Empty((await app.Send(new ReplaceMyAccessibilityNeedsCommand([]))).Value);
    }

    [Fact]
    public async Task Family_can_make_an_Adult_a_Parent_and_back_while_UserType_stays_Adult()
    {
        await using var app = new TestApp(db);
        var (userId, _, _) = await app.RegisterAsync();
        var linkId = Guid.NewGuid();
        await using var scope = app.Root.CreateAsyncScope();
        var rules = scope.ServiceProvider.GetRequiredService<IUserRuleService>();

        Assert.Equal(RuleTransitionOutcome.Changed, await rules.MarkAsParentAsync(userId, linkId, null));
        Assert.Equal(RuleTransitionOutcome.NoChange, await rules.MarkAsParentAsync(userId, linkId, null));
        app.Current.UserId = userId;
        var profile = (await app.Ask(new GetMyProfileQuery())).Value;
        Assert.Equal("Adult", profile.UserType);                                  // the age classification did not change
        Assert.Equal(["PARENT"], profile.Rules.Select(r => r.Code).ToArray());

        Assert.Equal(RuleTransitionOutcome.Changed, await rules.RevertParentToAdultAsync(userId, linkId, null));
        Assert.Equal(RuleTransitionOutcome.NoChange, await rules.RevertParentToAdultAsync(userId, linkId, null));
        Assert.Equal(["ADULT"], (await app.Ask(new GetMyRulesQuery())).Value.Select(r => r.Code).ToArray());

        await using var ctx = db.NewContext();
        var history = await ctx.UserRuleHistory.AsNoTracking().Where(h => h.UserId == userId).OrderBy(h => h.Id).ToListAsync();
        Assert.Equal([RuleChangeReason.Registered, RuleChangeReason.DependentAdded, RuleChangeReason.LastDependentUnlinked], history.Select(h => h.Reason).ToArray());
        Assert.Equal(linkId, history[1].RelatedFamilyLinkId);
    }

    [Fact]
    public async Task Children_Teens_and_unknown_users_are_not_Parent_candidates()
    {
        await using var app = new TestApp(db);
        var (teenId, _, _) = await app.RegisterAsync(app.RegistrationForm(dob: new DateOnly(2012, 6, 1)));
        await using var scope = app.Root.CreateAsyncScope();
        var rules = scope.ServiceProvider.GetRequiredService<IUserRuleService>();
        Assert.Equal(RuleTransitionOutcome.NotAllowed, await rules.MarkAsParentAsync(teenId, null, null));
        Assert.Equal(RuleTransitionOutcome.UserNotFound, await rules.MarkAsParentAsync(Guid.NewGuid(), null, null));
        Assert.Equal(RuleTransitionOutcome.NotAllowed, await rules.RevertParentToAdultAsync(teenId, null, null));
    }

    [Fact]
    public async Task The_user_directory_exposes_a_summary_to_other_modules()
    {
        await using var app = new TestApp(db);
        var (userId, email, _) = await app.RegisterAsync();
        await using var scope = app.Root.CreateAsyncScope();
        var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var summary = await directory.FindAsync(userId);
        Assert.Equal(("External", "Adult", "Active", true), (summary!.UserKind, summary.UserType, summary.AccountStatus, summary.IsActive));
        Assert.Equal(email, summary.Email);
        Assert.Equal(["ADULT"], summary.RuleCodes.ToArray());
        Assert.True(await directory.ExistsAsync(userId));
        Assert.Null(await directory.FindAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Add_dependent_lockout_lasts_ten_minutes_and_never_triggers_without_a_decided_threshold()
    {
        var parent = Guid.NewGuid();

        await using (var undecided = new TestApp(db))        // threshold not decided: failures are counted but never lock
        {
            await using var scope = undecided.Root.CreateAsyncScope();
            var lockout = scope.ServiceProvider.GetRequiredService<IAddDependentLockout>();
            LockoutStatusDto status = default!;
            for (var i = 0; i < 25; i++) status = await lockout.RegisterFailedAttemptAsync(parent);
            Assert.False(status.IsLocked);
            Assert.False(status.ThresholdConfigured);
            await lockout.ResetAsync(parent);
        }

        await using var app = new TestApp(db, o => o.AddDependentMaxFailedAttempts = 3);
        await using var s = app.Root.CreateAsyncScope();
        var service = s.ServiceProvider.GetRequiredService<IAddDependentLockout>();
        Assert.False((await service.RegisterFailedAttemptAsync(parent)).IsLocked);
        Assert.False((await service.RegisterFailedAttemptAsync(parent)).IsLocked);
        var locked = await service.RegisterFailedAttemptAsync(parent);
        Assert.True(locked.IsLocked);
        Assert.Equal(app.Clock.Now.UtcDateTime.AddMinutes(10), locked.LockedUntil);
        app.Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.True((await service.GetStatusAsync(parent)).IsLocked);
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False((await service.GetStatusAsync(parent)).IsLocked);
    }
}
