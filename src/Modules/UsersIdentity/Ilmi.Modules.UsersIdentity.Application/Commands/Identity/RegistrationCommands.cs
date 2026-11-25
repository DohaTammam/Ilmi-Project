using FluentValidation;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Common;
using Ilmi.Modules.UsersIdentity.Application.Dtos;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.ValueObjects;
using Ilmi.SharedKernel.Application;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Commands.Identity;

// ============================================================ registration form -> email OTP (UC002)
public sealed record RequestRegistrationCommand(
    string FirstName, string LastName, string Email, string MobileCountryCode, string MobileNationalNumber,
    DateOnly DateOfBirth, Gender? Gender, bool ReceiveNewsletter, bool NotifyIlmiSpecialNeeds,
    List<short>? DisabilityTypeIds, string Language, bool AcceptedTerms) : ICommand<OtpRequestedDto>;

public sealed class RequestRegistrationValidator : AbstractValidator<RequestRegistrationCommand>
{
    public RequestRegistrationValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.Email).Must(EmailAddress.IsValid).WithMessage("A valid email address is required.");
        RuleFor(x => x.MobileCountryCode).Matches(@"^\+\d{1,4}$").WithMessage("The mobile country code must look like +966.");
        RuleFor(x => x.MobileNationalNumber).Matches(@"^\d{4,15}$").WithMessage("The mobile number must be 4 to 15 digits.");
        RuleFor(x => x.DateOfBirth).NotEqual(default(DateOnly)).WithMessage("The date of birth is required.");
        RuleFor(x => x.Gender).IsInEnum().When(x => x.Gender.HasValue);
        RuleFor(x => x.Language).Must(l => Mapping.TryParseLanguage(l, out _)).WithMessage("The language must be 'ar' or 'en'.");
        RuleFor(x => x.AcceptedTerms).Equal(true).WithMessage("The terms and conditions must be accepted.");
    }
}

public sealed class RequestRegistrationHandler(
    IValidator<RequestRegistrationCommand> validator, RegistrationChecks checks, OtpService otp,
    IPayloadProtector protector, TimeProvider clock) : ICommandHandler<RequestRegistrationCommand, OtpRequestedDto>
{
    public async Task<Result<OtpRequestedDto>> HandleAsync(RequestRegistrationCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        Mapping.TryParseLanguage(c.Language, out var language);
        var data = new RegistrationData(c.FirstName.Trim(), c.LastName.Trim(), c.Email.Trim(), c.MobileCountryCode, c.MobileNationalNumber,
            c.DateOfBirth, c.Gender, c.ReceiveNewsletter, c.NotifyIlmiSpecialNeeds, (c.DisabilityTypeIds ?? []).Distinct().ToList(),
            language, clock.GetUtcNow().UtcDateTime);
        if (await checks.CheckAsync(data, ct) is { } rejected) return rejected;

        var encrypted = protector.Protect(RegistrationPayload.Serialize(data));
        var issued = await otp.IssueAsync(OtpPurpose.Registration, data.Email, null, null, encrypted, ct);
        return issued.IsFailure ? issued.Error! : new OtpRequestedDto(issued.Value.ExpiresAt, issued.Value.ResendsRemaining);
    }
}

// ============================================================ verify email -> create account, generated password by email
public sealed record VerifyRegistrationCommand(string Email, string Code) : ICommand<RegisteredDto>;

public sealed class VerifyRegistrationValidator : AbstractValidator<VerifyRegistrationCommand>
{
    public VerifyRegistrationValidator()
    {
        RuleFor(x => x.Email).Must(EmailAddress.IsValid);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
    }
}

public sealed class VerifyRegistrationHandler(
    IValidator<VerifyRegistrationCommand> validator, OtpService otp, IPayloadProtector protector, RegistrationChecks checks,
    ExternalUserProvisioner provisioner, IUserAccountManager accounts, IPasswordGenerator generator,
    IIdentityEmailSender email) : ICommandHandler<VerifyRegistrationCommand, RegisteredDto>
{
    public async Task<Result<RegisteredDto>> HandleAsync(VerifyRegistrationCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var verified = await otp.VerifyAsync(OtpPurpose.Registration, c.Email, c.Code, ct);
        if (verified.IsFailure) return verified.Error!;
        if (verified.Value.EncryptedPayload is null) return IdentityErrors.OtpInvalid;

        RegistrationData? data;
        try { data = RegistrationPayload.Deserialize(protector.Unprotect(verified.Value.EncryptedPayload)); }
        catch (Exception) { return IdentityErrors.OtpInvalid; }
        if (data is null) return IdentityErrors.OtpInvalid;

        // The checks ran when the form was submitted; run them again, someone may have registered since.
        if (await checks.CheckAsync(data, ct) is { } rejected) return rejected;
        var created = await provisioner.CreateAsync(data, ct);
        if (created.IsFailure) return created.Error!;

        // The password is generated and emailed (SRS). Identity stores only its salted hash; the plain text lives in memory only.
        var password = generator.Generate();
        var saved = await accounts.CreateAsync(created.Value, password, ct);
        if (saved.IsFailure) return saved.Error!;

        var emailed = true;
        try { await email.SendGeneratedPasswordAsync(created.Value.Email!, password, ct); }
        catch (ExternalIntegrationException) { emailed = false; }
        return new RegisteredDto(created.Value.Id, created.Value.Email!, emailed);
    }
}

