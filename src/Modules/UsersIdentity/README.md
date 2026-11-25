# Users & Identity module

Merged from the former *Users* and *Identity* modules. Owns every person-related fact and the single, centralised
authentication infrastructure for **both user kinds** (`External` and `Internal`). Schema: `users`.

SRS scope: UC002-UC008 (registration, login, OTP, passwords, Google/Apple, Quick Login boundary, profile).

| Area | Tables (schema `users`) |
|---|---|
| Accounts | `users` (shared), `external_user_profiles` |
| Reference data | `avatars`, `disability_types` |
| Declared needs and preferences | `user_accessibility_needs`, `user_settings` |
| Business Rules | `rules`, `user_rule_assignments`, `user_rule_history` |
| Authentication | `user_credentials`, `external_logins`, `otp_challenges`, `verification_lockouts`, `user_sessions`, `quick_login_registrations`, `login_audit` |

Three concepts that must never be merged: **UserKind** (External/Internal), **UserType** (Child/Teen/Adult, external users,
age based) and **Rule** (Child/Teen/Adult/Parent/RegistrationOfficer, business category). Internal security roles and
permissions are separate again and are not designed yet.

| Project | Layer | May reference |
|---|---|---|
| `Ilmi.Modules.UsersIdentity.Contracts` | Public surface for other modules | nothing |
| `Ilmi.Modules.UsersIdentity.Domain` | Entities (`Entities/Users`, `Entities/Identity`), value objects, enums, events | SharedKernel |
| `Ilmi.Modules.UsersIdentity.Application` | Use cases (`Commands|Queries/Users|Identity`), ports | own Domain, own Contracts, SharedKernel, other modules' **Contracts** |
| `Ilmi.Modules.UsersIdentity.Infrastructure` | `UsersIdentityDbContext`, configurations per area, interceptors, migrations (schema `users`) | own Application |
| `Ilmi.Modules.UsersIdentity.Presentation` | Controllers (`Users`, `Identity`), requests/responses | own Application, own Contracts |

Status: structure only, no implementation. Design: [docs/database/database-design.md](../../../docs/database/database-design.md).
