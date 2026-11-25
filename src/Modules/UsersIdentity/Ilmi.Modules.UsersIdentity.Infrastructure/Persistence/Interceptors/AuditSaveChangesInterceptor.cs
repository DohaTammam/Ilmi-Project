using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Identity;
using Ilmi.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence;

/// <summary>
/// Maintains the audit columns on every save and guards append-only tables:
/// <list type="bullet">
/// <item>Added: <c>CreatedAt/CreatedBy</c> and <c>UpdatedOn</c>; Modified: <c>UpdatedOn/UpdatedBy</c> (UpdatedOn is the users.updated_at column).</item>
/// <item>Append-only entities (Rule history, login audit) can only be inserted: updates and deletes are rejected.</item>
/// </list>
/// A bulk ExecuteUpdate bypasses this interceptor and must set the audit columns itself.
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow().UtcDateTime;
        var actor = currentUser.UserId;

        // Changing a profile row, or Identity's side of the row (password, lockout, logins), counts as modifying the user: touch the owning AspNetUsers row first.
        foreach (var child in context.ChangeTracker.Entries().Where(e => e.State == EntityState.Modified && e.Entity is ExternalUser or InternalUser or ApplicationUser).ToList())
        {
            var ownerId = child.Entity switch { ExternalUser ext => ext.UserId, InternalUser inner => inner.UserId, _ => ((ApplicationUser)child.Entity).Id };
            var owner = context.ChangeTracker.Entries<User>().FirstOrDefault(u => u.Entity.Id == ownerId);
            if (owner is { State: EntityState.Unchanged }) owner.Property(u => u.UpdatedOn).IsModified = true;
        }

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.Entity is IAppendOnly && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} is append-only and cannot be modified or deleted.");

            if (entry.Entity is not IAuditable auditable) continue;
            switch (entry.State)
            {
                case EntityState.Added:
                    if (auditable.CreatedAt == default) auditable.CreatedAt = now;
                    auditable.CreatedBy ??= actor;
                    if (auditable.UpdatedOn == default) auditable.UpdatedOn = auditable.CreatedAt;
                    break;
                case EntityState.Modified:
                    auditable.UpdatedOn = now;
                    auditable.UpdatedBy = actor;
                    // CreatedAt/CreatedBy never change after insert.
                    entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditable.CreatedBy)).IsModified = false;
                    break;
            }
        }
    }
}
