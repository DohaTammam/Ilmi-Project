using System.Text.Json.Serialization;
using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Ilmi.Modules.UsersIdentity.Presentation;

public static class DependencyInjection
{
    /// <summary>Controllers, session authentication and error handling of Users and Identity. Add the Application and Infrastructure registrations too.</summary>
    public static IServiceCollection AddUsersIdentityPresentation(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthentication.Scheme, null);
        services.AddAuthorization();
        services.AddProblemDetails();
        services.AddExceptionHandler<IntegrationExceptionHandler>();
        services.AddControllers()
            .AddApplicationPart(typeof(DependencyInjection).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
