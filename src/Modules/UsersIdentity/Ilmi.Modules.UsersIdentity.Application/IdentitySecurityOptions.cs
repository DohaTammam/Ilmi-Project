namespace Ilmi.Modules.UsersIdentity.Application;

/// <summary>
/// Security and timing values. Defaults are the SRS / confirmed values. They mirror the future Platform system_settings
/// (configurable without redeployment); until the Platform module exists they are read from configuration.
/// </summary>
public sealed class IdentitySecurityOptions
{
    public int OtpLength { get; set; } = 4;
    public int OtpExpirySeconds { get; set; } = 180;
    /// <summary>Failed verifications that lock a scope.</summary>
    public int OtpMaxAttempts { get; set; } = 5;
    public int OtpLockMinutes { get; set; } = 10;
    public int OtpMaxResends { get; set; } = 3;
    public int LoginMaxFailures { get; set; } = 5;
    public int LoginLockMinutes { get; set; } = 10;
    public int MinPasswordLength { get; set; } = 12;
    public int SelfRegistrationMinimumAge { get; set; } = 13;
    /// <summary>Confirmed: 10 minutes.</summary>
    public int PendingActivationMinutes { get; set; } = 10;
    /// <summary>Confirmed: 10 minutes.</summary>
    public int AddDependentLockMinutes { get; set; } = 10;
    /// <summary>OPEN business decision. Null = not decided: failures are counted but never trigger an automatic lock.</summary>
    public int? AddDependentMaxFailedAttempts { get; set; }
    /// <summary>Lifetime of a Quick Login challenge. Not specified by the requirements; short by design.</summary>
    public int QuickLoginChallengeSeconds { get; set; } = 60;

    public TimeSpan OtpLifetime => TimeSpan.FromSeconds(OtpExpirySeconds);
}
