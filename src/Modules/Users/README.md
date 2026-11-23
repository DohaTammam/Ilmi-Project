# Users module

User profiles, account types (Child/Teen/Adult, Parent flag), account status, QR identity (SRS UC007).

| Project | Layer | May reference |
|---|---|---|
| `Ilmi.Modules.Users.Contracts` | Public surface for other modules (DTOs, service interfaces) | nothing |
| `Ilmi.Modules.Users.Domain` | Entities, value objects, enums, events, exceptions | SharedKernel |
| `Ilmi.Modules.Users.Application` | Use cases (commands/queries), ports, validators | own Domain, own Contracts, SharedKernel, other modules' **Contracts** |
| `Ilmi.Modules.Users.Infrastructure` | EF Core (own schema `users`), repositories, external services | own Application |
| `Ilmi.Modules.Users.Presentation` | Controllers, request/response models | own Application, own Contracts |

Status: structure only, no implementation.

