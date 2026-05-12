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

## Aan de slag

### Vereisten
- .NET 9 SDK
- SQL Server (of SQL Server LocalDB voor ontwikkeling)

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
