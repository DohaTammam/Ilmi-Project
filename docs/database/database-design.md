# ilmi database design (revision 3: six modules)

**Status: design only.** No database, SQL script, EF entity, configuration, migration or repository exists. Approval is needed before implementation.
Column-level definitions (types, keys, nullability) are in the ERDs: [erd.md](erd.md). This document holds everything the ERDs cannot show:
inventory, constraints, indexes, rules, ownership, change log and open decisions.

Sources: ilmi Web & Application Phase 1 SRS v3.1, the earlier database design (revision 2) and the confirmed decisions made since.
The earlier design lived in chat only; this file replaces it as the single reference.

## 1. Module to schema mapping

One SQL Server database. One schema per module. Each module owns its own DbContext, configurations and migrations.

| # | Module | Schema | Projects (`Ilmi.Modules.<Name>.*`) | Tables |
|---|---|---|---|---|
| 1 | Users & Identity | `users` | `UsersIdentity` | 16 |
| 2 | Family | `family` | `Family` | 1 |
| 3 | Notifications | `notifications` | `Notifications` | 3 |
| 4 | Platform | `platform` | `Platform` | 2 |
| 5 | Support | `support` | `Support` | 3 |
| 6 | Cms | `cms` | `Cms` | 9 |
| | | | **Total** | **34** |

Retired schemas: `identity` (merged into `users`) and `settings` (split: user preferences into `users`, application configuration into `platform`).

## 2. Table inventory

Status: **Required** = directly required by the SRS or a confirmed decision. **Conditional** = depends on an undefined SRS item. **Deferred** = structure kept, details postponed.

| # | Table | Schema | Status | Purpose |
|---|---|---|---|---|
| 1 | users | users | Required | Shared account for External and Internal users |
| 2 | external_user_profiles | users | Required | Profile data of external people (age, mobile, QR, avatar, consents) |
| 3 | rules | users | Required | The five business Rules (lookup) |
| 4 | user_rule_assignments | users | Required | The user's current Rule |
| 5 | user_rule_history | users | Required (recommended) | Append-only audit of Rule changes |
| 6 | avatars | users | Required | 5 static avatars (lookup) |
| 7 | disability_types | users | Required | 4 special-needs values (lookup, DDL02) |
| 8 | user_accessibility_needs | users | Required | Declared special needs (multi-select) |
| 9 | user_settings | users | Required | Language and email-notification preference |
| 10 | user_credentials | users | Required | Password hash, only for password accounts |
| 11 | external_logins | users | Required | Google / Apple identities |
| 12 | otp_challenges | users | Required | Email OTP attempts |
| 13 | verification_lockouts | users | Required | Temporary locks (OTP and Parent Add Dependent) |
| 14 | user_sessions | users | Required | Revocable sessions, until logout |
| 15 | quick_login_registrations | users | Required (mobile) | Device-bound public key for Quick Login |
| 16 | login_audit | users | Required | Login log |
| 17 | family_links | family | Required | Guardian to dependent links with history |
| 18 | notifications | notifications | Required | In-app notification centre |
| 19 | notification_deliveries | notifications | Required | Per-channel delivery and retry outbox (1:N from notifications) |
| 20 | push_device_tokens | notifications | **Conditional** | Needs a defined push provider |
| 21 | system_settings | platform | Required | Runtime configuration |
| 22 | country_calling_codes | platform | Required | 63 countries (DDL03) |
| 23 | ticket_types | support | Required | 5 ticket types (lookup) |
| 24 | support_tickets | support | Required, details **Deferred (Q15)** | Contact Us tickets |
| 25 | ticket_attachments | support | Required | Up to 5 files per ticket |
| 26 | content_pages | cms | Required | Cached page JSON |
| 27 | exhibition_nodes | cms | Required, **provisional hierarchy** | Cached tree (Wing/Gallery/Cluster) |
| 28 | exhibition_node_translations | cms | Required | ar/en names |
| 29 | exhibitions | cms | Required, **provisional categories** | Cached exhibitions |
| 30 | exhibition_translations | cms | Required | ar/en texts |
| 31 | update_categories | cms | Required | Cached update categories |
| 32 | updates | cms | Required | Cached news and announcements |
| 33 | update_translations | cms | Required | ar/en texts |
| 34 | onboarding_completions | cms | Required | Completion log (application data, not cache) |

