using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Common;
using Ilmi.Modules.UsersIdentity.Application.Dtos;
using Ilmi.SharedKernel.Application;
using Ilmi.SharedKernel.Domain;

namespace Ilmi.Modules.UsersIdentity.Application.Queries.Users;

public sealed record GetMyProfileQuery : IQuery<UserProfileDto>;

public sealed class GetMyProfileHandler(ICurrentUser current, IUserRepository users) : IQueryHandler<GetMyProfileQuery, UserProfileDto>
{
    public async Task<Result<UserProfileDto>> HandleAsync(GetMyProfileQuery q, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        var user = await users.GetByIdAsync(userId, UserIncludes.Rules, ct);
        return user is null ? IdentityErrors.UserNotFound : user.ToProfile(await users.GetRulesAsync(ct));
    }
}

public sealed record GetMySettingsQuery : IQuery<UserSettingsDto>;

public sealed class GetMySettingsHandler(ICurrentUser current, IUserRepository users) : IQueryHandler<GetMySettingsQuery, UserSettingsDto>
{
    public async Task<Result<UserSettingsDto>> HandleAsync(GetMySettingsQuery q, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        var user = await users.GetByIdAsync(userId, UserIncludes.Settings, ct);
        return user?.Settings is null ? IdentityErrors.UserNotFound
            : new UserSettingsDto(user.Settings.Language.ToCode(), user.Settings.EmailNotificationsEnabled);
    }
}

public sealed record GetMyAccessibilityNeedsQuery : IQuery<IReadOnlyList<DisabilityTypeDto>>;

public sealed class GetMyAccessibilityNeedsHandler(ICurrentUser current, IUserRepository users)
    : IQueryHandler<GetMyAccessibilityNeedsQuery, IReadOnlyList<DisabilityTypeDto>>
{
    public async Task<Result<IReadOnlyList<DisabilityTypeDto>>> HandleAsync(GetMyAccessibilityNeedsQuery q, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        var user = await users.GetByIdAsync(userId, UserIncludes.AccessibilityNeeds, ct);
        if (user is null) return IdentityErrors.UserNotFound;
        var ids = user.AccessibilityNeeds.Select(n => n.DisabilityTypeId).ToHashSet();
        return Result.Success<IReadOnlyList<DisabilityTypeDto>>((await users.GetDisabilityTypesAsync(ct)).Where(t => ids.Contains(t.Id)).OrderBy(t => t.SortOrder).Select(t => t.ToDto()).ToList());
    }
}

public sealed record GetMyRulesQuery : IQuery<IReadOnlyList<RuleDto>>;

public sealed class GetMyRulesHandler(ICurrentUser current, IUserRepository users) : IQueryHandler<GetMyRulesQuery, IReadOnlyList<RuleDto>>
{
    public async Task<Result<IReadOnlyList<RuleDto>>> HandleAsync(GetMyRulesQuery q, CancellationToken ct)
    {
        if (current.UserId is not { } userId) return IdentityErrors.Unauthenticated;
        var user = await users.GetByIdAsync(userId, UserIncludes.Rules, ct);
        if (user is null) return IdentityErrors.UserNotFound;
        return Result.Success(user.ToProfile(await users.GetRulesAsync(ct)).Rules);
    }
}

/// <summary>The lookup shown on the registration form (SRS DDL02). Public.</summary>
public sealed record GetDisabilityTypesQuery : IQuery<IReadOnlyList<DisabilityTypeDto>>;

public sealed class GetDisabilityTypesHandler(IUserRepository users) : IQueryHandler<GetDisabilityTypesQuery, IReadOnlyList<DisabilityTypeDto>>
{
    public async Task<Result<IReadOnlyList<DisabilityTypeDto>>> HandleAsync(GetDisabilityTypesQuery q, CancellationToken ct) =>
        Result.Success<IReadOnlyList<DisabilityTypeDto>>((await users.GetDisabilityTypesAsync(ct)).Where(t => t.IsActive).OrderBy(t => t.SortOrder).Select(t => t.ToDto()).ToList());
}
