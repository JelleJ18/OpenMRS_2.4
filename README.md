# OpenMRS Communication Module

A SaaS communication module that sends appointment notifications to patients on behalf of OpenMRS organisations, via external messaging providers.

## Architecture

```
OpenMRS (2.7.x+)
     |  FHIR R4 REST / webhook
     v
CommunicationModule.Api        (ASP.NET Core Web API)
     |
     v
CommunicationModule.Core       (Business logic, scheduling, interfaces)
     |
     v
CommunicationModule.Infrastructure  (EF Core, provider adapters, secrets)
     |
     v
Messaging Providers (SwiftSend / LegacyLink / AsyncFlow / SecurePost)
     |
     v
Patient's phone
```

## Projects

| Project | Type | Purpose |
|---|---|---|
| `CommunicationModule.Api` | ASP.NET Core Web API | HTTP entry point, FHIR endpoint, receives appointments from OpenMRS |
| `CommunicationModule.Core` | Class Library | Domain models, interfaces, business logic, scheduling |
| `CommunicationModule.Infrastructure` | Class Library | EF Core DB access, messaging provider adapters, secrets management |
| `CommunicationModule.Dashboard` | Blazor Web App | Real-time monitoring dashboard |
| `CommunicationModule.Tests` | xUnit | Unit and integration tests |

## Getting Started

### Prerequisites
- .NET 9 SDK
- SQL Server (or SQL Server LocalDB for development)

### Run the API
```bash
dotnet run --project src/CommunicationModule.Api
```

### Run the Dashboard
```bash
dotnet run --project src/CommunicationModule.Dashboard
```

### Run Tests
```bash
dotnet test
```

## Key Technologies
- **ASP.NET Core 9** — Web API
- **Entity Framework Core 9** — Database ORM
- **Hangfire** — Background job scheduling (notifications at T-24h / T-1h)
- **HL7 FHIR R4** — Appointment data standard
- **OpenTelemetry** — Distributed tracing and metrics
- **Blazor** — Dashboard frontend

## Security
- AES-256 encryption at rest
- TLS 1.3 in transit
- Credentials stored in environment variables / Azure Key Vault — never in code or config files
- No PII in log files