## 3. Table specifications (constraints, indexes, rules)

Conventions: UTC `datetime2(3)` with `SYSUTCDATETIME()` defaults; `uniqueidentifier` keys created by the application (EF Core SQL Server sequential GUID generator,
not .NET `Guid.CreateVersion7()`, which does not sort sequentially in SQL Server); `bigint IDENTITY` for high-volume logs; `smallint` lookup keys; string enums
with CHECK constraints; `rowversion` for optimistic concurrency where concurrent edits are plausible; default delete behaviour RESTRICT.

### 3.1 Users & Identity (schema `users`)

**users**
- PK `id`. UQ `email` (stored lower-case, case-insensitive collation). UQ `(id, user_kind)` (target of the composite FK below).
- CHECK `user_kind IN ('External','Internal')`. CHECK `account_status IN ('Active','PendingActivation','Deactivated')`.
- CHECK `account_status <> 'PendingActivation' OR activation_expires_at IS NOT NULL`.
- Self FKs `created_by`, `updated_by` (nullable, RESTRICT). `row_version` rowversion.
- Shared by both kinds: only what every account has (identity, name, email, lifecycle). Nothing external-specific is stored here.

**external_user_profiles** (1:0..1 with users; exists only for External users)
- PK/FK `user_id` → users. `user_kind` is fixed to `'External'` (CHECK) and `(user_id, user_kind)` is a composite FK to `users(id, user_kind)`.
  This makes it impossible to attach an external profile to an Internal user. (Recommended hardening; the reverse, "every External user has a profile", is an Application invariant.)
- UQ `qr_code` (CHECK 10 chars of 0-9A-Z, binary collation).
- Filtered UQ `(mobile_country_code, mobile_national_number) WHERE user_type IN ('Teen','Adult')`: Children may share a mobile number (decision D9).
- IX `(user_type, date_of_birth)` for the daily age-transition job. FK `avatar_id` → avatars (RESTRICT).
- CHECK `user_type IN ('Child','Teen','Adult')`, `gender IN ('Male','Female')` or null.
- Moved here from the old `users.users` unchanged: date of birth, gender, user type, mobile, QR code, avatar, newsletter and special-needs-notification flags, terms acceptance.

**rules**: PK `id`; UQ `code` (`Child`, `Teen`, `Adult`, `Parent`, `RegistrationOfficer`). No permissions columns: Registration Officer permissions are undefined.

**user_rule_assignments**: current Rule only. PK `(user_id, rule_id)`; **UQ `(user_id)` enforces exactly one current Rule per user today**.
Dropping that single unique constraint is the only schema change needed if the business later allows several Rules (Q17). FKs: `user_id` → users, `rule_id` → rules,
`assigned_by` → users (null = system). IX `(rule_id)`. A junction cannot force "every user has a Rule"; that is an Application invariant plus an integrity check (Section 6).

**user_rule_history**: append-only. PK `id` (bigint identity). FKs `user_id`, `changed_by` → users; `from_rule_id`, `to_rule_id` → rules.
`related_family_link_id` is a logical reference to Family. CHECK `reason IN (...)` as in the ERD. IX `(user_id, changed_at DESC)`.

**avatars / disability_types**: lookups with UQ `code`. **user_accessibility_needs**: PK `(user_id, disability_type_id)`; FK `user_id` → external_user_profiles (CASCADE),
FK to disability_types (RESTRICT). This is the *declared special needs* concept; it is unrelated to the frontend accessibility modes.

**user_settings**: PK/FK `user_id` → users. CHECK `language IN ('ar','en')`. Contains no accessibility mode column (removed, see Section 8).

**user_credentials**: PK/FK `user_id`. `password_hash` is NOT NULL: **a social-only user has no row**. Both rows existing for one user is forbidden (Application rule plus integrity check).

**external_logins**: UQ `(provider, provider_subject)`, UQ `(user_id, provider)`, CHECK provider IN ('Google','Apple').

**otp_challenges**: email only (no channel column). IX `(target_email, purpose, created_at DESC)`, IX `(expires_at)` for purging.
`pending_payload` is **`varbinary(max)` holding ciphertext** (authenticated encryption of the pending form), never Base64 text. Cleared on use; rows purged after expiry.

