# ilmi

Backend for the **ilmi Web & Mobile Application** (Phase 1) on **ASP.NET Core / .NET 10**.

- **Architectural style:** Modular Monolith: one deployable API (plus one worker), split into independent business modules.
- **Architectural pattern:** Clean Architecture *inside each module*.
- Source of requirements: *ilmi Web & Application Phase 1 SRS v3.1*. Open requirement conflicts: [docs/srs-open-questions.md](docs/srs-open-questions.md).

> **Status: architecture and skeleton only.** There is no business logic, no endpoints, no database code. The only code is
> two empty host entry points and the architecture tests that guard the structure.

## Structure

```text
Ilmi.sln
src/
  Host/                                   composition roots (no business code, ever)
    Ilmi.Api/                             ASP.NET Core host: wires up every module's Presentation + Infrastructure
    Ilmi.Worker/                          background-job host
  BuildingBlocks/
    Ilmi.SharedKernel/                    tiny set of types shared by all modules (base entity, result type, ...)
  Modules/
    Identity/  Users/  Family/  Notifications/  Cms/  Support/  Settings/
      Ilmi.Modules.<Module>.Contracts/        public surface other modules may use (DTOs, service interfaces)
      Ilmi.Modules.<Module>.Domain/           Entities, ValueObjects, Enums, Events, Exceptions
      Ilmi.Modules.<Module>.Application/      Commands, Queries, Abstractions (ports), Dtos, EventHandlers
      Ilmi.Modules.<Module>.Infrastructure/   Persistence (Configurations, Repositories, Migrations), ExternalServices
      Ilmi.Modules.<Module>.Presentation/     Controllers, Requests, Responses
      README.md                               what the module owns (mapped to SRS use cases)
tests/
  Ilmi.ArchitectureTests/                 enforces the dependency rules below (51 tests)
  Ilmi.IntegrationTests/                  host smoke test; module endpoint tests will be added per module
docs/
  srs-open-questions.md
```

Empty folders contain a `.gitkeep` so the structure is versioned. Remove it when you add the first real file.

## Modules (from the SRS)

| Module | Owns | SRS |
|---|---|---|
| **Identity** | registration, login, OTP, passwords, Google/Apple sign-in, Quick Login boundary | UC002-UC006, UC008 |
| **Users** | profiles, account types (Child/Teen/Adult, Parent flag), status, QR identity | UC007 |
| **Family** | add dependent, QR link/unlink, family view and rules | UC013-UC017 |
| **Notifications** | notification centre; email, SMS, push delivery | UC020, section 4 |
| **Cms** | exhibitions, galleries, gardens, home, About ilmi, ilmi Updates, CMS integration boundary | UC009-UC011, UC018 |
| **Support** | Contact Us, tickets, attachments | UC019 |
| **Settings** | language, accessibility, configurable values | UC001 |

Onboarding (UC012) is mostly CMS-driven content and is expected to live in **Cms**; confirm when that module is designed.

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

1. A module may use another module **only through that module's `Contracts` project**. Never its Domain, Application or Infrastructure.
2. A module owns its data: its own database **schema** (e.g. `users`), no reading another module's tables, no cross-schema foreign keys.
3. Controllers (Presentation) contain no business logic; they translate HTTP to Application use cases.
4. Prefer synchronous calls through Contracts interfaces; use domain/integration events (in-process) where one module must react to another.

`tests/Ilmi.ArchitectureTests` reads every `.csproj` and **fails the build** when any rule is broken (verified by temporarily adding a
forbidden reference: 2 tests failed, then passed after reverting).

### Why this shape
- **Contracts as a separate project per module** is the only way to make "talk through the public surface" compiler-enforced rather than a convention.
- **One shared database, one schema per module:** simple to run and deploy now; a module can be moved to its own database later because it owns its tables.
- **Two hosts:** the API stays stateless and horizontally scalable; scheduled work (SRS GSR019/GSR020, 24-hourly, configurable) runs in the Worker.
- 40 projects is more ceremony than a small app needs; it is the price of enforced boundaries. If it proves heavy, Contracts and Presentation can be merged per module without changing the rules.

## Technology (chosen, not yet used in code)

| Concern | Choice | Where it is referenced |
|---|---|---|
| Web API | ASP.NET Core 10 | Host/Ilmi.Api, Presentation projects |
| Persistence | EF Core 10 + SQL Server | each module's Infrastructure |
| Validation | FluentValidation 12 | each module's Application |
| API docs | Swashbuckle (Swagger/OpenAPI) | Host/Ilmi.Api |
| Background jobs | .NET Generic Host `BackgroundService` | Host/Ilmi.Worker |
| Tests | xUnit, Microsoft.AspNetCore.Mvc.Testing | tests/ |

All packages are pinned to the .NET 10 line (the newest "latest" NuGet packages target .NET 11).

## Build and test

```powershell
dotnet build Ilmi.sln
dotnet test Ilmi.sln
```

Nothing else is required to run yet: there is no database, no configuration and no secrets. Planned configuration approach for later:
connection strings and signing keys come from user-secrets / environment variables, never from committed files.

## Adding functionality (when you are ready)

1. Pick a module; read its README and the SRS use cases it owns.
2. Start in Domain, then Application (use case + ports), then Infrastructure (adapter), then Presentation (controller).
3. Register the module in the host (one `Add<Module>Module()` per module, to be added in each module's Presentation/Infrastructure).
4. If another module needs something, add it to this module's **Contracts**, not by adding a reference.
5. Run `dotnet test`; the architecture tests will tell you if a boundary was crossed.

## Suggested order
Resolve the open SRS questions first (3, 5 and 7 are still open), then **Settings/Users foundations → Identity → Notifications → Family → Cms → Support**.
Cross-cutting items to design before the first endpoint: response envelope and error handling, authentication/JWT, per-module DbContext and
migrations, the scheduled-job approach, logging with PII masking, and load testing for the SRS performance targets (NFR001-NFR003).
