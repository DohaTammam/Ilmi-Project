using Ilmi.Modules.UsersIdentity.Contracts.Dtos;

namespace Ilmi.Modules.UsersIdentity.Contracts.Services;

/// <summary>
/// The Parent "Add Dependent" verification lockout (10 minutes, confirmed). The number of failed attempts that
/// triggers it is an OPEN business decision: until it is configured, failures are counted but never lock automatically.
/// </summary>
public interface IAddDependentLockout
{
    Task<LockoutStatusDto> GetStatusAsync(Guid parentUserId, CancellationToken ct = default);
    Task<LockoutStatusDto> RegisterFailedAttemptAsync(Guid parentUserId, CancellationToken ct = default);
    Task ResetAsync(Guid parentUserId, CancellationToken ct = default);
}
