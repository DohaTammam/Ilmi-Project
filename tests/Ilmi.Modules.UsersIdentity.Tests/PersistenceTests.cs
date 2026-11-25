using Ilmi.Modules.UsersIdentity.Application.Abstractions;
using Ilmi.Modules.UsersIdentity.Application.Commands.Users;
using Ilmi.Modules.UsersIdentity.Domain.Entities;
using Ilmi.Modules.UsersIdentity.Domain.Enums;
using Ilmi.Modules.UsersIdentity.Domain.Rules;
using Ilmi.Modules.UsersIdentity.Domain.ValueObjects;
using Ilmi.Modules.UsersIdentity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ilmi.Modules.UsersIdentity.Tests;

/// <summary>Runs against a real SQL Server database created by the real migration.</summary>
[Collection("sql")]
public class PersistenceTests(SqlServerFixture db)
{
    private static readonly DateTime Now = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 25);

    private static User NewUser(DateOnly dob, string? mobile = null, string? email = null, string code = "+966")
    {
        var data = new NewExternalUser("Test", "User", email ?? TestApp.NewEmail(), code, mobile ?? TestApp.NewMobile(), dob, null, false, false,
            QrCode.Generate(), null, Now, Language.En);
        return User.CreateExternal(data, Today, Now).Value;
    }

    private async Task<User> Save(User user)
    {
        await using var ctx = db.NewContext();
        ctx.AddAccount(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    /// <summary>Raw SQL surfaces the SqlException itself (EF wraps only SaveChanges failures).</summary>
    private static async Task<string> SqlError(Func<Task> action) =>
        (await Assert.ThrowsAnyAsync<Exception>(action)).GetBaseException().Message;

    private async Task<List<string>> Sql(string sql)
    {
        await using var ctx = db.NewContext();
        return await ctx.Database.SqlQueryRaw<string>(sql).ToListAsync();
    }

    // ------------------------------------------------------------------ schema and model
    [Fact]
    public async Task Every_table_of_the_module_lives_in_the_users_schema()
    {
        var tables = await Sql("SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'");
        Assert.All(tables, t => Assert.StartsWith("users.", t));
        var expected = new[]
        {
            // ASP.NET Core Identity
            "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims", "AspNetRoleClaims", "AspNetUserLogins", "AspNetUserTokens",
            // the two profile tables with a foreign key to AspNetUsers
            "external_users", "internal_users",
            // the module's own tables
            "avatars", "disability_types", "user_accessibility_needs", "rules", "user_rule_assignments", "user_rule_history", "user_settings",
            "otp_challenges", "verification_lockouts", "user_sessions", "quick_login_registrations", "login_audit"
        }.Select(t => "users." + t).Append("users.__EFMigrationsHistory");
        Assert.Equal(expected.Order(), tables.Order());
    }

    [Fact]
    public async Task Excluded_entities_do_not_exist()
    {
        var tables = await Sql("SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES");
        Assert.DoesNotContain(tables, t => t.Contains("accessibility_modes") || t.Contains("job_executions") || t.Contains("external_user_profiles") || t.Contains("user_credentials") || t.Contains("external_logins"));
        Assert.DoesNotContain(tables, t => t.StartsWith("identity.") || t.StartsWith("settings."));
        var settingsColumns = await Sql("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'user_settings'");
        Assert.DoesNotContain(settingsColumns, c => c.Contains("accessib", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["created_at", "created_by", "email_notifications_enabled", "language", "row_version", "updated_at", "updated_by", "user_id"],
            settingsColumns.Order().ToList());
    }

    [Fact]
    public async Task The_model_has_one_module_context_and_no_accessibility_mode_property()
    {
        await using var ctx = db.NewContext();
        var entities = ctx.Model.GetEntityTypes().ToList();
        Assert.Equal(22, entities.Count);                       // 22 entity types on 21 tables: the Identity user and the Domain user share AspNetUsers
        Assert.All(entities, e => Assert.Equal("users", e.GetSchema()));
        Assert.DoesNotContain(entities.SelectMany(e => e.GetProperties()), p => p.Name == "AccessibilityModeId");
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task The_Identity_users_table_carries_the_shared_account_fields_and_the_profiles_the_person_data()
    {
        string Cols(string table) => table;
        var users = await Sql("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'users' AND TABLE_NAME = 'AspNetUsers'");
        foreach (var c in new[]
                 {
                     // Identity's own columns
                     "Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail", "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
                     "PhoneNumber", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnd", "LockoutEnabled", "AccessFailedCount",
                     // the module's shared account fields
                     "FirstName", "LastName", "UserKind", "AccountStatus", "PendingActivationExpiresAt", "IsActive",
                     "CreatedAt", "CreatedBy", "UpdatedOn", "UpdatedBy", "RowVersion"
                 })
            Assert.Contains(c, users);
        Assert.DoesNotContain("RuleId", users);          // Rules live in user_rule_assignments
        Assert.DoesNotContain("IsParent", users);        // Parent is a Rule, never a flag
        Assert.DoesNotContain("DateOfBirth", users);     // date of birth lives in the profile tables
        foreach (var c in new[] { "MobileCountryCode", "MobileNationalNumber", "UserType", "MobileUniquenessKey" }) Assert.Contains(c, users);   // mobile and UserType: one table, one unique index

        var external = await Sql("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'users' AND TABLE_NAME = 'external_users'");
        Assert.Equal(["avatar_id", "date_of_birth", "gender", "notify_ilmi_special_needs", "qr_code",
                      "receive_newsletter", "row_version", "terms_accepted_at", "user_id", "user_kind"], external.Order().ToList());
        var internalCols = await Sql("SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'users' AND TABLE_NAME = 'internal_users'");
        Assert.Equal(["date_of_birth", "row_version", "user_id", "user_kind"], internalCols.Order().ToList());
        Assert.DoesNotContain(internalCols, c => c.Contains("employee", StringComparison.OrdinalIgnoreCase) || c.Contains("department") || c.Contains("job_title"));
    }

    [Fact]
    public async Task Each_profile_table_has_a_foreign_key_to_AspNetUsers()
    {
        var fks = await Sql(@"SELECT OBJECT_NAME(fk.parent_object_id) + ' -> ' + OBJECT_NAME(fk.referenced_object_id) AS [Value]
                              FROM sys.foreign_keys fk WHERE OBJECT_SCHEMA_NAME(fk.parent_object_id) = 'users'");
        Assert.Contains("external_users -> AspNetUsers", fks);
        Assert.Contains("internal_users -> AspNetUsers", fks);
        Assert.Contains("user_settings -> AspNetUsers", fks);
        Assert.Contains("AspNetUserLogins -> AspNetUsers", fks);
        Assert.Contains("user_accessibility_needs -> AspNetUsers", fks);
    }

    [Fact]
    public async Task Secrets_are_stored_as_hashes_or_ciphertext_never_as_text()
    {
        var types = await Sql(@"SELECT TABLE_NAME + '.' + COLUMN_NAME + ':' + DATA_TYPE + ':' + CAST(ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) AS varchar(12)) AS [Value]
                                FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'users'");
        Assert.Contains("otp_challenges.pending_payload:varbinary:-1", types);      // varbinary(max)
        Assert.Contains("otp_challenges.code_hash:binary:32", types);
        Assert.Contains("user_sessions.refresh_token_hash:binary:32", types);
        Assert.Contains("quick_login_registrations.public_key:varbinary:512", types);
        Assert.DoesNotContain(types, t => t.StartsWith("quick_login_registrations.") &&
            new[] { "pin", "passcode", "biometric", "secret" }.Any(w => t.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    // ------------------------------------------------------------------ lookups and seeds
    [Fact]
    public async Task DisabilityTypes_seed_matches_SRS_DDL02()
    {
        await using var ctx = db.NewContext();
        var rows = await ctx.DisabilityTypes.AsNoTracking().OrderBy(d => d.Id).ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.Equal([(1, "Unable to Walk", "عدم القدرة على المشي"),
                      (2, "Unable to Walk Long Distances", "عدم القدرة على المشي لمسافات طويلة"),
                      (3, "Visual Impairment / Blindness", "ضعف البصر / العمى"),
                      (4, "Hearing Loss / Deafness", "فقد حاسة السمع")],
            rows.Select(r => ((int)r.Id, r.NameEn, r.NameAr)).ToArray());
        Assert.All(rows, r => Assert.True(r.IsActive));
        Assert.Equal(4, rows.Select(r => r.Code).Distinct().Count());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, rows.Select(r => r.SortOrder).ToArray());
    }

    [Fact]
    public async Task Rules_seed_has_the_five_documented_Rules()
    {
        await using var ctx = db.NewContext();
        var rules = await ctx.Rules.AsNoTracking().OrderBy(r => r.SortOrder).ToListAsync();
        Assert.Equal(["CHILD", "TEEN", "ADULT", "PARENT", "REGISTRATION_OFFICER"], rules.Select(r => r.Code).ToArray());
        Assert.Equal(["Child", "Teen", "Adult", "Parent", "Registration Officer"], rules.Select(r => r.NameEn).ToArray());
        Assert.Equal([true, true, true, false, false], rules.Select(r => r.IsAgeBased).ToArray());
        Assert.Equal([false, false, false, false, true], rules.Select(r => r.IsStaff).ToArray());
        Assert.All(rules, r => Assert.True(r.IsActive));
    }

    [Fact]
    public async Task Seeding_is_repeatable_and_creates_no_duplicates()
    {
        await using var ctx = db.NewContext();
        await ctx.Database.MigrateAsync();      // already applied: a no-op
        await ctx.Database.MigrateAsync();
        Assert.Equal(4, await ctx.DisabilityTypes.CountAsync());
        Assert.Equal(5, await ctx.Rules.CountAsync());
    }

    // ------------------------------------------------------------------ keys
    [Fact]
    public async Task UserAccessibilityNeeds_uses_the_composite_key_and_rejects_duplicates_and_unknown_types()
    {
        var user = NewUser(new DateOnly(1990, 1, 1));
        user.ReplaceAccessibilityNeeds([1], Now);
        await Save(user);

        await using var ctx = db.NewContext();
        Assert.Equal(["UserId", "DisabilityTypeId"], ctx.Model.FindEntityType(typeof(UserAccessibilityNeed))!.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray());

        ctx.UserAccessibilityNeeds.Add(new UserAccessibilityNeed(user.Id, 1, Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        ctx.ChangeTracker.Clear();

        ctx.UserAccessibilityNeeds.Add(new UserAccessibilityNeed(user.Id, 99, Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());   // unknown disability type: foreign key
        ctx.ChangeTracker.Clear();

        ctx.UserAccessibilityNeeds.Add(new UserAccessibilityNeed(user.Id, 2, Now));  // a different type is fine
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task One_disability_type_serves_many_users_and_one_user_has_many_needs()
    {
        var a = NewUser(new DateOnly(1990, 1, 1)); a.ReplaceAccessibilityNeeds([3, 4], Now);
        var b = NewUser(new DateOnly(1991, 1, 1)); b.ReplaceAccessibilityNeeds([3], Now);
        await Save(a); await Save(b);
        await using var ctx = db.NewContext();
        Assert.Equal(2, await ctx.UserAccessibilityNeeds.CountAsync(n => n.UserId == a.Id));
        Assert.Equal(2, await ctx.UserAccessibilityNeeds.CountAsync(n => n.DisabilityTypeId == 3 && (n.UserId == a.Id || n.UserId == b.Id)));
    }

    [Fact]
    public async Task UserRuleAssignments_key_prevents_the_same_Rule_twice_without_forcing_a_single_Rule()
    {
        var user = NewUser(new DateOnly(1990, 1, 1));
        await Save(user);
        await using var ctx = db.NewContext();
        var loaded = await ctx.Accounts.Include(u => u.RuleAssignments).SingleAsync(u => u.Id == user.Id);
        Assert.True(loaded.AssignRule(RuleIds.RegistrationOfficer, RuleChangeReason.OfficerAssigned, Now, null, null).IsSuccess);   // a second Rule is storable
        await ctx.SaveChangesAsync();
        Assert.Equal(2, await ctx.UserRuleAssignments.CountAsync(a => a.UserId == user.Id));

        await using var ctx2 = db.NewContext();
        await ctx2.Database.ExecuteSqlRawAsync("DELETE FROM users.user_rule_assignments WHERE 1 = 0");
        var duplicate = await SqlError(() =>
            ctx2.Database.ExecuteSqlInterpolatedAsync($"INSERT users.user_rule_assignments (user_id, rule_id, assigned_at) VALUES ({user.Id}, {RuleIds.Adult}, SYSUTCDATETIME())"));
        Assert.Contains("PRIMARY KEY", duplicate, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ email and mobile uniqueness
    [Fact]
    public async Task Email_is_unique_on_the_normalized_form()
    {
        var email = TestApp.NewEmail();
        await Save(NewUser(new DateOnly(1990, 1, 1), email: email));
        var clash = NewUser(new DateOnly(1991, 1, 1), email: email.ToUpperInvariant());
        await using var ctx = db.NewContext();
        ctx.AddAccount(clash);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        var message = ex.GetBaseException().Message;
        Assert.True(message.Contains("ux_users_normalized_email") || message.Contains("UserNameIndex"), message);   // the user name is the email address too
    }

    [Fact]
    public async Task Mobile_number_is_unique_for_everyone_except_Children()
    {
        var mobile = TestApp.NewMobile();
        var adult = await Save(NewUser(new DateOnly(1985, 1, 1), mobile));
        await Save(NewUser(new DateOnly(2016, 1, 1), mobile));      // a Child may share an adult's number
        await Save(NewUser(new DateOnly(2017, 1, 1), mobile));      // several Children may share one number
        Assert.Equal(UserType.Adult, adult.UserType);

        await using var ctx = db.NewContext();
        ctx.AddAccount(NewUser(new DateOnly(1990, 1, 1), mobile));   // a second Adult may not
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("ux_users_mobile", ex.GetBaseException().Message);
        ctx.ChangeTracker.Clear();

        ctx.AddAccount(NewUser(new DateOnly(2011, 1, 1), mobile));   // nor a Teen
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Mobile_numbers_are_unique_across_Internal_and_External_users()
    {
        var mobile = TestApp.NewMobile();
        // 1. an Internal user with a mobile number: succeeds
        var staff = await Save(User.CreateInternal("Omar", "Staff", TestApp.NewEmail(), Language.En, Now, "+966", mobile).Value);

        // 2. an External (Adult) user with the same number: fails at the database
        await using var ctx = db.NewContext();
        ctx.AddAccount(NewUser(new DateOnly(1990, 1, 1), mobile));
        var external = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("ux_users_mobile", external.GetBaseException().Message);
        ctx.ChangeTracker.Clear();

        // the same number for a Teen is refused as well
        ctx.AddAccount(NewUser(new DateOnly(2011, 1, 1), mobile));
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        ctx.ChangeTracker.Clear();

        // 3. another user with a different number: succeeds
        ctx.AddAccount(NewUser(new DateOnly(1990, 1, 1), TestApp.NewMobile()));
        await ctx.SaveChangesAsync();
        Assert.Equal(mobile, staff.MobileNationalNumber);
    }

    [Fact]
    public async Task An_Internal_user_cannot_take_a_number_that_an_External_user_already_has()
    {
        var mobile = TestApp.NewMobile();
        await Save(NewUser(new DateOnly(1990, 1, 1), mobile));                       // External Adult first
        await using var ctx = db.NewContext();
        ctx.AddAccount(User.CreateInternal("Omar", "Staff", TestApp.NewEmail(), Language.En, Now, "+966", mobile).Value);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("ux_users_mobile", ex.GetBaseException().Message);
    }

    [Fact]
    public async Task Children_may_share_a_number_with_each_other_with_an_Adult_and_with_an_Internal_user_but_Adults_and_Internal_users_never_share()
    {
        var mobile = TestApp.NewMobile();
        await Save(User.CreateInternal("Omar", "Staff", TestApp.NewEmail(), Language.En, Now, "+966", mobile).Value);
        await Save(NewUser(new DateOnly(2016, 1, 1), mobile));    // a Child alongside the Internal user
        await Save(NewUser(new DateOnly(2017, 1, 1), mobile));    // and another Child
        await using var ctx = db.NewContext();
        Assert.Equal(3, await ctx.Accounts.CountAsync(u => u.MobileNationalNumber == mobile));
    }

    [Fact]
    public async Task Internal_users_without_a_mobile_number_do_not_collide()
    {
        await Save(User.CreateInternal("A", "Staff", TestApp.NewEmail(), Language.En, Now).Value);
        await Save(User.CreateInternal("B", "Staff", TestApp.NewEmail(), Language.En, Now).Value);
    }

    [Fact]
    public async Task Same_number_with_a_different_country_code_is_a_different_number()
    {
        var mobile = TestApp.NewMobile();
        await Save(NewUser(new DateOnly(1985, 1, 1), mobile, code: "+966"));
        await Save(NewUser(new DateOnly(1986, 1, 1), mobile, code: "+968"));
    }

    [Fact]
    public async Task UpdatedOn_also_moves_when_only_the_profile_row_changes()
    {
        await using var app = new TestApp(db);
        var (userId, _, _) = await app.RegisterAsync();
        var createdAt = app.Clock.Now.UtcDateTime;
        app.Clock.Advance(TimeSpan.FromHours(2));
        app.Current.UserId = userId;
        // same names, different newsletter preference: only external_users changes
        Assert.True((await app.Send(new UpdateMyProfileCommand("Sara", "Alharbi", Gender.Female, null, false, false))).IsSuccess);
        await using var ctx = db.NewContext();
        var user = await ctx.Accounts.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal(createdAt.AddHours(2), user.UpdatedOn);
    }

    [Fact]
    public async Task A_Child_who_becomes_a_Teen_on_a_shared_number_is_rejected_by_the_database_not_silently_resolved()
    {
        // OPEN business decision: what happens to the Child -> Teen transition when the shared number is already taken.
        // This test documents the current behaviour: the unique index refuses the change.
        var mobile = TestApp.NewMobile();
        await Save(NewUser(new DateOnly(1985, 1, 1), mobile));
        var child = await Save(NewUser(new DateOnly(2016, 1, 1), mobile));
        await using var ctx = db.NewContext();
        var error = await SqlError(() =>
            ctx.Database.ExecuteSqlInterpolatedAsync($"UPDATE users.AspNetUsers SET UserType = 'Teen' WHERE Id = {child.Id}"));
        Assert.Contains("ux_users_mobile", error);
    }

    [Fact]
    public async Task A_profile_can_only_belong_to_a_user_of_the_matching_kind()
    {
        var staff = await Save(User.CreateInternal("Omar", "Staff", TestApp.NewEmail(), Language.En, Now).Value);
        await using var ctx = db.NewContext();

        // an External profile cannot point at an Internal user (composite foreign key on (user_id, user_kind))
        var wrongKind = await SqlError(() => ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT users.external_users (user_id, user_kind, date_of_birth, qr_code, receive_newsletter, notify_ilmi_special_needs, terms_accepted_at)
               VALUES ({staff.Id}, 'External', '1990-01-01', 'ABCDE12345', 0, 0, SYSUTCDATETIME())"));
        Assert.Contains("FOREIGN KEY", wrongKind, StringComparison.OrdinalIgnoreCase);

        // and a profile cannot claim a different kind than its table
        var wrongTable = await SqlError(() => ctx.Database.ExecuteSqlInterpolatedAsync(
            $@"INSERT users.external_users (user_id, user_kind, date_of_birth, qr_code, receive_newsletter, notify_ilmi_special_needs, terms_accepted_at)
               VALUES ({staff.Id}, 'Internal', '1990-01-01', 'ABCDE12346', 0, 0, SYSUTCDATETIME())"));
        Assert.Contains("ck_external_users_user_kind", wrongTable);
    }

    [Fact]
    public async Task Internal_users_persist_with_their_own_profile_and_no_age_classification()
    {
        var user = User.CreateInternal("Omar", "Staff", TestApp.NewEmail(), Language.Ar, Now, "+966", TestApp.NewMobile(), new DateOnly(1988, 3, 4)).Value;
        await Save(user);
        await using var ctx = db.NewContext();
        var loaded = await ctx.Accounts.Include(u => u.InternalUser).Include(u => u.ExternalUser).AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(UserKind.Internal, loaded.UserKind);
        Assert.NotNull(loaded.InternalUser);
        Assert.Null(loaded.ExternalUser);
        Assert.Null(loaded.UserType);
        Assert.Equal(new DateOnly(1988, 3, 4), loaded.InternalUser!.DateOfBirth);

        // an internal mobile number is unique among internal users (and across kinds: see Mobile_numbers_are_unique_across_Internal_and_External_users)
        var clash = User.CreateInternal("Lina", "Staff", TestApp.NewEmail(), Language.En, Now, "+966", user.MobileNationalNumber, null).Value;
        ctx.ChangeTracker.Clear();
        ctx.AddAccount(clash);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        Assert.Contains("ux_users_mobile", ex.GetBaseException().Message);
    }

    [Fact]
    public async Task Identity_stores_passwords_roles_and_logins_without_any_role_seeded()
    {
        await using var app = new TestApp(db);
        var (userId, _, password) = await app.RegisterAsync();
        await using var ctx = db.NewContext();
        var user = await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == userId);     // Identity's user
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.DoesNotContain(password, user.PasswordHash);            // only Identity's salted hash is stored
        Assert.True(user.LockoutEnabled);
        Assert.Equal(0, await ctx.Roles.CountAsync());                 // roles are security roles; none are defined by the requirements
    }

    [Fact]
    public async Task RowVersion_detects_concurrent_changes()
    {
        var user = await Save(NewUser(new DateOnly(1990, 1, 1)));
        await using var first = db.NewContext();
        await using var second = db.NewContext();
        var a = await first.Accounts.SingleAsync(u => u.Id == user.Id);
        var b = await second.Accounts.SingleAsync(u => u.Id == user.Id);
        a.Rename("First", "Writer");
        b.Rename("Second", "Writer");
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task UpdatedOn_is_maintained_automatically_when_a_user_is_modified()
    {
        await using var app = new TestApp(db);
        var (userId, _, _) = await app.RegisterAsync();
        var createdAt = app.Clock.Now.UtcDateTime;

        app.Clock.Advance(TimeSpan.FromHours(1));
        app.Current.UserId = userId;
        var updated = await app.Send(new UpdateMyProfileCommand("Renamed", "Person", Gender.Female, null, true, false));
        Assert.True(updated.IsSuccess, updated.Error?.Message);

        await using var ctx = db.NewContext();
        var user = await ctx.Accounts.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal("Renamed", user.FirstName);
        Assert.Equal(createdAt, user.CreatedAt);                       // creation time never changes
        Assert.Equal(createdAt.AddHours(1), user.UpdatedOn);          // UpdatedOn moved to the time of the change
        Assert.Equal(userId, user.UpdatedBy);

        // a save that changes nothing leaves UpdatedOn alone
        app.Clock.Advance(TimeSpan.FromHours(1));
        await app.Send(new UpdateMyProfileCommand("Renamed", "Person", Gender.Female, null, true, false));
        await using var ctx2 = db.NewContext();
        Assert.Equal(createdAt.AddHours(1), (await ctx2.Accounts.AsNoTracking().SingleAsync(u => u.Id == userId)).UpdatedOn);
    }

    [Fact]
    public async Task Rule_history_and_login_audit_are_append_only()
    {
        await using var app = new TestApp(db);
        var (userId, email, password) = await app.RegisterAsync();
        await app.LoginAsync(email, password);

        await app.WithScope(async sp =>
        {
            var ctx = sp.GetRequiredService<UsersIdentityDbContext>();
            var history = await ctx.UserRuleHistory.FirstAsync(h => h.UserId == userId);
            ctx.UserRuleHistory.Remove(history);
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
            ctx.ChangeTracker.Clear();

            var audit = await ctx.LoginAudits.FirstAsync(a => a.UserId == userId);
            ctx.Entry(audit).Property(a => a.Outcome).CurrentValue = LoginOutcome.AccountLocked;
            await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
            return true;
        });
    }
}
