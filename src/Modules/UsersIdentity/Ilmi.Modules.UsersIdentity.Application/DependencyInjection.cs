using FluentValidation;
using Ilmi.Modules.UsersIdentity.Application.Common;
using Ilmi.Modules.UsersIdentity.Application.Services;
using Ilmi.Modules.UsersIdentity.Contracts.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ilmi.Modules.UsersIdentity.Application;

public static class DependencyInjection
{
    /// <summary>Registers handlers, validators, services and the public Contracts of Users and Identity.</summary>
    public static IServiceCollection AddUsersIdentityApplication(this IServiceCollection services, IdentitySecurityOptions? options = null)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options ?? new IdentitySecurityOptions());
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, ServiceLifetime.Scoped, includeInternalTypes: true);

        // command / query handlers
        foreach (var type in typeof(DependencyInjection).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false }))
            foreach (var i in type.GetInterfaces().Where(i => i.IsGenericType &&
                         (i.GetGenericTypeDefinition() == typeof(SharedKernel.Application.ICommandHandler<,>) ||
                          i.GetGenericTypeDefinition() == typeof(SharedKernel.Application.IQueryHandler<,>))))
                services.AddScoped(i, type);

        services.AddScoped<OtpService>();
        services.AddScoped<SessionService>();
        services.AddScoped<PasswordPolicy>();
        services.AddScoped<QuickLoginDisabler>();
        services.AddScoped<RegistrationChecks>();
        services.AddScoped<ExternalUserProvisioner>();
        services.AddScoped<ISessionAuthenticator, SessionAuthenticator>();

        // public Contracts (for Family, Notifications, Support, Cms)
        services.AddScoped<IUserDirectory, UserDirectoryService>();
        services.AddScoped<IUserRuleService, UserRuleService>();
        services.AddScoped<IAddDependentLockout, AddDependentLockoutService>();
        return services;
    }
}
