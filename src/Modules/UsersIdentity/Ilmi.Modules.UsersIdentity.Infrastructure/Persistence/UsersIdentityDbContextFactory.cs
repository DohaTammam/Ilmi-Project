using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence;

/// <summary>
/// Used only by the EF tools (<c>dotnet ef migrations add</c>) to build the model. Creating a migration never connects to a database.
/// Set ILMI_USERSIDENTITY_CONNECTION to point the tools at a real database (for example to script a migration).
/// </summary>
public sealed class UsersIdentityDbContextFactory : IDesignTimeDbContextFactory<UsersIdentityDbContext>
{
    public UsersIdentityDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ILMI_USERSIDENTITY_CONNECTION") ?? "Server=design-time-only;Database=Ilmi";
        var options = new DbContextOptionsBuilder<UsersIdentityDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", UsersIdentityDbContext.Schema))
            .Options;
        return new UsersIdentityDbContext(options);
    }
}
