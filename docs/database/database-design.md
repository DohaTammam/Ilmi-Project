# ilmi database design (revision 6: Identity isolated in Infrastructure, global mobile uniqueness)

**Status: Users & Identity is implemented on ASP.NET Core Identity** (21 tables: Identity's `AspNetUsers` is the main users table, with `external_users` and `internal_users` profile tables; `UsersIdentityDbContext`; initial migration `InitialUsersIdentity`, **applied to the local SQL Server LocalDB database `Ilmi`**). The other five modules are still design only.
Column-level definitions (types, keys, nullability) are in the ERDs: [erd.md](erd.md). This document holds everything the ERDs cannot show:
inventory, constraints, indexes, rules, ownership, change log and open decisions.

Sources: ilmi Web & Application Phase 1 SRS v3.1, the earlier database design (revision 2) and the confirmed decisions made since.
The earlier design lived in chat only; this file replaces it as the single reference.

## 1. Module to schema mapping

One SQL Server database. One schema per module. Each module owns its own DbContext, configurations and migrations.

| # | Module | Schema | Projects (`Ilmi.Modules.<Name>.*`) | Tables |
|---|---|---|---|---|
| 1 | Users & Identity | `users` | `UsersIdentity` | 21 |
| 2 | Family | `family` | `Family` | 1 |
| 3 | Notifications | `notifications` | `Notifications` | 3 |
| 4 | Platform | `platform` | `Platform` | 2 |
| 5 | Support | `support` | `Support` | 3 |
| 6 | Cms | `cms` | `Cms` | 9 |
| | | | **Total** | **39** |

Retired schemas: `identity` (merged into `users`) and `settings` (split: user preferences into `users`, application configuration into `platform`).

## 2. Table inventory

Status: **Required** = directly required by the SRS or a confirmed decision. **Conditional** = depends on an undefined SRS item. **Deferred** = structure kept, details postponed.

| # | Table | Schema | Status | Purpose |
|---|---|---|---|---|
| 1 | AspNetUsers | users | Required | The main users table: ASP.NET Core Identity users (email, password hash, lockout) plus the shared account fields, the mobile number and UserType |
| 2 | external_users | users | Required | Profile of External users (date of birth, gender, QR code, avatar, consents); FK to AspNetUsers |
| 3 | internal_users | users | Required | Profile of Internal (staff) users (optional date of birth); FK to AspNetUsers |
| 4 | AspNetRoles | users | Required (Identity) | Identity security roles (not business Rules); none defined yet |
| 5 | AspNetUserRoles | users | Required (Identity) | Users in security roles |
| 6 | AspNetUserClaims | users | Required (Identity) | Identity user claims |
| 7 | AspNetRoleClaims | users | Required (Identity) | Identity role claims |
| 8 | AspNetUserLogins | users | Required (Identity) | Google and Apple logins (replaces the former external_logins) |
| 9 | AspNetUserTokens | users | Required (Identity) | Identity user tokens (unused until needed) |
| 10 | rules | users | Required | The five business Rules (lookup) |
| 11 | user_rule_assignments | users | Required | The Rules currently assigned to the user |
| 12 | user_rule_history | users | Required (recommended) | Append-only audit of Rule changes |
| 13 | avatars | users | Required | 5 static avatars (lookup) |
| 14 | disability_types | users | Required | 4 special-needs values (lookup, DDL02) |
| 15 | user_accessibility_needs | users | Required | Declared special needs (multi-select) |
| 16 | user_settings | users | Required | Language and email-notification preference |
| 17 | otp_challenges | users | Required | Email OTP attempts |
| 18 | verification_lockouts | users | Required | Temporary locks (OTP and Parent Add Dependent) |
| 19 | user_sessions | users | Required | Revocable sessions, until logout |
| 20 | quick_login_registrations | users | Required (mobile) | Device-bound public key for Quick Login |
| 21 | login_audit | users | Required | Login log |
| 22 | family_links | family | Required | Guardian to dependent links with history |
| 23 | notifications | notifications | Required | In-app notification centre |
| 24 | notification_deliveries | notifications | Required | Per-channel delivery and retry outbox (1:N from notifications) |
| 25 | push_device_tokens | notifications | **Conditional** | Needs a defined push provider |
| 26 | system_settings | platform | Required | Runtime configuration |
| 27 | country_calling_codes | platform | Required | 63 countries (DDL03) |
| 28 | ticket_types | support | Required | 5 ticket types (lookup) |
| 29 | support_tickets | support | Required, details **Deferred (Q15)** | Contact Us tickets |
| 30 | ticket_attachments | support | Required | Up to 5 files per ticket |
| 31 | content_pages | cms | Required | Cached page JSON |
| 32 | exhibition_nodes | cms | Required, **provisional hierarchy** | Cached tree (Wing/Gallery/Cluster) |
| 33 | exhibition_node_translations | cms | Required | ar/en names |
| 34 | exhibitions | cms | Required, **provisional categories** | Cached exhibitions |
| 35 | exhibition_translations | cms | Required | ar/en texts |
| 36 | update_categories | cms | Required | Cached update categories |
| 37 | updates | cms | Required | Cached news and announcements |
| 38 | update_translations | cms | Required | ar/en texts |
| 39 | onboarding_completions | cms | Required | Completion log (application data, not cache) |

## 3. Table specifications (constraints, indexes, rules)

Conventions: UTC `datetime2(3)` with `SYSUTCDATETIME()` defaults; `uniqueidentifier` keys created by the application (EF Core SQL Server sequential GUID generator,
not .NET `Guid.CreateVersion7()`, which does not sort sequentially in SQL Server); `bigint IDENTITY` for high-volume logs; `smallint` lookup keys; string enums
with CHECK constraints; `rowversion` for optimistic concurrency where concurrent edits are plausible; default delete behaviour RESTRICT.

### 3.1 Users & Identity (schema `users`): implemented on ASP.NET Core Identity

Conventions: UTC `datetime2(3)`; `uniqueidentifier` keys created by the application with a SQL-Server-ordered sequential GUID (`SequentialGuid`);
`bigint IDENTITY` for logs; string enums with CHECK constraints; `rowversion` where concurrent edits are plausible; default delete behaviour RESTRICT.
Identity's tables keep Identity's standard names and columns (`AspNetUsers`, ...); the module's own tables use snake_case. Columns are in the ERD ([erd.md](erd.md)).

**AspNetUsers** is the main users table (ASP.NET Core Identity). One row is shared by two code models: Identity's `ApplicationUser` (Infrastructure) owns Identity's columns,
the Domain `User` (a plain class) owns the business columns (see section 7.1).
- From Identity: `Id`, `UserName` (the email address), `NormalizedUserName`, `Email`, `NormalizedEmail`, `EmailConfirmed`, `PasswordHash` (null for social-only users), `SecurityStamp`, `ConcurrencyStamp`,
  `PhoneNumber` (not used), `PhoneNumberConfirmed`, `TwoFactorEnabled`, `LockoutEnd`, `LockoutEnabled`, `AccessFailedCount`.
- Added by the module: `FirstName`, `LastName`, `UserKind`, **`MobileCountryCode`, `MobileNationalNumber`, `UserType`, `MobileUniquenessKey` (computed)**, `AccountStatus`, `PendingActivationExpiresAt`, `IsActive`, `CreatedAt`, `CreatedBy`, `UpdatedOn`, `UpdatedBy`, `RowVersion`.
- UQ `ux_users_normalized_email` on `NormalizedEmail` (login is by email only), UQ `UserNameIndex`, UQ `(Id, UserKind)` (target of the profile foreign keys), and **UQ `ux_users_mobile`** (section 4.3).
- CHECKs: `UserKind`, `AccountStatus`, `UserType` value sets; `PendingActivation => PendingActivationExpiresAt NOT NULL`; **`External => UserType and mobile NOT NULL`, `Internal => UserType NULL`**. FKs (RESTRICT): `CreatedBy`, `UpdatedBy` -> AspNetUsers.
- `UpdatedOn` is set automatically by the persistence interceptor whenever the user, its profile row, or Identity's side of the row (password, lockout, logins) is modified. `IsActive` (access gate) is independent of `AccountStatus` (lifecycle).

**external_users** (1 : 0..1 with AspNetUsers): PK/FK `user_id`; `user_kind` fixed to `'External'`, and **`(user_id, user_kind)` is a composite foreign key to `AspNetUsers (Id, UserKind)`**, so an External profile can only belong to an External user.
Columns: `date_of_birth`, `gender`, `qr_code`, `avatar_id`, `receive_newsletter`, `notify_ilmi_special_needs`, `terms_accepted_at`, `row_version`. UQ `ux_external_users_qr_code`; CHECK QR format; FK `avatar_id` -> avatars.

**internal_users** (1 : 0..1 with AspNetUsers): PK/FK `user_id`; `user_kind` fixed to `'Internal'` (same composite foreign key). Columns: `date_of_birth` (optional), `row_version`.
Internal users have **no UserType**. No employee attributes exist: the requirements define none, and EmployeeNumber, DepartmentId and JobTitle are deliberately absent.

**Identity tables** (created by the Identity model): `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`. Roles are security roles and are **not** the business Rules; none are defined or seeded.
`AspNetUserLogins` holds the Google / Apple logins: PK `(LoginProvider, ProviderKey)` and an added UQ `(UserId, LoginProvider)`.

**rules**: PK `id`; UQ `code` (`CHILD`, `TEEN`, `ADULT`, `PARENT`, `REGISTRATION_OFFICER`); seeded. `name_ar` is nullable and seeded null (Q25). No permissions columns.

**user_rule_assignments**: PK `(user_id, rule_id)`, so the same Rule can never be assigned twice. **No unique constraint on `user_id`**: one Rule or several is an open decision (Q17). FKs: `user_id`, `assigned_by` -> AspNetUsers, `rule_id` -> rules, all RESTRICT. IX `(rule_id)`.

**user_rule_history**: append-only (the interceptor rejects updates and deletes). PK `id` bigint identity. FKs: `user_id`, `changed_by` -> AspNetUsers; `from_rule_id`, `to_rule_id` -> rules. `related_family_link_id` is a logical reference to Family. IX `(user_id, changed_at DESC)`.

**avatars**: lookup, UQ `code`, no seed. **disability_types**: lookup (SRS DDL02), UQ `code`, seeded with the four documented values (`code` values derived from the English names).

**user_accessibility_needs**: junction, composite PK `(user_id, disability_type_id)`. FK `user_id` -> AspNetUsers (CASCADE), FK to disability_types (RESTRICT). Frontend accessibility modes are not stored.

**user_settings**: PK/FK `user_id` -> AspNetUsers. CHECK `language IN ('ar','en')`. No accessibility mode column. `updated_at` NOT NULL; `row_version`.

**otp_challenges**: `channel` with CHECK `= 'Email'`. IX `(target_email, purpose, created_at DESC)`, IX `(expires_at)`. `code_hash binary(32)` (HMAC-SHA-256 with a server key); `pending_payload` is **`varbinary(max)` AES-GCM ciphertext**, wiped when consumed. FKs `user_id`, `initiated_by_user_id` -> AspNetUsers.

**verification_lockouts**: PK `(purpose, scope_key)`; no FK by design. **user_sessions**: UQ `refresh_token_hash` (SHA-256 of the opaque token); `expires_at` NULL (a Web session lasts until logout). **login_audit**: append-only.

**quick_login_registrations**: device-bound public key only (`public_key varbinary(512)`, `key_algorithm`, `key_fingerprint` UQ, one-time challenge, `is_enabled`, `enabled_at`, `last_used_at`, `disabled_at`, `disabled_reason`). Filtered UQ `(device_id) WHERE is_enabled = 1`. No PIN, passcode hash or biometric data.

### 3.2 Family (schema `family`)

**family_links**: PK `id`. **Filtered UQ `(guardian_user_id, dependent_user_id) WHERE status = 'Active'`**. CHECK `guardian_user_id <> dependent_user_id`. CHECK status, link_method.
IX `(guardian_user_id, status)`, IX `(dependent_user_id, status)`. `row_version`. No limit on guardians or dependents anywhere.
All user columns are logical references, with no physical FK and no navigation property to a Users & Identity entity.

### 3.3 Notifications (schema `notifications`)

**notifications**: PK bigint identity. IX `(user_id, created_at DESC)`; filtered IX `(user_id) WHERE is_read = 0`. Must never contain secrets.
**notification_deliveries**: FK `notification_id` → notifications, **SET NULL, one notification to many deliveries**. IX `(status, next_attempt_at)`, IX `(notification_id)`.
`payload_encrypted` is `varbinary(max)` ciphertext cleared after sending. **push_device_tokens** (Conditional): UQ `(user_id, device_id)`, IX `(token)`.

### 3.4 Platform (schema `platform`)

**system_settings**: PK `setting_key` (`key` is a reserved word in SQL Server). Seed values:

| Key | Seed | Source |
|---|---|---|
| otp.expiry_seconds | 180 | SRS |
| otp.max_attempts | 5 | SRS |
| otp.lock_minutes | 10 | confirmed |
| login.max_failures / login.lock_minutes | 5 / 10 | SRS |
| password_reset.max_resends | 3 | SRS |
| pending_activation.minutes | 10 | confirmed |
| add_dependent.lock_minutes | 10 | confirmed |
| add_dependent.max_failed_attempts | **no seed (Open, Q21)** | |
| jobs.cms_sync_interval_hours / jobs.age_check_interval_hours | 24 / 24 | SRS |
| support.attachment_max_count / support.attachment_max_mb | 5 / 3 | SRS |

**country_calling_codes**: UQ `name_en`; `calling_code` is not unique (+1 is shared). Other tables store the calling code as plain text, never as a foreign key.

### 3.5 Support (schema `support`)

**support_tickets**: UQ `ticket_number` (SQL SEQUENCE; format Deferred), FK `ticket_type_id`, IX `(user_id, submitted_at DESC)`, IX `(email)`. `user_id` is a nullable logical reference (guests).
**ticket_attachments**: FK `ticket_id` (CASCADE), UQ `(ticket_id, attachment_no)` with CHECK 1-5, UQ `(ticket_id, sha256)`, CHECK `size_bytes <= 3 MB`. Files live in object storage.
Preserved unchanged from the earlier design. Title, status values, numbering format and guest-data retention are **Deferred – Requires Future Business Discussion** (Q15).

### 3.6 Cms (schema `cms`)

Preserved unchanged: the external CMS remains the source of truth and the database holds a local cache. Cache rows are un-published (`is_published = 0`), not deleted, when they disappear from the CMS.
Translation tables use composite PKs `(parent_id, locale)` with CASCADE. `content_pages.payload` CHECK `ISJSON`. IX `updates(is_published, published_at DESC)`.
`onboarding_completions` is application data, not cache. Hierarchy (Wing/Gallery/Cluster) and categories remain **provisional**.

**Synchronisation:** a Worker job pulls the content every 24 hours (configurable) under a single-runner lock. Items are matched by `source_id`; unchanged items (same `source_hash`) are skipped;
after a fully successful fetch items missing from the source are un-published; failures keep serving the last good cache. Read queries use only local tables. Where the job's run state is stored
is now outside Ilmi's database (Section 8), so the `checkpoint` column once proposed on `job_executions` is gone; the CMS API contract and watermark are Open (Q26).

## 4. Relationships and cardinality

### 4.1 Physical foreign keys (same module only)

| From → To | Cardinality | On delete |
|---|---|---|
| external_users.avatar_id -> avatars.id | N : 0..1 | RESTRICT |
| external_users (user_id, user_kind) -> AspNetUsers (Id, UserKind) | **1 : 0..1** (composite FK) | RESTRICT |
| internal_users (user_id, user_kind) -> AspNetUsers (Id, UserKind) | **1 : 0..1** (composite FK) | RESTRICT |
| AspNetUserLogins / AspNetUserRoles / AspNetUserClaims / AspNetUserTokens .UserId -> AspNetUsers.Id | 1 : N | CASCADE (Identity default) |
| AspNetUserRoles.RoleId, AspNetRoleClaims.RoleId -> AspNetRoles.Id | 1 : N | CASCADE (Identity default) |
| user_accessibility_needs.user_id -> AspNetUsers.Id | 1 : N | CASCADE |
| user_accessibility_needs.disability_type_id → disability_types.id | N : 1 | RESTRICT |
| user_settings.user_id -> AspNetUsers.Id | 1 : 0..1 | RESTRICT |
| user_rule_assignments.user_id -> AspNetUsers.Id; .rule_id -> rules.id; .assigned_by -> AspNetUsers.Id | 1 : N (no unique constraint on user_id; one or several Rules is open) | RESTRICT |
| user_rule_history.user_id → AspNetUsers.Id; from/to rule → rules.id | 1 : N | RESTRICT |
| otp_challenges.user_id, initiated_by_user_id; login_audit.user_id → users.id | 0..1 : N | RESTRICT |
| AspNetUsers.CreatedBy / UpdatedBy, user_rule_assignments.assigned_by, user_rule_history.changed_by -> AspNetUsers.Id | N : 0..1 | RESTRICT |
| notification_deliveries.notification_id → notifications.id | **1 : N** | SET NULL |
| support_tickets.ticket_type_id → ticket_types.id | N : 1 | RESTRICT |
| ticket_attachments.ticket_id → support_tickets.id | 1 : 0..5 | CASCADE |
| exhibition_nodes.parent_id → exhibition_nodes.id; exhibitions.node_id → exhibition_nodes.id | 1 : N | RESTRICT |
| every `*_translations` table → its parent | 1 : N, one row per language | CASCADE |
| updates.category_id → update_categories.id | N : 1 | RESTRICT |

Identity tables that used to be logical references to Users (`user_id`) are now **physical** foreign keys because both belong to the same module.

### 4.2 Logical cross-module references (no database constraint)

| Column | Owner of the target | Validated through | Notes |
|---|---|---|---|
| family_links.guardian_user_id, dependent_user_id, unlinked_by_user_id | Users & Identity | UsersIdentity.Contracts (user directory) | validated on write |
| notifications.user_id; notification_deliveries.user_id; push_device_tokens.user_id | Users & Identity | UsersIdentity.Contracts | validated on write |
| support_tickets.user_id (nullable) | Users & Identity | UsersIdentity.Contracts | null for guests |
| onboarding_completions.user_id (nullable) | Users & Identity | informational | null for guests |
| user_rule_history.related_family_link_id (nullable) | Family | informational | |
| created_by / updated_by on non-users tables | Users & Identity | UsersIdentity.Contracts | null for Worker/system |

Rules for logical references: plain `uniqueidentifier` column with an index; never a navigation property; never a read of another module's tables or DbContext;
users are never hard-deleted (deletion is part of deferred Q16), so a reference cannot dangle; an integrity-check job reports orphans and never deletes.
Writes that span modules (for example link a dependent and change the guardian's Rule) run in one transaction on the shared database connection.

### 4.3 Global mobile-number uniqueness (approved decision)

**Rule:** a mobile number cannot be used by both an Internal and an External user, nor by two Teens/Adults. Children may share a number.
**Enforced by the database**, with one unique index on the one shared users table:

```sql
-- persisted computed column on users.AspNetUsers
MobileUniquenessKey AS (CASE WHEN [UserType] = 'Child' THEN CONVERT(varchar(36), [Id]) ELSE '' END) PERSISTED
-- the index
CREATE UNIQUE INDEX ux_users_mobile ON users.AspNetUsers (MobileCountryCode, MobileNationalNumber, MobileUniquenessKey)
  WHERE MobileNationalNumber IS NOT NULL;
```

- Teens, Adults and Internal users all get the key `''`, so a number can appear once among them (and Internal users, whose `UserType` is NULL, are covered).
- A Child's key is its own `Id`, so Children never collide with each other or with anyone else.
- Users without a mobile number (`NULL`, allowed for Internal users) are outside the index.
- A Child -> Teen change on a shared number is refused by the index, never silently resolved (open question Q20).

**Why this is the simplest reliable option.** The mobile number is an attribute of both user kinds, so it belongs on the shared table, next to `UserType` (a Child is a property of that same row).
Keeping it in the two profile tables would need an index that spans two tables. The alternatives were rejected:

| Alternative | Why not |
|---|---|
| A third table of mobile numbers with a unique index | Extra table and joins, plus a copy of "is Child" that can drift from `UserType` |
| Triggers on both profile tables | Race conditions between concurrent inserts into different tables; logic hidden outside the schema |
| An indexed view over both tables | SQL Server does not allow `UNION` in an indexed view |
| A filter such as `UserType <> 'Child' OR UserType IS NULL` | SQL Server filtered indexes reject `OR` |

The cost: `UserType` and the mobile number moved from the profile tables onto `AspNetUsers` (one computed column added). The application also checks the number before saving (`registration.mobile_taken`); the index is what makes it safe under concurrency.

## 5. UserKind, UserType, Rule, and Identity roles

| Concept | Values | Applies to | Stored in | Driven by |
|---|---|---|---|---|
| **UserKind** | External, Internal | every user | `AspNetUsers.UserKind` | how the account was created |
| **UserType** | Child (6-12), Teen (13-16), Adult (17+) | External only | `AspNetUsers.UserType` (NULL for Internal users) | age (completed years) |
| **Rule** | Child, Teen, Adult, Parent, Registration Officer | see Q30 | `user_rule_assignments` -> `rules` | registration, family events, age transitions |
| **Identity role** | (none defined) | security | `AspNetRoles` / `AspNetUserRoles` | internal authorization model, not designed |

- Rules business logic is unchanged: Adult -> Parent when an Adult successfully links a dependent; Parent -> Adult when the last active dependent is unlinked (driven by the Family module through `IUserRuleService`);
  Child -> Teen on the 13th birthday and Teen -> Adult on the 17th (age job not implemented; Child -> Teen mobile conflict open, Q20). A Parent keeps `UserType = Adult`.
- **Registration Officer** stays a Rule only: its permissions and assignment workflow are undefined and it is not a security role. Identity roles and permissions are separate and not designed.
- Both user kinds use the same Identity authentication infrastructure (password hash, lockout, logins); `UserKind` lets authentication and authorization policies differ per kind.

### 5.1 `is_active` and `account_status` (non-conflicting definitions, needs approval)

| | `account_status` | `is_active` |
|---|---|---|
| Meaning | Business **lifecycle** of the account | Administrative **access gate** (record-level switch) |
| Values | Active, PendingActivation, Deactivated | 1 = enabled, 0 = disabled |
| Changed by | Lifecycle events only: dependent added (PendingActivation), activation (Active), deactivation (Deactivated) | Administrative or system action only (and any future deletion design, Q16) |
| Never changed by | `is_active` | account_status events |

Access rule: a user may sign in only when **`is_active = 1` AND `account_status = 'Active'`**. Because the two columns never change for the same reason and neither is derived from the other,
they cannot contradict each other. `account_status` is not removed. What actually sets `is_active = 0`, and who may, is not defined in the requirements (Q32).

### 5.2 `UpdatedOn`

The earlier `users` design already had an audit column `updated_at`. To avoid two columns with identical meaning, **the `UpdatedOn` property is mapped to the existing `updated_at` column**
(`datetime2(3)`, UTC), now NOT NULL so it always holds a value (equal to creation time until the first modification). This follows the project's `_at` naming. If you want a separate physical column, say so.
Mechanism: an EF Core `SaveChangesInterceptor` in the Users & Identity Infrastructure sets `UpdatedOn = UtcNow` for every `users` entry in state **Modified**.
No database trigger (triggers complicate EF's `OUTPUT` handling). Bulk `ExecuteUpdate` bypasses the interceptor and must set the value explicitly.

## 6. Integrity checks (scheduled, report only)

Orphaned logical references; users with both a credentials row and an external login; External users without exactly one Rule assignment or without a profile; the Rule/UserType and Parent consistency checks above.

## 7. DbContext ownership and configuration structure

Each module has exactly one DbContext in its own Infrastructure project. A DbContext never contains another module's entities and is never injected into another module.
Migrations live with their module and use a history table in the module's schema (`<schema>.__EFMigrationsHistory`).

| Module | DbContext | Schema | Entities (DbSets) |
|---|---|---|---|
| Users & Identity | `UsersIdentityDbContext` | users | 21 (Section 2, #1-21) |
| Family | `FamilyDbContext` | family | 1 |
| Notifications | `NotificationsDbContext` | notifications | 3 |
| Platform | `PlatformDbContext` | platform | 2 |
| Support | `SupportDbContext` | support | 3 |
| Cms | `CmsDbContext` | cms | 9 |

Structure (folders exist now; the files are created at implementation):

```text
Ilmi.Modules.UsersIdentity.Infrastructure/
  Persistence/
    UsersIdentityDbContext                      (to be created)
    Configurations/
      Users/      User, ExternalUserProfile, Avatar, DisabilityType, UserAccessibilityNeed, UserSettings, Rule, UserRuleAssignment, UserRuleHistory
      Identity/   UserCredential, ExternalLogin, OtpChallenge, VerificationLockout, UserSession, QuickLoginRegistration, LoginAudit
    Interceptors/   audit timestamp (UpdatedOn)
    Migrations/     history table users.__EFMigrationsHistory
    Repositories/
Ilmi.Modules.<Family|Notifications|Platform|Support|Cms>.Infrastructure/
  Persistence/  <Module>DbContext, Configurations/, Migrations/, Repositories/
```

Domain entities for Users & Identity live in `Domain/Entities/Users` and `Domain/Entities/Identity` (one module, two cohesive areas). The Application and Presentation layers mirror this with `Users` and `Identity` sub-folders.

### 7.1 ASP.NET Core Identity stays out of the Domain

The Domain `User` is a plain class (no `IdentityUser`, no Identity package). Everything Identity-specific is in Infrastructure:

| Piece | Where | Role |
|---|---|---|
| `User` | Domain | Business account: names, kind, status, email (as the login identifier), mobile, `UserType`, Rules. Knows nothing about Identity |
| `ApplicationUser : IdentityUser<Guid>` | Infrastructure (`Persistence/Identity`) | The Identity user: user name, normalized email, password hash, security stamp, lockout. `ApplicationUser.From(user)` is the one place that maps a Domain user to an Identity user |
| `ApplicationRole` | Infrastructure | Identity security role (not a business Rule) |
| `UsersIdentityDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>` | Infrastructure | Both entities map `AspNetUsers` (EF Core table splitting on the shared primary key); `Email` and `NormalizedEmail` are the two columns both map and always carry the same value |
| `IUserAccountManager` | Application (port) | Create account, password check/change, lockout, external logins. Use cases depend only on this port |
| `UserAccountManager` | Infrastructure | Implements the port with `UserManager<ApplicationUser>` and the DbContext. The only place that uses `UserManager` |

Architecture tests (`IdentityIsolationTests`) fail the build if the Domain, Application, Contracts or Presentation reference an Identity package or mention Identity types, or if a use case uses `UserManager` directly.

## 8. Removed, moved, merged and modified

| Kind | Item | Detail |
|---|---|---|
| **Merged** | module Users + Identity -> **Users & Identity** | projects `Ilmi.Modules.UsersIdentity.*`, schema `users`; the `identity` schema is retired |
| **Renamed** | module Settings -> **Platform** | projects `Ilmi.Modules.Platform.*`, schema `platform` |
| **Moved** | `country_calling_codes`, `system_settings` | Settings -> Platform |
| **Moved** | `user_settings` | Settings -> Users & Identity |
| **Replaced** | my own `users` table -> **`AspNetUsers`** (ASP.NET Core Identity) | the main users table; `User : IdentityUser<Guid>` |
| **Added** | `external_users`, `internal_users` | profile tables with a (composite) foreign key to AspNetUsers (date of birth, gender, QR code, consents; internal: optional date of birth) |
| **Moved** | mobile number and `UserType` | profile tables -> `AspNetUsers`, so one unique index enforces mobile uniqueness across Internal and External users (section 4.3) |
| **Decoupled** | ASP.NET Core Identity from the Domain | `User` is a plain class; `ApplicationUser` / `UserManager` live in Infrastructure (section 7.1) |
| **Added** | `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserTokens` | Identity's standard tables; unused until needed |
| **Replaced** | `user_credentials` -> `AspNetUsers.PasswordHash`, `LockoutEnd`, `AccessFailedCount` | Identity hashes passwords and tracks lockout; social-only users have a null hash |
| **Replaced** | `external_logins` -> `AspNetUserLogins` | Google / Apple; PK (LoginProvider, ProviderKey) plus UQ (UserId, LoginProvider) |
| **Added** | `user_rule_assignments` (replaces `users.rule_id`) | PK `(user_id, rule_id)`, no unique on `user_id` |
| **Added** | `otp_challenges.channel` (CHECK = Email) | |
| **Renamed** | `quick_login_registrations.revoked_*` -> `disabled_*` | |
| **Modified** | `rules.name_ar`, `avatars.name_ar` | nullable (Arabic names not provided) |
| **Removed** | `accessibility_modes`, `user_settings.accessibility_mode_id` | modes are managed by the Frontend only |
| **Removed** | `job_executions` | managed by a separate external job database |
| **Unchanged** | `user_accessibility_needs`, all Notifications, Support and Cms tables | including the 1:N notification to deliveries |

External job database: Ilmi has no entity, DbSet, configuration, migration or repository for it (open, Q33).

## 9. Confirmed rules carried over unchanged

Email-only login and OTP · Google/Apple sign-in · social-only users have no password record · Children may share a mobile number · age classification by whole years ·
Adult ↔ Parent transitions · Registration Officer is a Rule with undefined permissions · 10-minute Pending Activation (`activation_expires_at`; nothing is invented after expiry) ·
10-minute Parent Add Dependent lockout (attempt count Open) · web sessions last until logout · the Device PIN is never stored or received (Quick Login uses a device-bound public key with
a server challenge) · the generated password is emailed (hashed in the database, encrypted payload cleared after sending, never in an in-app notification) · CMS content cached locally ·
Q15 and Q16 deferred.

Quick Login in one paragraph: the device creates a key pair in secure hardware whose private key needs OS user verification (the Device PIN). The backend stores only the public key, its
fingerprint and a one-time challenge. Sign-in = the device signs the challenge after the OS asks for the PIN; the backend verifies the signature. A new enable creates a new row; revocation sets
`is_enabled = 0` with a reason. Failed PIN attempts and lockout are handled by the operating system, so the database has no PIN counters.

## 10. Assumptions, decisions and open questions

### Assumptions made during implementation (please confirm or correct)

| # | Assumption |
|---|---|
| A1 | Age is the number of completed whole years from the date of birth and the current UTC date. |
| A2 | The SRS's "CRM" (GSR019) means the external CMS. |
| A3 | A dependent has a `users` row in `PendingActivation` from the moment a Parent adds them (Family drives this; not implemented yet). |
| A4 | `users.UpdatedOn` is the existing `updated_at` column; it is also NOT NULL on `user_settings` (the earlier design had it nullable there). |
| A5 | `FirstName`, `LastName`, email, mobile number and `UserType` are shared account fields on `AspNetUsers`; date of birth, gender, QR code and consents are in the profile tables. Identity's `PhoneNumber` is not used. |
| A6 | Opaque session tokens (hashed in `user_sessions`) are used instead of JWTs. Identity is registered without cookies or its sign-in UI (`AddIdentityCore`): the module issues its own sessions after the email OTP. |
| A7 | Login `outcome` values beyond Success (InvalidCredentials, InvalidOtp, AccountLocked, AccountInactive) are an addition: the requirements define only the successful-login log. |
| A8 | Quick Login challenges live for 60 seconds (configurable); the requirements do not specify it. |
| A9 | `disability_types.code` values are derived from the English names; the Rules have no Arabic names yet. |

### Decisions that need your approval

| # | Decision |
|---|---|
| D7 (revised) | Rules held in `user_rule_assignments` with PK `(user_id, rule_id)` and **no** unique on `user_id`, plus append-only `user_rule_history`. The earlier design had `UQ(user_id)` (single Rule); the implementation brief asked not to enforce one. |
| D8 | Quick Login = device-bound ES256 public key plus one-time server challenge (implemented). The brief's `Method` column is not stored (the OS owns the PIN/biometric choice) and `KeyMetadata` is the algorithm, fingerprint and platform columns. |
| D9 (approved) | Mobile numbers are unique across Internal and External users, Children excepted: one unique index over a persisted computed key on `AspNetUsers` (section 4.3). Child -> Teen conflicts are reported by the database, never silently resolved (tested). |
| D10 | `is_active` (access gate) and `account_status` (lifecycle) as defined in Section 5.1. |
| D12 | `external_users` and `internal_users` profile tables with a composite foreign key `(user_id, user_kind)` to `AspNetUsers`. `internal_users` holds only an optional date of birth; employee attributes are not invented. |
| D14 | `AspNetUsers.PasswordHash` is NULL for social-only users (Identity's model); they cannot create or use a password. |
| D15 | Deactivate / activate / administrative-access operations exist in the Application layer but are **not exposed over HTTP** until the internal authorization model is defined. |

### Open questions

| # | Question | Status |
|---|---|---|
| Q17 | Can a user hold several Rules? (No schema change needed either way.) | Open |
| Q18 | Registration Officer: permissions, assignment workflow, whether it can coexist with Adult/Parent | Open |
| Q19 | What happens when Pending Activation expires (row fate, email reuse)? | Open |
| Q20 | Mobile-number conflict when a Child becomes a Teen: require a new number, relax uniqueness for that user, or block the transition | Open |
| Q21 | Failed attempts and counting window before the Parent Add Dependent lockout (the 10-minute duration is confirmed) | Open |
| Q22 | Does a still-pending dependent count as "successfully added" for Adult -> Parent? | Open |
| Q23 | Confirm "years only" means completed whole years | Open |
| Q24 | Mobile session lifetime; whether Quick Login skips email OTP; optional key attestation | Open |
| Q25 | Arabic avatar names and Arabic Rule names | Open |
| Q26 | CMS API contract, exact hierarchy and categories, age filtering, media caching | Open |
| Q27 | May an account hold both a password and a social login? (Linking Google/Apple to an existing email account is refused until decided.) | Open |
| Q29 | How is a duplicate dependent detected (ERR024)? | Open |
| Q30 | Do Internal users have a business Rule at all? | Open |
| Q31 | Internal user attributes and authentication/authorisation policy differences | Open |
| Q32 | What sets `is_active = 0` and who may do it? Who may deactivate an account? | Open |
| Q33 | How the Worker talks to the external job database | Open |
| Q34 | Mobile uniqueness across Internal and External users | **Resolved**: unique across both kinds, Children excepted (section 4.3) |
| Q15 | Support ticket numbering, statuses, title, guest data | **Deferred - Requires Future Business Discussion** |
| Q16 | Privacy, retention, deletion, minors' data, logging, consent history | **Deferred - Requires Future Business Discussion** |

Not implemented, awaiting the integrations: email delivery (Notifications module), Google/Apple token verification, per-country mobile length validation (Platform country catalogue), reading limits from Platform `system_settings`.

## 11. Validation performed

- No removed entity (`accessibility_modes`, `user_accessibility_modes`, `job_executions`, `identity`/`settings` schemas, `users.rule_id`) appears in this design or in the ERDs.
- All dependency rules are enforced by `tests/Ilmi.ArchitectureTests` (reads every project file): Contracts reference nothing; Domain → SharedKernel; Application → own Domain/Contracts + other modules' Contracts;
  Infrastructure → own Application; Presentation → own Application/Contracts; modules reach each other only through Contracts; hosts are referenced by nobody. A new test also asserts the retired
  `Users`, `Identity` and `Settings` projects no longer exist.
- Physical foreign keys exist only inside one module; every cross-module column is listed in Section 4.2.
