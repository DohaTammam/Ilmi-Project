# Users & Identity module

Merged from the former *Users* and *Identity* modules. Owns every person-related fact and the single, centralised
authentication infrastructure for **both user kinds** (`External` and `Internal`). Schema `users`, one `UsersIdentityDbContext`.

**Status: implemented.** SRS scope: UC002-UC008 (registration, login, OTP, passwords, Google/Apple, Quick Login, profile).
Design: [database-design.md](../../../docs/database/database-design.md) · ERD: [erd.md](../../../docs/database/erd.md).

Three concepts that are never merged: **UserKind** (External/Internal), **UserType** (Child/Teen/Adult, External only, age based)
and **Rule** (CHILD/TEEN/ADULT/PARENT/REGISTRATION_OFFICER, business category). Registration Officer is a Rule only: its permissions are undefined.
Internal security roles and permissions are separate and not designed yet.

## Tables (21, schema `users`), built on ASP.NET Core Identity

**`AspNetUsers` is ASP.NET Core Identity's users table and the main users table.** Two profile tables carry the profile attributes, each with a foreign key to it.

| Area | Tables |
|---|---|
| Main users table | `AspNetUsers`: Identity's email, password hash, lockout and security stamp, plus `FirstName`, `LastName`, `UserKind`, **mobile number**, **`UserType`**, `AccountStatus`, `PendingActivationExpiresAt`, `IsActive`, audit columns and `UpdatedOn` |
| Profiles (composite FK to `AspNetUsers`) | `external_users` (date of birth, gender, QR code, avatar, consents) · `internal_users` (optional date of birth; no employee attributes: none are defined) |
| Identity's standard tables | `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins` (Google / Apple), `AspNetUserTokens` |
| Reference data | `avatars`, `disability_types` (seeded: SRS DDL02), `rules` (seeded: 5 Rules) |
| Declared needs and preferences | `user_accessibility_needs` (composite PK junction), `user_settings` |
| Rules | `user_rule_assignments`, `user_rule_history` (append-only) |
| Module authentication | `otp_challenges`, `verification_lockouts`, `user_sessions`, `quick_login_registrations`, `login_audit` (append-only) |

**Mobile numbers are unique across Internal and External users** (Children may share a number), enforced by one unique index on `AspNetUsers`
(`ux_users_mobile`, over a persisted computed key). Email is the login identifier. Passwords (Identity hash), lockout (5 failures, 10 minutes) and external logins are handled by Identity;
social-only users have a null `PasswordHash`. Identity roles are security roles, separate from the business Rules.

## Identity is isolated from the Domain

The Domain `User` is a plain class. `ApplicationUser : IdentityUser<Guid>`, `ApplicationRole`, `UserManager` and the Identity DbContext are in **Infrastructure**; both user classes map the same `AspNetUsers` row (EF Core table splitting).
Use cases depend only on the `IUserAccountManager` port, implemented in Infrastructure by `UserAccountManager`. Architecture tests enforce this.

## Layers

| Project | Contents |
|---|---|
| `Contracts` | `IUserDirectory`, `IUserRuleService` (Family: Adult ↔ Parent), `IAddDependentLockout`, DTOs. The only surface other modules may use |
| `Domain` | `User` (plain class, no Identity) with `ExternalUser` / `InternalUser` profiles, settings, needs, Rule assignments/history, OTP, sessions, Quick Login, audit; `AgeBands`, `EmailAddress`, `QrCode`; enums |
| `Application` | commands/queries/handlers (`Commands|Queries/Users|Identity`), FluentValidation validators, `OtpService`, `SessionService`, ports for the Identity account manager, email, social tokens and Quick Login crypto |
| `Infrastructure` | `UsersIdentityDbContext` (an `IdentityDbContext`), `ApplicationUser` / `ApplicationRole` (Identity), entity configurations, `UserAccountManager` (implements `IUserAccountManager` over `UserManager`), `AuditSaveChangesInterceptor` (UpdatedOn, append-only guard), repositories, security adapters, migration `InitialUsersIdentity` |
| `Presentation` | `AuthController`, `QuickLoginController`, `MeController`, `LookupsController`, session authentication handler, problem-details mapping |

## HTTP API (see Swagger in Development)

| Area | Endpoints |
|---|---|
| Registration | `POST /api/auth/register/request` → `register/verify` (email OTP, generated password emailed) · `register/external` (Google/Apple, no password) |
| Login | `POST /api/auth/login` (email + password) → `login/verify-otp` (session) · `login/external` · `otp/resend` |
| Passwords | `POST /api/auth/password/forgot`, `password/reset`, `password/change` (disables Quick Login) |
| Session | `POST /api/auth/logout` (revokes the session; a session otherwise lasts until logout) |
| Quick Login | `POST /api/auth/quick-login/register`, `disable`, `challenge`, `login` (device key + signed challenge; the Device PIN never reaches the backend) |
| Me | `GET/PUT /api/me`, `/api/me/settings`, `/api/me/accessibility-needs`, `GET /api/me/rules` |
| Lookup | `GET /api/lookups/disability-types` (public) |

Account deactivation/activation and the administrative access gate exist as Application commands but are not exposed over HTTP until the internal authorization model is defined.

## Configuration

| Key | Purpose |
|---|---|
| `ConnectionStrings:UsersIdentity` | SQL Server connection. Development uses LocalDB `(localdb)\MSSQLLocalDB`, database `Ilmi`, integrated Windows authentication (no credentials) from `src/Host/Ilmi.Api/appsettings.Development.json`. Other environments use user-secrets or an environment variable |
| `UsersIdentity:Crypto:OtpHmacKey` | Base64, ≥ 32 bytes: HMAC key for OTP codes (never committed) |
| `UsersIdentity:Crypto:PayloadEncryptionKey` | Base64, 32 bytes: AES-GCM key for pending registration payloads (never committed) |
| `UsersIdentity:Security:*` | non-secret limits (defaults are the SRS / confirmed values; see `appsettings.json`) |

In Development, missing crypto keys are replaced by random per-process keys. Migrations are never applied automatically. To apply them:

```powershell
$env:ILMI_USERSIDENTITY_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=Ilmi;Integrated Security=true;TrustServerCertificate=true"
dotnet ef database update --project src/Modules/UsersIdentity/Ilmi.Modules.UsersIdentity.Infrastructure --startup-project src/Modules/UsersIdentity/Ilmi.Modules.UsersIdentity.Infrastructure --context UsersIdentityDbContext
```

## Integrations that still need configuration

| Port | Today | Needed |
|---|---|---|
| `IIdentityEmailSender` | refuses to send (HTTP 503), never logs codes or passwords | the Notifications module (delivery outbox) |
| `IExternalIdentityVerifier` | refuses every Google/Apple token (HTTP 503) | provider client ids and signing-key validation |
| Mobile length per country | basic format only (`+` code, 4-15 digits) | Platform country catalogue (63 countries) |
| Limits from `system_settings` | read from configuration | Platform module |

## Open decisions that affect this module

Single or several Rules per user (Q17) · Registration Officer permissions/assignment (Q18) · Pending Activation expiry consequences (Q19) ·
Child → Teen mobile conflict (Q20: the unique index refuses the change) · Parent Add Dependent failed-attempt threshold (Q21; the 10-minute lock is implemented) · Internal users' Rule and attributes (Q30, Q31) ·
who may deactivate accounts or close the access gate (Q32). Q15 and Q16 remain deferred.
