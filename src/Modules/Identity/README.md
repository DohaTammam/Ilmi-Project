# Identity module

Registration, login, OTP verification, password management, Google/Apple sign-in, Quick Login boundary (SRS UC002-UC006, UC008).

| Project | Layer | May reference |
|---|---|---|
| `Ilmi.Modules.Identity.Contracts` | Public surface for other modules (DTOs, service interfaces) | nothing |
| `Ilmi.Modules.Identity.Domain` | Entities, value objects, enums, events, exceptions | SharedKernel |
| `Ilmi.Modules.Identity.Application` | Use cases (commands/queries), ports, validators | own Domain, own Contracts, SharedKernel, other modules' **Contracts** |
| `Ilmi.Modules.Identity.Infrastructure` | EF Core (own schema `identity`), repositories, external services | own Application |
| `Ilmi.Modules.Identity.Presentation` | Controllers, request/response models | own Application, own Contracts |

Status: structure only, no implementation.

