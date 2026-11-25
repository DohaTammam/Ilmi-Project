using System.Security.Cryptography;
using Ilmi.Modules.UsersIdentity.Application;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Commands.Identity;
using Ilmi.Modules.UsersIdentity.Application.Dtos;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Infrastructure;
using Ilmi.Modules.UsersIdentity.Infrastructure.ExternalServices;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Identity;
using Ilmi.SharedKernel.Application;
using Ilmi.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ilmi.Modules.UsersIdentity.Tests;

public sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Captures what would be emailed, in memory only (the real adapters never log codes or passwords).</summary>
public sealed class RecordingEmailSender : IIdentityEmailSender
{
    public string? LastOtp { get; private set; }
    public OtpPurpose? LastOtpPurpose { get; private set; }
    public string? LastPassword { get; private set; }
    public int OtpCount { get; private set; }

    public Task SendOtpAsync(string email, OtpPurpose purpose, string code, TimeSpan lifetime, CancellationToken ct)
    { LastOtp = code; LastOtpPurpose = purpose; OtpCount++; return Task.CompletedTask; }

    public Task SendGeneratedPasswordAsync(string email, string password, CancellationToken ct)
    { LastPassword = password; return Task.CompletedTask; }
}

public sealed class FakeExternalVerifier : IExternalIdentityVerifier
{
    private readonly Dictionary<string, ExternalIdentity> _tokens = [];
    public string Issue(string subject, string email, bool verified = true)
    {
        var token = $"token-{Guid.NewGuid():N}";
        _tokens[token] = new ExternalIdentity(subject, email, verified);
        return token;
    }
    public Task<ExternalIdentity?> VerifyAsync(ExternalProvider provider, string idToken, CancellationToken ct) =>
        Task.FromResult(_tokens.GetValueOrDefault(idToken));
}

public sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }
    public Guid? SessionId { get; set; }
}

/// <summary>One real SQL Server (LocalDB) database for all integration tests. The REAL migration is applied; nothing is mocked.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    public string ConnectionString { get; } =
        (Environment.GetEnvironmentVariable("ILMI_TEST_SQLSERVER") ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true")
        + $";Database=IlmiTest_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    public UsersIdentityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<UsersIdentityDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", UsersIdentityDbContext.Schema)).Options);
}

public static class ContextExtensions
{
    /// <summary>Adds a Domain user together with its Identity user (they share the AspNetUsers row), exactly as the module stores it.</summary>
    public static void AddAccount(this UsersIdentityDbContext ctx, User user) => ctx.Users.Add(ApplicationUser.From(user));
}

[CollectionDefinition("sql")]
public sealed class SqlCollection : ICollectionFixture<SqlServerFixture>;

/// <summary>The module wired exactly like the host, with fakes only for email, social tokens and the current user.</summary>
public sealed class TestApp : IAsyncDisposable
{
    public FakeClock Clock { get; } = new();
    public RecordingEmailSender Email { get; } = new();
    public FakeExternalVerifier External { get; } = new();
    public TestCurrentUser Current { get; } = new();
    public ServiceProvider Root { get; }
    public IdentitySecurityOptions Options { get; }

    public TestApp(SqlServerFixture db, Action<IdentitySecurityOptions>? tweak = null)
    {
        Options = new IdentitySecurityOptions();
        tweak?.Invoke(Options);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<ICurrentUser>(Current);
        services.AddSingleton<IIdentityEmailSender>(Email);
        services.AddSingleton<IExternalIdentityVerifier>(External);
        services.AddUsersIdentityApplication(Options);
        services.AddUsersIdentityInfrastructure(db.ConnectionString, new IdentityCryptoOptions
        {
            OtpHmacKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            PayloadEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }, Options);
        Root = services.BuildServiceProvider();
    }

    /// <summary>Runs one command the way a request would: in its own scope.</summary>
    public async Task<Result<TResponse>> Send<TResponse>(ICommand<TResponse> command)
    {
        await using var scope = Root.CreateAsyncScope();
        dynamic handler = scope.ServiceProvider.GetRequiredService(typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResponse)));
        return await handler.HandleAsync((dynamic)command, CancellationToken.None);
    }

    public async Task<Result<TResponse>> Ask<TResponse>(IQuery<TResponse> query)
    {
        await using var scope = Root.CreateAsyncScope();
        dynamic handler = scope.ServiceProvider.GetRequiredService(typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResponse)));
        return await handler.HandleAsync((dynamic)query, CancellationToken.None);
    }

    public async Task<T> WithScope<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Root.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    // ---------------------------------------------------------------- scenario helpers
    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";
    private static string _mobileSeed = "5" + Random.Shared.Next(10_000_000, 99_999_999);
    public static string NewMobile() => (_mobileSeed = (long.Parse(_mobileSeed) + 1).ToString());

    public RequestRegistrationCommand RegistrationForm(string? email = null, DateOnly? dob = null, string? mobile = null, List<short>? needs = null) =>
        new("Sara", "Alharbi", email ?? NewEmail(), "+966", mobile ?? NewMobile(), dob ?? new DateOnly(1990, 5, 20), Gender.Female,
            true, false, needs, "en", true);

    /// <summary>Registers through the real flow (form, emailed code, verification) and returns the new user's id and generated password.</summary>
    public async Task<(Guid UserId, string Email, string Password)> RegisterAsync(RequestRegistrationCommand? form = null)
    {
        form ??= RegistrationForm();
        var requested = await Send(form);
        Assert.True(requested.IsSuccess, requested.Error?.Message);
        var verified = await Send(new VerifyRegistrationCommand(form.Email, Email.LastOtp!));
        Assert.True(verified.IsSuccess, verified.Error?.Message);
        return (verified.Value.UserId, form.Email, Email.LastPassword!);
    }

    /// <summary>Password login followed by the emailed code. Returns the session.</summary>
    public async Task<SessionDto> LoginAsync(string email, string password, ClientType client = ClientType.Web, string? deviceId = null)
    {
        var step1 = await Send(new LoginWithPasswordCommand(email, password));
        Assert.True(step1.IsSuccess, step1.Error?.Message);
        var step2 = await Send(new VerifyLoginOtpCommand(email, Email.LastOtp!, client, deviceId));
        Assert.True(step2.IsSuccess, step2.Error?.Message);
        return step2.Value;
    }

    public async ValueTask DisposeAsync() => await Root.DisposeAsync();
}
