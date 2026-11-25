using Ilmi.Modules.UsersIdentity.Contracts.Dtos;

namespace Ilmi.Modules.UsersIdentity.Contracts.Services;

/// <summary>Read access to user facts for other modules (Family, Notifications, Support, Cms). Never expose entities.</summary>
public interface IUserDirectory
{
    Task<UserSummaryDto?> FindAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default);
}