**verification_lockouts**: PK `(purpose, scope_key)`. `scope_key` is a hash of the target email, or the Parent's user id for the Add Dependent lockout. No FK by design.

**user_sessions**: UQ `refresh_token_hash`. Filtered IX `(user_id) WHERE revoked_at IS NULL`. `expires_at` is NULL for "active until explicit logout". `last_used_at` is informational only.

**quick_login_registrations**: filtered UQ `(device_id) WHERE is_enabled = 1` (one enabled user per device); UQ `key_fingerprint`; IX `(user_id)`;
CHECK `(is_enabled = 1 AND revoked_at IS NULL) OR (is_enabled = 0 AND revoked_at IS NOT NULL)`.

**login_audit**: IX `(user_id, occurred_at DESC)`. No IP address or user agent (not requested by the SRS; logging policy belongs to deferred Q16).

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
| external_user_profiles.user_id → users.id | 1 : 0..1 | RESTRICT |
| external_user_profiles (user_id, user_kind) → users (id, user_kind) | composite, guards the kind | RESTRICT |
| external_user_profiles.avatar_id → avatars.id | N : 0..1 | RESTRICT |
| user_accessibility_needs.user_id → external_user_profiles.user_id | 1 : N | CASCADE |
| user_accessibility_needs.disability_type_id → disability_types.id | N : 1 | RESTRICT |
| user_settings.user_id → users.id | 1 : 0..1 | RESTRICT |
| user_rule_assignments.user_id → users.id; .rule_id → rules.id | 1 : N today 1 : 0..1 (UQ) | RESTRICT |
| user_rule_history.user_id → users.id; from/to rule → rules.id | 1 : N | RESTRICT |
| user_credentials / external_logins / user_sessions / quick_login_registrations .user_id → users.id | 1 : 0..1, 1 : 0..2, 1 : N, 1 : N | RESTRICT |
| otp_challenges.user_id, initiated_by_user_id; login_audit.user_id → users.id | 0..1 : N | RESTRICT |
| users.created_by / updated_by, user_rule_assignments.assigned_by, user_rule_history.changed_by → users.id | N : 0..1 | RESTRICT |
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

## 5. UserKind, UserType, Rule

| Concept | Values | Applies to | Stored in | Driven by |
|---|---|---|---|---|
| **UserKind** | External, Internal | every user | `users.user_kind` | how the account was created |
| **UserType** | Child (6-12), Teen (13-16), Adult (17+) | External only | `external_user_profiles.user_type` | age |
| **Rule** | Child, Teen, Adult, Parent, RegistrationOfficer | see Q30 | `user_rule_assignments` / `rules` | transitions below |

- All Rules business logic and transitions are **unchanged**: Adult → Parent when an Adult successfully adds or links a dependent; Parent → Adult when the last active dependent is unlinked;
  Child → Teen on the 13th birthday; Teen → Adult on the 17th. A Parent keeps `user_type = Adult`. Automatic transitions never overwrite RegistrationOfficer.
- Consistency checks (Application rules plus an integrity job): a Child/Teen/Adult Rule equals the user type; Parent implies Adult plus at least one active guardian link;
  an Adult with an active guardian link has the Parent Rule (unless a Registration Officer); every External user has exactly one current Rule.
