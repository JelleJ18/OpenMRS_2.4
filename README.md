# OpenMRS Communicatiemodule

Een SaaS communicatiemodule die afspraaknotificaties verstuurt namens OpenMRS-organisaties, via een externe messaging provider.

## Architectuur

```
OpenMRS (2.7.x+)
     |  FHIR R4 REST / webhook
     v
CommunicationModule.Api          (ASP.NET Core Web API)
     |
     v
CommunicationModule.Core         (Domeinmodellen, interfaces, bedrijfslogica)
     |
     v
CommunicationModule.Infrastructure  (EF Core, provider adapter, secrets)
     |
     v
Messaging Provider (bijv. SwiftSend)
     |
     v
Telefoon van de patiënt
```

## Projecten

| Project | Type | Doel |
|---|---|---|
| `CommunicationModule.Api` | ASP.NET Core Web API | HTTP-toegangspunt, FHIR-endpoint, ontvangt afspraken van OpenMRS |
| `CommunicationModule.Core` | Class Library | Domeinmodellen, interfaces, bedrijfslogica, planning |
| `CommunicationModule.Infrastructure` | Class Library | EF Core databasetoegang, messaging provider adapter, secretbeheer |
| `CommunicationModule.Dashboard` | Blazor Web App | Real-time monitoringsdashboard |
| `CommunicationModule.Tests` | xUnit | Unit- en integratietests |

## Om te starten

### Vereisten
- .NET 9 SDK
- Docker Desktop
- Toegang tot de AWS MySQL/RDS database

### API starten
```bash
dotnet run --project src/CommunicationModule.Api
```

### Dashboard starten
```bash
dotnet run --project src/CommunicationModule.Dashboard
```

### Tests uitvoeren
```bash
dotnet test
```

### Met Docker draaien
Kopieer `.env.example` naar `.env` en vul de productie-connection string in.
Vul daar ook `Crypto__Key` in; user secrets worden niet meegenomen in Docker-containers.

```bash
docker compose up --build
```

Daarna draait de API op `http://localhost:5079` en de dashboard op `http://localhost:5080`.

### CI/CD
- CI: `.github/workflows/ci.yml` bouwt en test bij pull requests en pushes naar `main`.
- CD: `.github/workflows/publish-images.yml` bouwt en publiceert Docker-images naar GHCR.
- Runtime secrets komen uit GitHub Secrets of je deployment environment, niet uit `appsettings.json`.

## Belangrijkste technologieën
- **ASP.NET Core 9** — Web API
- **Entity Framework Core 9** — Database ORM
- **Hangfire** — Achtergrondtaken voor notificatieplanning (T-24u / T-1u)
- **HL7 FHIR R4** — Standaard voor afspraakgegevens
- **OpenTelemetry** — Gedistribueerde tracing en metrics
- **Blazor** — Dashboard frontend

## Beveiliging
- AES-256 versleuteling voor opslag
- TLS 1.3 voor transport
- Inloggegevens worden opgeslagen via omgevingsvariabelen of Azure Key Vault — nooit in code of configuratiebestanden
- Geen persoonsgegevens (PII) in logbestanden

## OpenMRS koppeling
- OpenMRS 2.7+ stuurt een FHIR Appointment naar de API.
- De standaard is webhook; polling is alleen een fallback.
- Beheerders hebben de API-URL, de instance-ID en de access key nodig.
