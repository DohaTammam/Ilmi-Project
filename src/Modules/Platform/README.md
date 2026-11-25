# Platform module

Renamed from *Settings*. Owns general ilmi application configuration and shared application-level reference data.
Schema: `platform`.

| Table | Purpose |
|---|---|
| `system_settings` | Runtime configuration (OTP limits, lock durations, pending-activation minutes, ticket limits, job intervals) |
| `country_calling_codes` | 63 countries: calling code, number length range, default (moved here from the old Settings module) |

Not owned here, on purpose:
- **Accessibility modes** are managed by the Frontend only. There is no table, entity, repository or configuration for them.
- **Job executions** live in a separate external job database. Ilmi has no entity, DbSet or migration for them.
  If the Worker must talk to that database, it does so through an adapter outside this module's persistence model (open decision).
- **User language / email-notification preference** is part of the user's own data (`users.user_settings`, Users & Identity).

| Project | Layer | May reference |
|---|---|---|
| `Ilmi.Modules.Platform.Contracts` | Public surface (read settings, country catalogue) | nothing |
| `Ilmi.Modules.Platform.Domain` | Entities, value objects, enums | SharedKernel |
| `Ilmi.Modules.Platform.Application` | Use cases, ports | own Domain, own Contracts, SharedKernel, other modules' **Contracts** |
| `Ilmi.Modules.Platform.Infrastructure` | `PlatformDbContext`, configurations, migrations (schema `platform`), external adapters | own Application |
| `Ilmi.Modules.Platform.Presentation` | Controllers, requests/responses | own Application, own Contracts |

Status: structure only, no implementation.