- **Registration Officer** stays a Rule only. Its permissions and assignment workflow are undefined. It is **not** a security role.
- **Internal security roles and permissions** are a separate concept, outside this task and not designed. When defined, they belong in this module in their own tables and must not reuse `rules`.
- **Internal users** share `users` and the whole authentication infrastructure. `user_kind` lets authentication and authorisation policies differ per kind. No internal-only attributes are in
  the requirements, so **no `internal_user_profiles` table** (and no EmployeeNumber, DepartmentId or JobTitle) is created. Revisit when attributes are specified (Q31).

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
(`datetime2(3)`, UTC), now NOT NULL with default `SYSUTCDATETIME()` so it always holds a value (equal to creation time until the first modification). This follows the project's `_at` naming. If you want a separate physical column, say so.
Mechanism: an EF Core `SaveChangesInterceptor` in the Users & Identity Infrastructure sets `UpdatedOn = UtcNow` for every `users` entry in state **Modified** (and also when its `external_user_profiles` row is modified).
No database trigger (triggers complicate EF's `OUTPUT` handling). Bulk `ExecuteUpdate` bypasses the interceptor and must set the value explicitly.

## 6. Integrity checks (scheduled, report only)

Orphaned logical references; users with both a credentials row and an external login; External users without exactly one Rule assignment or without a profile; the Rule/UserType and Parent consistency checks above.

## 7. DbContext ownership and configuration structure

Each module has exactly one DbContext in its own Infrastructure project. A DbContext never contains another module's entities and is never injected into another module.
Migrations live with their module and use a history table in the module's schema (`<schema>.__EFMigrationsHistory`).

| Module | DbContext | Schema | Entities (DbSets) |
|---|---|---|---|
| Users & Identity | `UsersIdentityDbContext` | users | 16 (Section 2, #1-16) |
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

## 8. Removed, moved, merged and modified

| Kind | Item | Detail |
|---|---|---|
| **Merged** | module Users + Identity → **Users & Identity** | projects `Ilmi.Modules.UsersIdentity.*`, schema `users`; the `identity` schema is retired |
| **Renamed** | module Settings → **Platform** | projects `Ilmi.Modules.Platform.*`, schema `platform` |
| **Moved** | `country_calling_codes` | Settings → Platform |
| **Moved** | `system_settings` | Settings → Platform (schema `platform`) |
| **Moved** | `user_settings` | Settings → Users & Identity (it is user data; real FK to `users`) |
| **Moved** | the nine Identity tables | `identity.*` → `users.*` |
| **Moved** | external-only attributes of `users` | → new `external_user_profiles` |
| **Added** | `external_user_profiles`, `user_rule_assignments` | |
| **Added** | `users.user_kind`, `users.is_active`, `UpdatedOn` mapping | Section 5 |
| **Modified** | `users` | shared columns only; `rule_id` removed (replaced by `user_rule_assignments`); `activation_expires_at` stays here for its CHECK |
| **Modified** | `user_rule_history` | now has real FKs (same module) |
| **Modified** | identity tables | `user_id` becomes a physical FK |
| **Modified** | `family_links` | unchanged structure; user references stay logical |
| **Removed** | `accessibility_modes` table | modes are managed by the Frontend only. No entity, repository, configuration or migration |
| **Removed** | `user_settings.accessibility_mode_id` | |
| **Removed** | `job_executions` table and the `checkpoint` column | managed by a separate external job database. Not in Ilmi's model or ERD |
| **Unchanged** | `user_accessibility_needs` | stays in Users & Identity (declared special needs, not display modes) |
| **Unchanged** | all Notifications, Support and Cms tables | including the corrected 1:N notification to deliveries |

External job database: Ilmi has no entity, DbSet, configuration, migration or repository for it. If the Worker must talk to it, a port and adapter can live outside the persistence model
(for example in the Platform module's `ExternalServices` folder). The approach is Open (Q33).

## 9. Confirmed rules carried over unchanged

Email-only login and OTP · Google/Apple sign-in · social-only users have no password record · Children may share a mobile number · age classification by whole years ·
Adult ↔ Parent transitions · Registration Officer is a Rule with undefined permissions · 10-minute Pending Activation (`activation_expires_at`; nothing is invented after expiry) ·
10-minute Parent Add Dependent lockout (attempt count Open) · web sessions last until logout · the Device PIN is never stored or received (Quick Login uses a device-bound public key with
a server challenge) · the generated password is emailed (hashed in the database, encrypted payload cleared after sending, never in an in-app notification) · CMS content cached locally ·
Q15 and Q16 deferred.

Quick Login in one paragraph: the device creates a key pair in secure hardware whose private key needs OS user verification (the Device PIN). The backend stores only the public key, its
fingerprint and a one-time challenge. Sign-in = the device signs the challenge after the OS asks for the PIN; the backend verifies the signature. A new enable creates a new row; revocation sets
`is_enabled = 0` with a reason. Failed PIN attempts and lockout are handled by the operating system, so the database has no PIN counters.

## 10. Assumptions and unresolved decisions

### Assumptions I made (please confirm or correct)

| # | Assumption |
|---|---|
| A1 | Age is the number of completed whole years from the date of birth and the current UTC date. |
| A2 | The SRS's "CRM" (GSR019) means the external CMS. |
| A3 | A dependent has a `users` row in `PendingActivation` from the moment a Parent adds them. |
| A4 | `user_rule_assignments` replaces `users.rule_id` and holds only the current Rule; history is in `user_rule_history`. |
| A5 | `UpdatedOn` is the existing `updated_at` column (Section 5.2). |
| A6 | `first_name`, `last_name` and `email` are shared by both kinds; mobile number and date of birth are external-profile data. |
| A7 | `user_settings` applies to every user (both kinds). |
| A8 | Identity tables now use physical FKs to `users` because they share a module. |

### Decisions that need your approval

| # | Decision |
|---|---|
| D7 (revised) | Current Rule stored in `user_rule_assignments` with UQ `(user_id)` (one Rule today), plus `user_rule_history`. |
| D8 | Quick Login = device-bound public key with a server challenge. |
| D9 | Mobile uniqueness as a filtered unique index on Teen/Adult; Child → Teen conflicts are reported, never silently resolved. |
| D10 | `is_active` (access gate) and `account_status` (lifecycle) definitions in Section 5.1. |
| D11 | `UpdatedOn` mapped to the existing `updated_at` column. |
| D12 | No `internal_user_profiles` table until internal attributes are specified. |
| D13 | Composite FK `(user_id, user_kind)` so profiles cannot be attached to Internal users. |

### Open questions

| # | Question | Status |
|---|---|---|
| Q17 | Can a user hold several Rules? (Only a unique-constraint change away.) | Open |
| Q18 | Registration Officer: permissions, assignment workflow, whether it can coexist with Adult/Parent | Open |
| Q19 | What happens when Pending Activation expires (row fate, email reuse)? | Open |
| Q20 | Mobile-number conflict when a Child becomes a Teen: options are (a) require a new number, (b) allow and relax uniqueness for that user, (c) block the transition | Open |
| Q21 | Failed attempts and counting window before the Parent Add Dependent lockout | Open |
| Q22 | Does a still-pending dependent count as "successfully added" for Adult → Parent? Is the link effective while pending? | Open |
| Q23 | Confirm "years only" means completed whole years | Open |
| Q24 | Mobile session lifetime; whether Quick Login skips email OTP; optional key attestation | Open |
| Q25 | Arabic avatar names and Arabic Rule names | Open |
| Q26 | CMS API contract, exact hierarchy and categories, age filtering, media caching | Open |
| Q27 | May an account hold both a password and a social login? | Open |
| Q29 | How is a duplicate dependent detected (ERR024)? | Open |
| Q30 | Do Internal users have a business Rule at all (is Registration Officer for Internal users)? | Open |
| Q31 | Internal user attributes and authentication/authorisation policy differences | Open |
| Q32 | What sets `is_active = 0`, and by whom? | Open |
| Q33 | How the Worker talks to the external job database | Open |
| Q15 | Support ticket numbering, statuses, title, guest data | **Deferred – Requires Future Business Discussion** |
| Q16 | Privacy, retention, deletion, minors' data, logging, consent history | **Deferred – Requires Future Business Discussion** |

Confirmed earlier and applied: Q1-Q3, Q5-Q8, Q14 (see Section 9), Q9 (CMS external with local cache). Not applicable now: Q11 (timezone), Q28 (accessibility modes are frontend-only).

## 11. Validation performed

- No removed entity (`accessibility_modes`, `user_accessibility_modes`, `job_executions`, `identity`/`settings` schemas, `users.rule_id`) appears in this design or in the ERDs.
- All dependency rules are enforced by `tests/Ilmi.ArchitectureTests` (reads every project file): Contracts reference nothing; Domain → SharedKernel; Application → own Domain/Contracts + other modules' Contracts;
  Infrastructure → own Application; Presentation → own Application/Contracts; modules reach each other only through Contracts; hosts are referenced by nobody. A new test also asserts the retired
  `Users`, `Identity` and `Settings` projects no longer exist.
- Physical foreign keys exist only inside one module; every cross-module column is listed in Section 4.2.
