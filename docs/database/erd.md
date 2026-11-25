# ilmi database ERDs (one diagram per area)

Design only. Nothing here has been created in SQL Server. Target: one SQL Server database, one schema per module.

## How to read these diagrams

| Notation | Meaning |
|---|---|
| `PK` | Primary key |
| `FK` | Foreign key (a **physical** constraint in the database, same module only) |
| `UK` | Single-column unique constraint. Composite, filtered and CHECK constraints are written in the column comment and listed in [database-design.md](database-design.md) |
| comment `null` | Column is nullable. No comment on nullability means NOT NULL |
| comment `logical -> ...` | **Logical cross-module reference**: a plain column with no database foreign key. Existence is validated through the owning module's Contracts |
| **Solid line** | Physical foreign key between tables of the **same** module |
| **Dotted line** | Logical cross-module reference (no constraint). The other module's table is drawn as a stub named `<Module>_<table>` showing only its key |
| `\|\|--o{` etc. | Crow's foot cardinality: `\|\|` exactly one, `o\|` zero or one, `}o` zero or many, `}\|` one or many |

Types are SQL Server types (`uniqueidentifier`, `datetime2(3)` in UTC, `rowversion`). `+A` columns (the audit set) are written out only where
they matter: `created_at`, `updated_at`, `created_by`, `updated_by` on mutable business tables.