// ============================================================ Google / Apple registration (UC002 alternative flows): no password, no OTP
public sealed record RegisterWithExternalProviderCommand(
    ExternalProvider Provider, string IdToken, string FirstName, string LastName, string MobileCountryCode, string MobileNationalNumber,
    DateOnly DateOfBirth, Gender? Gender, bool ReceiveNewsletter, bool NotifyIlmiSpecialNeeds, List<short>? DisabilityTypeIds,
    string Language, bool AcceptedTerms, ClientType ClientType, string? DeviceId) : ICommand<SessionDto>;

public sealed class RegisterWithExternalProviderValidator : AbstractValidator<RegisterWithExternalProviderCommand>
{
    public RegisterWithExternalProviderValidator()
    {
        RuleFor(x => x.Provider).IsInEnum();
        RuleFor(x => x.IdToken).NotEmpty();
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.MobileCountryCode).Matches(@"^\+\d{1,4}$");
        RuleFor(x => x.MobileNationalNumber).Matches(@"^\d{4,15}$");
        RuleFor(x => x.DateOfBirth).NotEqual(default(DateOnly));
        RuleFor(x => x.Language).Must(l => Mapping.TryParseLanguage(l, out _));
        RuleFor(x => x.AcceptedTerms).Equal(true).WithMessage("The terms and conditions must be accepted.");
        RuleFor(x => x.ClientType).IsInEnum();
        RuleFor(x => x.DeviceId).MaximumLength(100);
    }
}

public sealed class RegisterWithExternalProviderHandler(
    IValidator<RegisterWithExternalProviderCommand> validator, IExternalIdentityVerifier verifier, IUserAccountManager accounts,
    RegistrationChecks checks, ExternalUserProvisioner provisioner, SessionService sessions, QuickLoginDisabler quickLogin,
    ILoginAuditRepository audit, IUnitOfWork uow, TimeProvider clock) : ICommandHandler<RegisterWithExternalProviderCommand, SessionDto>
{
    private sealed class Rollback(Error error) : Exception { public Error Error { get; } = error; }

    public async Task<Result<SessionDto>> HandleAsync(RegisterWithExternalProviderCommand c, CancellationToken ct)
    {
        if (await validator.CheckAsync(c, ct) is { } invalid) return invalid;
        var identity = await verifier.VerifyAsync(c.Provider, c.IdToken, ct);
        if (identity is null) return IdentityErrors.ExternalTokenInvalid;
        if (!identity.EmailVerified) return IdentityErrors.ExternalEmailUnverified;
        if (await accounts.FindByExternalLoginAsync(c.Provider, identity.Subject, ct) is not null) return IdentityErrors.ExternalAlreadyRegistered;

        Mapping.TryParseLanguage(c.Language, out var language);
        var now = clock.GetUtcNow().UtcDateTime;
        var data = new RegistrationData(c.FirstName.Trim(), c.LastName.Trim(), identity.Email.Trim(), c.MobileCountryCode, c.MobileNationalNumber,
            c.DateOfBirth, c.Gender, c.ReceiveNewsletter, c.NotifyIlmiSpecialNeeds, (c.DisabilityTypeIds ?? []).Distinct().ToList(), language, now);
        // An email that already belongs to another account is rejected: linking a social login to an existing account is an open question.
        if (await checks.CheckAsync(data, ct) is { } rejected) return rejected;

        var created = await provisioner.CreateAsync(data, ct);
        if (created.IsFailure) return created.Error!;
        var user = created.Value;

        IssuedSession? session = null;
        try
        {
            // one transaction: the account, its Google/Apple login (AspNetUserLogins, no password hash), the session and the audit row
            await uow.ExecuteInTransactionAsync(async token =>
            {
                var made = await accounts.CreateAsync(user, null, token);
                if (made.IsFailure) throw new Rollback(made.Error!);
                var linked = await accounts.AddExternalLoginAsync(user, c.Provider, identity.Subject, token);
                if (linked.IsFailure) throw new Rollback(linked.Error!);
                session = sessions.Create(user.Id, c.ClientType, c.DeviceId);
                audit.Add(LoginAudit.Record(user.Id, c.Provider == ExternalProvider.Google ? LoginMethod.Google : LoginMethod.Apple, LoginOutcome.Success, c.DeviceId, now));
                if (c.DeviceId is not null) await quickLogin.DisableOtherUsersOnDeviceAsync(c.DeviceId, user.Id, token);
                await uow.SaveChangesAsync(token);
            }, ct);
        }
        catch (Rollback r) { return r.Error; }
        return new SessionDto(session!.SessionId, session.UserId, session.Token, session.IssuedAt, session.ExpiresAt);
    }
}
