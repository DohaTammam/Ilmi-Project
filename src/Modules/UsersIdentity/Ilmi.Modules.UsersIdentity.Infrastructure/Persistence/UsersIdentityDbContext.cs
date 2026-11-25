using System.Text.RegularExpressions;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ilmi.Modules.UsersIdentity.Infrastructure.Persistence;

/// <summary>
/// The single DbContext of the Users and Identity module. It is an ASP.NET Core <see cref="IdentityDbContext{TUser,TRole,TKey}"/>:
/// <c>AspNetUsers</c> is the main users table, next to Identity's roles, claims, logins and tokens tables. The Identity user
/// (<see cref="ApplicationUser"/>) and the Domain <see cref="User"/> share the AspNetUsers row. Every table lives in the
/// <c>users</c> schema, including the migrations history table. Identity tables keep Identity's standard names;
/// the module's own tables use snake_case.
/// </summary>
public sealed partial class UsersIdentityDbContext(DbContextOptions<UsersIdentityDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public const string Schema = "users";

    /// <summary>The Domain users (the business side of the AspNetUsers row). Identity's own users are the inherited <c>Users</c> set.</summary>
    public DbSet<User> Accounts => Set<User>();
    public DbSet<ExternalUser> ExternalUsers => Set<ExternalUser>();
    public DbSet<InternalUser> InternalUsers => Set<InternalUser>();
    public DbSet<Avatar> Avatars => Set<Avatar>();
    public DbSet<DisabilityType> DisabilityTypes => Set<DisabilityType>();
    public DbSet<UserAccessibilityNeed> UserAccessibilityNeeds => Set<UserAccessibilityNeed>();
    public DbSet<Rule> Rules => Set<Rule>();
    public DbSet<UserRuleAssignment> UserRuleAssignments => Set<UserRuleAssignment>();
    public DbSet<UserRuleHistory> UserRuleHistory => Set<UserRuleHistory>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();

    // authentication: passwords, lockout and external logins are Identity's (AspNetUsers, AspNetUserLogins); the rest is module-owned
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<VerificationLockout> VerificationLockouts => Set<VerificationLockout>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<QuickLoginRegistration> QuickLoginRegistrations => Set<QuickLoginRegistration>();
    public DbSet<LoginAudit> LoginAudits => Set<LoginAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);          // Identity's tables and relationships
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UsersIdentityDbContext).Assembly);

        // One account per provider: Identity's primary key is (LoginProvider, ProviderKey); a user also cannot link a provider twice.
        modelBuilder.Entity<IdentityUserLogin<Guid>>()
            .HasIndex(l => new { l.UserId, l.LoginProvider }).IsUnique().HasDatabaseName("ux_AspNetUserLogins_user_provider");

        // Project convention: snake_case columns for the module's own tables. Identity's tables (AspNet*) keep Identity's names.
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => e.GetTableName() is { } t && !t.StartsWith("AspNet")))
            foreach (var property in entity.GetProperties())
                if (property.FindAnnotation(RelationalAnnotationNames.ColumnName) is null)
                    property.SetColumnName(ToSnakeCase(property.Name));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All timestamps are UTC datetime2(3).
        configurationBuilder.Properties<DateTime>().HaveColumnType("datetime2(3)").HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveColumnType("datetime2(3)").HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<DateOnly>().HaveColumnType("date");
        configurationBuilder.Properties<DateOnly?>().HaveColumnType("date");
    }

    private static string ToSnakeCase(string name) => WordBoundary().Replace(name, "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();
}
