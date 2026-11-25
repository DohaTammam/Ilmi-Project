# ilmi

Backend for the **ilmi Web & Mobile Application** (Phase 1) on **ASP.NET Core / .NET 10**.

- **Architectural style:** Modular Monolith: one deployable API (plus one worker), split into six independent business modules.
- **Architectural pattern:** Clean Architecture *inside each module*.
- Source of requirements: *ilmi Web & Application Phase 1 SRS v3.1*. Open requirement conflicts: [docs/srs-open-questions.md](docs/srs-open-questions.md).

> **Status: architecture, skeleton and database design only.** There is no business logic, no endpoints and no database code.
> The only code is two empty host entry points and the architecture tests that guard the structure.
> Nothing in the database design may be implemented before it is approved.

## Documents

| Document | Content |
|---|---|
| [docs/architecture.md](docs/architecture.md) | module diagram, dependency rules, project/folder structure, DbContext ownership |
| [docs/database/database-design.md](docs/database/database-design.md) | module-to-schema mapping, table inventory, constraints, relationships, changes, assumptions, open decisions |
| [docs/database/erd.md](docs/database/erd.md) | Mermaid ERD source per area (all entities and attributes) |
| [docs/database/diagrams/](docs/database/diagrams/README.md) | generated ERD files: PDF, SVG, PNG and editable draw.io |
| [docs/srs-open-questions.md](docs/srs-open-questions.md) | requirement conflicts and gaps |

## Modules and schemas

| Module | Schema | Owns |
|---|---|---|
| **Users & Identity** (`UsersIdentity`) | `users` | External and Internal accounts, profiles, Rules, special needs, user settings, registration, login, OTP, sessions, Google/Apple, Quick Login (UC002-UC008) |
| **Family** | `family` | add dependent, QR link/unlink, family view and rules (UC013-UC017) |
| **Notifications** | `notifications` | notification centre; email, SMS, push delivery (UC020) |
| **Platform** | `platform` | system settings, country calling codes (formerly Settings) |
| **Support** | `support` | Contact Us, tickets, attachments (UC019) |
| **Cms** | `cms` | local cache of the external CMS, onboarding completions (UC009-UC012, UC018) |

Accessibility *modes* are managed by the Frontend only (no table). Job executions live in a separate external database (not in Ilmi's model).

## Structure

```text
Ilmi.sln
src/
  Host/                                   composition roots (no business code, ever)
    Ilmi.Api/                             ASP.NET Core host: wires up every module's Presentation + Infrastructure
    Ilmi.Worker/                          background-job host
  BuildingBlocks/
    Ilmi.SharedKernel/                    tiny set of types shared by all modules
  Modules/
    UsersIdentity/  Family/  Notifications/  Platform/  Support/  Cms/
      Ilmi.Modules.<Module>.Contracts/        public surface other modules may use (DTOs, service interfaces)
      Ilmi.Modules.<Module>.Domain/           Entities, ValueObjects, Enums, Events, Exceptions
      Ilmi.Modules.<Module>.Application/      Commands, Queries, Abstractions (ports), Dtos, EventHandlers
      Ilmi.Modules.<Module>.Infrastructure/   Persistence (DbContext, Configurations, Migrations, Repositories), ExternalServices
      Ilmi.Modules.<Module>.Presentation/     Controllers, Requests, Responses
      README.md                               what the module owns
tests/
  Ilmi.ArchitectureTests/                 enforces the dependency rules and the approved module list
  Ilmi.IntegrationTests/                  host smoke test
docs/
```

Empty folders contain a `.gitkeep` so the structure is versioned. Remove it when you add the first real file.

## Dependency rules

Inside every module (dependencies point inward):

```text
Presentation ──► Application ──► Domain ──► SharedKernel
Infrastructure ─► Application      Application ──► own Contracts
```

| Project | May reference |
|---|---|
| Contracts | nothing |
| Domain | SharedKernel |
| Application | own Domain, own Contracts, SharedKernel, **other modules' Contracts** |
| Infrastructure | own Application / Domain / Contracts, SharedKernel, other modules' Contracts |
| Presentation | own Application, own Contracts, SharedKernel, other modules' Contracts (never Domain or Infrastructure) |
| Host (Api, Worker) | every module's Presentation/Application/Infrastructure; nothing references a Host |

Between modules:

1. A module may use another module **only through that module's `Contracts` project**.
2. A module owns its data: its own **schema** and its own DbContext. No reading another module's tables, no cross-schema foreign keys, no cross-module navigation properties.
   Cross-module references are plain ids validated through Contracts. Physical foreign keys exist only inside one module.
3. Controllers (Presentation) contain no business logic.
4. Prefer synchronous calls through Contracts interfaces; use in-process events where one module must react to another.

`tests/Ilmi.ArchitectureTests` reads every `.csproj` and fails the build when a rule is broken or when a retired module project reappears.

## Technology (chosen, not yet used in code)

| Concern | Choice |
|---|---|
| Web API | ASP.NET Core 10 |
| Persistence | EF Core 10 + SQL Server, one DbContext per module |
| Validation | FluentValidation 12 |
| API docs | Swashbuckle (Swagger/OpenAPI) |
| Background jobs | .NET Generic Host `BackgroundService` |
| Tests | xUnit, Microsoft.AspNetCore.Mvc.Testing |

All packages are pinned to the .NET 10 line (the newest "latest" NuGet packages target .NET 11).

## Build and test

```powershell
dotnet build Ilmi.sln
dotnet test Ilmi.sln
```

Nothing else is required to run yet: no database, no configuration, no secrets.

## Next steps

1. Approve the decisions listed in [docs/database/database-design.md](docs/database/database-design.md) section 10 and answer the open questions.
2. Then implement module by module: **Platform → Users & Identity → Notifications → Family → Cms → Support**, domain first, then application, infrastructure, presentation.
3. Cross-cutting items to design before the first endpoint: response envelope and error handling, authentication/JWT, the scheduled-job approach, logging with PII masking,
   and load testing for the SRS performance targets (NFR001-NFR003).