Diagram index: [1 Users core](#1-users--identity--accounts-and-profiles), [2 Rules](#2-users--identity--rules), [3 Authentication](#3-users--identity--authentication),
[4 Family](#4-family), [5 Notifications](#5-notifications), [6 Platform](#6-platform), [7 Support](#7-support), [8 CMS exhibitions](#8-cms--exhibitions), [9 CMS pages, updates, onboarding](#9-cms--pages-updates-onboarding).

---

## 1. Users & Identity: accounts and profiles

Schema `users`. `users` is the shared account for both user kinds. Everything specific to external people is in `external_user_profiles`.
There is **no** `internal_user_profiles` table: the requirements define no internal-only attributes (Open).

```mermaid
erDiagram
  users ||--o| external_user_profiles : "physical FK, 1 : 0..1"
  users ||--o| user_settings : "physical FK, 1 : 0..1"
  avatars |o--o{ external_user_profiles : "physical FK"
  external_user_profiles ||--o{ user_accessibility_needs : "physical FK"
  disability_types ||--o{ user_accessibility_needs : "physical FK"

  users {
    uniqueidentifier id PK
    varchar(8) user_kind "External|Internal; UQ(id,user_kind)"
    nvarchar(250) first_name
    nvarchar(250) last_name
    nvarchar(250) email UK "lower-case, case-insensitive"
    varchar(17) account_status "Active|PendingActivation|Deactivated; lifecycle"
    bit is_active "default 1; administrative access gate"
    datetime2(3) activation_expires_at "null; required while PendingActivation"
    datetime2(3) created_at "default SYSUTCDATETIME()"
    datetime2(3) updated_at "UpdatedOn; set by EF interceptor"
    uniqueidentifier created_by "null; FK users.id"
    uniqueidentifier updated_by "null; FK users.id"
    rowversion row_version
  }
  external_user_profiles {
    uniqueidentifier user_id PK "FK users.id"
    varchar(8) user_kind "fixed External; FK (user_id,user_kind)"
    date date_of_birth
    varchar(6) gender "null; Male|Female"
    varchar(5) user_type "Child|Teen|Adult; age based"
    varchar(5) mobile_country_code
    varchar(15) mobile_national_number "filtered UQ with country code WHERE user_type IN (Teen,Adult)"
    char(10) qr_code UK "0-9 A-Z"
    smallint avatar_id FK "null"
    bit receive_newsletter
    bit notify_ilmi_special_needs
    datetime2(3) terms_accepted_at
    datetime2(3) created_at
    datetime2(3) updated_at "null"
    rowversion row_version
  }
  avatars {
    smallint id PK
    varchar(40) code UK
    nvarchar(100) name_en
    nvarchar(100) name_ar "null; Arabic names not provided"
    nvarchar(250) image_key
    tinyint sort_order
    bit is_active
  }
  disability_types {
    smallint id PK
    varchar(40) code UK
    nvarchar(100) name_en
    nvarchar(100) name_ar
    tinyint sort_order
    bit is_active
  }
  user_accessibility_needs {
    uniqueidentifier user_id PK "FK external_user_profiles.user_id, cascade"
    smallint disability_type_id PK "FK disability_types.id, restrict"
    datetime2(3) created_at
  }
  user_settings {
    uniqueidentifier user_id PK "FK users.id"
    char(2) language "ar|en"
    bit email_notifications_enabled "default 1"
    datetime2(3) created_at
    datetime2(3) updated_at "null"
    rowversion row_version
  }
```

---

## 2. Users & Identity: Rules

`UserType` (age) and `Rule` (business category) are separate. A Parent keeps `user_type = Adult`. `user_rule_assignments` holds the **current** Rule;
`user_rule_history` is the append-only record of every change.

```mermaid
erDiagram
  users ||--o| user_rule_assignments : "physical FK; UQ(user_id) = one current Rule today (Q17)"
  rules ||--o{ user_rule_assignments : "physical FK"
  users ||--o{ user_rule_history : "physical FK"
  rules |o--o{ user_rule_history : "physical FK from_rule_id"
  rules ||--o{ user_rule_history : "physical FK to_rule_id"
  user_rule_history }o..o| Family_family_links : "logical -> family.family_links.id"

  users {
    uniqueidentifier id PK
    varchar(8) user_kind
  }
  rules {
    smallint id PK
    varchar(30) code UK "Child|Teen|Adult|Parent|RegistrationOfficer"
    nvarchar(100) name_en
    nvarchar(100) name_ar "Arabic names not provided"
    bit is_age_based "1 for Child, Teen, Adult"
    bit is_staff "1 for RegistrationOfficer"
    tinyint sort_order
    bit is_active
  }
  user_rule_assignments {
    uniqueidentifier user_id PK "FK users.id; also UQ(user_id) while single Rule"
    smallint rule_id PK "FK rules.id"
    datetime2(3) assigned_at
    uniqueidentifier assigned_by "null; FK users.id; null = system"
  }
  user_rule_history {
    bigint id PK "identity"
    uniqueidentifier user_id FK
    smallint from_rule_id FK "null for first assignment"
    smallint to_rule_id FK
    varchar(30) reason "Registered|DependentAdded|LastDependentUnlinked|AgeTransition|OfficerAssigned|OfficerRemoved"
    uniqueidentifier related_family_link_id "null; logical -> family.family_links.id"
    datetime2(3) changed_at
    uniqueidentifier changed_by "null; FK users.id; null = system"
  }
  Family_family_links {
    uniqueidentifier id PK "owned by Family"
  }
```

---

## 3. Users & Identity: authentication

One authentication infrastructure for both user kinds. Social-only users have **no** `user_credentials` row. Same module, so `user_id` here is a physical FK
(restrict, users are never hard-deleted).

```mermaid
erDiagram
  users ||--o| user_credentials : "physical FK; password accounts only"
  users ||--o{ external_logins : "physical FK; Google/Apple"
  users |o--o{ otp_challenges : "physical FK user_id"
  users |o--o{ otp_challenges : "physical FK initiated_by_user_id"
  users ||--o{ user_sessions : "physical FK"
  users ||--o{ quick_login_registrations : "physical FK"
  users |o--o{ login_audit : "physical FK"

  users {
    uniqueidentifier id PK
    varchar(8) user_kind
    varchar(17) account_status
    bit is_active
  }
  user_credentials {
    uniqueidentifier user_id PK "FK users.id"
    varchar(256) password_hash "NOT NULL; salted adaptive hash"
    datetime2(3) password_changed_at
    tinyint failed_login_count "default 0"
    datetime2(3) locked_until "null"
    rowversion row_version
  }
  external_logins {
    uniqueidentifier id PK
    uniqueidentifier user_id FK "UQ(user_id,provider)"
    varchar(6) provider "Google|Apple; UQ(provider,provider_subject)"
    varchar(255) provider_subject
    nvarchar(250) provider_email
    datetime2(3) created_at
  }
  otp_challenges {
    uniqueidentifier id PK
    varchar(25) purpose "Registration|Login|PasswordReset|DependentVerification|EmailChange"
    nvarchar(250) target_email "OTP is sent by email only"
    uniqueidentifier user_id FK "null"
    uniqueidentifier initiated_by_user_id FK "null; the Parent"
    binary(32) code_hash "HMAC-SHA-256"
    datetime2(3) expires_at
    tinyint attempt_count "default 0"
    tinyint resend_count "default 0"
    datetime2(3) consumed_at "null"
    varbinary(max) pending_payload "null; encrypted bytes, cleared on use"
    datetime2(3) created_at
  }
  verification_lockouts {
    varchar(25) purpose PK
    varchar(100) scope_key PK "hash of email, or the Parent user id"
    tinyint failed_count "default 0"
    datetime2(3) last_failed_at "null"
    datetime2(3) locked_until "null"
  }
  user_sessions {
    uniqueidentifier id PK
    uniqueidentifier user_id FK
    varchar(6) client_type "Web|Mobile"
    varchar(100) device_id "null"
    binary(32) refresh_token_hash UK
    datetime2(3) issued_at
    datetime2(3) last_used_at "null; informational only"
    datetime2(3) expires_at "null = until explicit logout"
    datetime2(3) revoked_at "null"
    varchar(30) revoked_reason "null; UserLogout|SystemRevoked"
  }
  quick_login_registrations {
    uniqueidentifier id PK
    uniqueidentifier user_id FK
    varchar(100) device_id "filtered UQ WHERE is_enabled = 1"
    varchar(7) platform "iOS|Android"
    varbinary(512) public_key "SubjectPublicKeyInfo; never a PIN"
    varchar(10) key_algorithm "ES256"
    binary(32) key_fingerprint UK "SHA-256 of public_key"
    binary(32) challenge_nonce "null; one-time"
    datetime2(3) challenge_expires_at "null"
    bit is_enabled
    datetime2(3) enabled_at
    datetime2(3) last_used_at "null"
    datetime2(3) revoked_at "null"
    varchar(25) revoked_reason "null"
  }
  login_audit {
    bigint id PK "identity"
    uniqueidentifier user_id FK "null"
    varchar(12) login_method "PasswordOtp|Google|Apple|QuickLogin"
    varchar(25) outcome
    varchar(100) device_id "null"
    datetime2(3) occurred_at
  }
```

`verification_lockouts` has no foreign key on purpose: its key is a hash of an email address (registration, when no user exists yet) or a Parent's user id.

---

## 4. Family

Schema `family`. Both user columns are **logical** references to `users.users.id`.

```mermaid
erDiagram
  UsersIdentity_users ||..o{ family_links : "logical: guardian_user_id"
  UsersIdentity_users ||..o{ family_links : "logical: dependent_user_id"
  UsersIdentity_users |o..o{ family_links : "logical: unlinked_by_user_id"

  UsersIdentity_users {
    uniqueidentifier id PK "owned by Users and Identity"
  }
  family_links {
    uniqueidentifier id PK
    uniqueidentifier guardian_user_id "logical -> users.users.id; CHECK <> dependent"
    uniqueidentifier dependent_user_id "logical -> users.users.id; filtered UQ(guardian,dependent) WHERE status = Active"
    varchar(8) status "Active|Unlinked"
    varchar(10) link_method "Created|QrScan|ManualCode"
    datetime2(3) linked_at
    datetime2(3) unlinked_at "null"
    uniqueidentifier unlinked_by_user_id "null; logical -> users.users.id"
    datetime2(3) created_at
    datetime2(3) updated_at "null"
    uniqueidentifier created_by "null; logical"
    uniqueidentifier updated_by "null; logical"
    rowversion row_version
  }
```

No maximum number of guardians or dependents: there is no counter or cap anywhere.

---

## 5. Notifications

Schema `notifications`. **One notification has many deliveries (1:N).** A delivery may have no notification (OTP and password emails).

```mermaid
erDiagram
  notifications |o--o{ notification_deliveries : "physical FK, 1 : N (SET NULL)"
  UsersIdentity_users ||..o{ notifications : "logical: user_id"
  UsersIdentity_users |o..o{ notification_deliveries : "logical: user_id"
  UsersIdentity_users ||..o{ push_device_tokens : "logical: user_id"

  UsersIdentity_users {
    uniqueidentifier id PK "owned by Users and Identity"
  }
  notifications {
    bigint id PK "identity"
    uniqueidentifier user_id "logical -> users.users.id"
    varchar(20) template_code
    nvarchar(max) parameters_json "null; never secrets"
    datetime2(3) created_at
    bit is_read "default 0"
    datetime2(3) read_at "null"
  }
  notification_deliveries {
    bigint id PK "identity"
    bigint notification_id FK "null; SET NULL"
    uniqueidentifier user_id "null; logical -> users.users.id"
    varchar(5) channel "Email|Sms|Push"
    nvarchar(250) recipient "sensitive"
    varchar(20) template_code
    varbinary(max) payload_encrypted "null; ciphertext, cleared after send"
    varchar(8) status "Pending|Sent|Failed"
    tinyint attempt_count "default 0"
    datetime2(3) next_attempt_at "null"
    datetime2(3) sent_at "null"
    nvarchar(500) last_error "null"
    varchar(100) provider_message_id "null"
    datetime2(3) created_at
  }
  push_device_tokens {
    uniqueidentifier id PK "conditional table"
    uniqueidentifier user_id "logical -> users.users.id; UQ(user_id,device_id)"
    varchar(100) device_id
    varchar(7) platform "iOS|Android"
    nvarchar(500) token
    bit is_active
    datetime2(3) created_at
    datetime2(3) last_seen_at "null"
  }
```

---

## 6. Platform

Schema `platform`. Two independent reference tables with no relationships. Accessibility modes (frontend only) and `job_executions` (external job database) are intentionally absent.

```mermaid
erDiagram
  system_settings {
    varchar(100) setting_key PK "KEY is a reserved word in SQL Server"
    nvarchar(500) setting_value
    varchar(10) value_type "Int|Duration|Bool|String"
    nvarchar(500) description "null"
    datetime2(3) created_at
    datetime2(3) updated_at "null"
    uniqueidentifier created_by "null; logical"
    uniqueidentifier updated_by "null; logical"
    rowversion row_version
  }
  country_calling_codes {
    smallint id PK
    nvarchar(100) name_en UK
    nvarchar(100) name_ar
    varchar(5) calling_code "not unique (+1 shared)"
    tinyint min_length
    tinyint max_length
    varchar(30) example "null"
    bit is_default
    bit is_active
    tinyint sort_order
  }
```

---

## 7. Support

Schema `support`. Details marked **Deferred** (ticket numbering format, statuses, title, guest data retention; Q15) are not decided here.

```mermaid
erDiagram
  ticket_types ||--o{ support_tickets : "physical FK"
  support_tickets ||--o{ ticket_attachments : "physical FK, cascade; max 5"
  UsersIdentity_users |o..o{ support_tickets : "logical: user_id (null for guests)"

  UsersIdentity_users {
    uniqueidentifier id PK "owned by Users and Identity"
  }
  ticket_types {
    smallint id PK
    varchar(30) code UK
    nvarchar(100) name_en
    nvarchar(100) name_ar
    tinyint sort_order
    bit is_active
  }
  support_tickets {
    uniqueidentifier id PK
    int ticket_number UK "from a SEQUENCE; format Deferred"
    smallint ticket_type_id FK
    uniqueidentifier user_id "null; logical -> users.users.id"
    nvarchar(250) first_name "contact snapshot"
    nvarchar(250) last_name
    nvarchar(250) email
    varchar(5) mobile_country_code
    varchar(15) mobile_national_number
    nvarchar(100) title "null; Deferred"
    nvarchar(2500) description
    varchar(20) status "Deferred; value set undecided"
    datetime2(3) submitted_at
    datetime2(3) created_at
    datetime2(3) updated_at "null"
    rowversion row_version
  }
  ticket_attachments {
    uniqueidentifier id PK
    uniqueidentifier ticket_id FK "UQ(ticket_id,attachment_no), UQ(ticket_id,sha256)"
    tinyint attachment_no "CHECK 1-5"
    nvarchar(255) file_name
    varchar(150) content_type
    int size_bytes "CHECK <= 3 MB"
    binary(32) sha256
    nvarchar(500) storage_key "file bytes live in object storage"
    datetime2(3) created_at
  }
```

---

## 8. CMS: exhibitions

Schema `cms`. Local cache (read model) of the external CMS. Hierarchy and categories are **provisional** (Open): the requirements do not define them clearly.

```mermaid
erDiagram
  exhibition_nodes |o--o{ exhibition_nodes : "physical FK parent_id"
  exhibition_nodes ||--o{ exhibition_node_translations : "physical FK, cascade"
  exhibition_nodes |o--o{ exhibitions : "physical FK"
  exhibitions ||--o{ exhibition_translations : "physical FK, cascade"

  exhibition_nodes {
    uniqueidentifier id PK
    varchar(100) source_id UK "id in the external CMS"
    uniqueidentifier parent_id FK "null"
    varchar(8) node_type "Wing|Gallery|Cluster; provisional"
    smallint sort_order
    bit is_published
    datetime2(3) fetched_at
  }
  exhibition_node_translations {
    uniqueidentifier node_id PK "FK exhibition_nodes.id"
    char(2) locale PK "ar|en"
    nvarchar(250) name
  }
  exhibitions {
    uniqueidentifier id PK
    varchar(100) source_id UK
    uniqueidentifier node_id FK "null"
    varchar(7) category "null; Gallery|Garden; provisional"
    nvarchar(500) image_url "null"
    nvarchar(500) video_url "null"
    date availability_from "null"
    date availability_to "null"
    bit is_trending
    int trending_order "null"
    tinyint target_age_min "null; provisional"
    tinyint target_age_max "null; provisional"
    smallint sort_order
    bit is_published
    datetime2(3) source_updated_at "null"
    datetime2(3) fetched_at
  }
  exhibition_translations {
    uniqueidentifier exhibition_id PK "FK exhibitions.id"
    char(2) locale PK "ar|en"
    nvarchar(250) name
    nvarchar(max) description
    nvarchar(max) location
    nvarchar(max) price
    nvarchar(max) target_audience
    nvarchar(max) duration
    nvarchar(max) disclaimer
  }
```

---

## 9. CMS: pages, updates, onboarding

`onboarding_completions` is application data (UC012 BR03), not part of the cache.

```mermaid
erDiagram
  update_categories ||--o{ updates : "physical FK"
  updates ||--o{ update_translations : "physical FK, cascade"
  UsersIdentity_users |o..o{ onboarding_completions : "logical: user_id (null for guests)"

  UsersIdentity_users {
    uniqueidentifier id PK "owned by Users and Identity"
  }
  content_pages {
    varchar(50) page_key PK
    char(2) locale PK "ar|en"
    nvarchar(max) payload "CHECK ISJSON"
    varchar(64) source_hash
    bit is_published
    datetime2(3) source_updated_at "null"
    datetime2(3) fetched_at
  }
  update_categories {
    smallint id PK
    varchar(100) source_id UK
    nvarchar(100) name_en
    nvarchar(100) name_ar
  }
  updates {
    uniqueidentifier id PK
    varchar(100) source_id UK
    varchar(12) update_type "News|Announcement"
    smallint category_id FK
    nvarchar(500) image_url "null"
    datetime2(3) published_at
    bit is_published
    datetime2(3) fetched_at
  }
  update_translations {
    uniqueidentifier update_id PK "FK updates.id"
    char(2) locale PK "ar|en"
    nvarchar(250) title
    nvarchar(max) brief
    nvarchar(max) body
  }
  onboarding_completions {
    bigint id PK "identity"
    uniqueidentifier user_id "null; logical -> users.users.id"
    varchar(100) device_id
    varchar(7) platform "null"
    datetime2(3) completed_at
  }
```
