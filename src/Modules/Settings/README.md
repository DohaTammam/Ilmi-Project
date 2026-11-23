# Settings module

Language (AR/EN), accessibility settings, configurable system values (SRS UC001).

| Project | Layer | May reference |
|---|---|---|
| `Ilmi.Modules.Settings.Contracts` | Public surface for other modules (DTOs, service interfaces) | nothing |
| `Ilmi.Modules.Settings.Domain` | Entities, value objects, enums, events, exceptions | SharedKernel |
| `Ilmi.Modules.Settings.Application` | Use cases (commands/queries), ports, validators | own Domain, own Contracts, SharedKernel, other modules' **Contracts** |
| `Ilmi.Modules.Settings.Infrastructure` | EF Core (own schema `settings`), repositories, external services | own Application |
| `Ilmi.Modules.Settings.Presentation` | Controllers, request/response models | own Application, own Contracts |

Status: structure only, no implementation.

