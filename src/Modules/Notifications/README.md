# Notifications module

In-app notification centre, email, SMS and push delivery, read/unread state (SRS UC020, section 4).

| Project | Layer | May reference |
|---|---|---|
| `Ilmi.Modules.Notifications.Contracts` | Public surface for other modules (DTOs, service interfaces) | nothing |
| `Ilmi.Modules.Notifications.Domain` | Entities, value objects, enums, events, exceptions | SharedKernel |
| `Ilmi.Modules.Notifications.Application` | Use cases (commands/queries), ports, validators | own Domain, own Contracts, SharedKernel, other modules' **Contracts** |
| `Ilmi.Modules.Notifications.Infrastructure` | EF Core (own schema `notifications`), repositories, external services | own Application |
| `Ilmi.Modules.Notifications.Presentation` | Controllers, request/response models | own Application, own Contracts |

Status: structure only, no implementation.

