using Ilmi.Modules.UsersIdentity.Application;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Identity;
using Ilmi.Modules.UsersIdentity.Infrastructure.ExternalServices;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ilmi.Modules.UsersIdentity.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers ASP.NET Core Identity (users, roles, logins on the <c>users</c> schema), UsersIdentityDbContext, repositories and the
    /// security adapters. The connection string and the crypto keys come from the host's configuration; nothing is hardcoded.
    /// Migrations are never applied automatically.
    /// </summary>
    public static IServiceCollection AddUsersIdentityInfrastructure(this IServiceCollection services, string connectionString,
        IdentityCryptoOptions crypto, IdentitySecurityOptions security)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<UsersIdentityDbContext>((sp, options) => options
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", UsersIdentityDbContext.Schema))
            .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

        // ASP.NET Core Identity without cookies or sign-in UI: the module issues its own sessions (email OTP, Quick Login).
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.User.AllowedUserNameCharacters = "";            // the user name is the email address
                o.Password.RequiredLength = security.MinPasswordLength;    // GSR014: 12 characters; no other composition rule is specified
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 1;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = security.LoginMaxFailures;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(security.LoginLockMinutes);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<UsersIdentityDbContext>();
        services.AddScoped<IUserAccountManager, UserAccountManager>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOtpRepository, OtpRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IQuickLoginRepository, QuickLoginRepository>();
        services.AddScoped<ILoginAuditRepository, LoginAuditRepository>();

        services.AddSingleton(crypto);
        services.AddSingleton<IPasswordGenerator, SecurePasswordGenerator>();
        services.AddSingleton<IOtpCodeService, OtpCodeService>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IPayloadProtector, AesGcmPayloadProtector>();
        services.AddSingleton<IQuickLoginCrypto, EcdsaQuickLoginCrypto>();

        // External integrations that still need configuration. A host (or the Notifications module) overrides these.
        services.TryAddSingleton<IIdentityEmailSender, UnconfiguredEmailSender>();
        services.TryAddSingleton<IExternalIdentityVerifier, UnconfiguredExternalIdentityVerifier>();
        return services;
    }
}
