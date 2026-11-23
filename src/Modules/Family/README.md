# Family module

Add dependent, link/unlink family profiles by QR, view family, family business rules (SRS UC013-UC017).

| Project | Layer | May reference |
|---|---|---|
| `Ilmi.Modules.Family.Contracts` | Public surface for other modules (DTOs, service interfaces) | nothing |
| `Ilmi.Modules.Family.Domain` | Entities, value objects, enums, events, exceptions | SharedKernel |
| `Ilmi.Modules.Family.Application` | Use cases (commands/queries), ports, validators | own Domain, own Contracts, SharedKernel, other modules' **Contracts** |
| `Ilmi.Modules.Family.Infrastructure` | EF Core (own schema `family`), repositories, external services | own Application |
| `Ilmi.Modules.Family.Presentation` | Controllers, request/response models | own Application, own Contracts |

Status: structure only, no implementation.

