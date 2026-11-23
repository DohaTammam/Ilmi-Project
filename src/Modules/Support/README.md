# Support module

Contact Us, support tickets and attachments (SRS UC019).

| Project | Layer | May reference |
|---|---|---|
| `Ilmi.Modules.Support.Contracts` | Public surface for other modules (DTOs, service interfaces) | nothing |
| `Ilmi.Modules.Support.Domain` | Entities, value objects, enums, events, exceptions | SharedKernel |
| `Ilmi.Modules.Support.Application` | Use cases (commands/queries), ports, validators | own Domain, own Contracts, SharedKernel, other modules' **Contracts** |
| `Ilmi.Modules.Support.Infrastructure` | EF Core (own schema `support`), repositories, external services | own Application |
| `Ilmi.Modules.Support.Presentation` | Controllers, request/response models | own Application, own Contracts |

Status: structure only, no implementation.

