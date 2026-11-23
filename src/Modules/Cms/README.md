# Cms module

CMS / Explore: exhibitions, galleries, gardens, home page, About ilmi, ilmi Updates content, CMS integration boundary (SRS UC009-UC011, UC018).

| Project | Layer | May reference |
|---|---|---|
| `Ilmi.Modules.Cms.Contracts` | Public surface for other modules (DTOs, service interfaces) | nothing |
| `Ilmi.Modules.Cms.Domain` | Entities, value objects, enums, events, exceptions | SharedKernel |
| `Ilmi.Modules.Cms.Application` | Use cases (commands/queries), ports, validators | own Domain, own Contracts, SharedKernel, other modules' **Contracts** |
| `Ilmi.Modules.Cms.Infrastructure` | EF Core (own schema `cms`), repositories, external services | own Application |
| `Ilmi.Modules.Cms.Presentation` | Controllers, request/response models | own Application, own Contracts |

Status: structure only, no implementation.

