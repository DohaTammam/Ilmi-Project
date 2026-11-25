using System.Security.Cryptography;
using Ilmi.Modules.UsersIdentity.Application;
using Ilmi.Modules.UsersIdentity.Infrastructure;
using Ilmi.Modules.UsersIdentity.Infrastructure.ExternalServices;
using Ilmi.Modules.UsersIdentity.Presentation;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Users & Identity ---------------------------------------------------------------------------------------------
// Secrets and the connection string come from user-secrets or environment variables, never from committed files:
//   ConnectionStrings:UsersIdentity, UsersIdentity:Crypto:OtpHmacKey, UsersIdentity:Crypto:PayloadEncryptionKey (Base64, 32 bytes)
var connectionString = config.GetConnectionString("UsersIdentity")
    ?? throw new InvalidOperationException("ConnectionStrings:UsersIdentity is not configured. Use user-secrets or an environment variable.");
var crypto = config.GetSection("UsersIdentity:Crypto").Get<IdentityCryptoOptions>() ?? new IdentityCryptoOptions();
if (builder.Environment.IsDevelopment())
{
    // Development convenience only: missing keys become random per-process keys (pending OTPs do not survive a restart).
    crypto.OtpHmacKey ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    crypto.PayloadEncryptionKey ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
var security = config.GetSection("UsersIdentity:Security").Get<IdentitySecurityOptions>() ?? new IdentitySecurityOptions();

builder.Services.AddUsersIdentityApplication(security);
builder.Services.AddUsersIdentityInfrastructure(connectionString, crypto, security);
builder.Services.AddUsersIdentityPresentation();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("session", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "Session token returned by the login endpoints."
    });
});

var app = builder.Build();

app.UseExceptionHandler();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program;
