# OpenMRS Communication Module — Implementation Plan

## What we're building

A **SaaS service** that sits between OpenMRS (the hospital system) and patients' phones. OpenMRS tells it "there's an appointment", and it handles sending reminders at 24h and 1h before — using whichever messaging provider the hospital has subscribed to.

---

## Phase 1 — Foundation

### 1. Project setup
C# solution with the following projects:
- `CommunicationModule.Api` — HTTP API that OpenMRS talks to
- `CommunicationModule.Core` — business logic (scheduling, retry, provider routing)
- `CommunicationModule.Infrastructure` — database, messaging provider clients, secrets
- `CommunicationModule.Dashboard` — real-time monitoring UI (Blazor or React)

### 2. Database schema (SQL)
| Table | Purpose |
|---|---|
| `Organisations` | Multi-tenant: each hospital is a tenant |
| `ProviderSubscriptions` | Which provider a hospital uses (credentials AES-256 encrypted) |
| `NotificationJobs` | Scheduled notifications (appointment ID, send time, status) |
| `MessageLogs` | Outcome of each send attempt — **no PII**, only message ID, timestamp, status, provider |

### 3. OpenMRS integration
- OpenMRS 2.7.x exposes a **FHIR R4 REST API**
- The API layer receives a **FHIR Appointment resource** from OpenMRS (via webhook or polling)
- Validate the incoming FHIR message and return an **ACK** (acknowledgement)

---

## Phase 2 — Core Logic

### 4. Scheduling engine
- When an appointment is received, schedule two jobs: **T-24h** and **T-1h**
- Before sending: check if appointment has already started — if yes, skip
- Use **Hangfire** (C# background job library) to persist and execute scheduled jobs

### 5. Provider abstraction — Strategy pattern
Create an `IMessagingProvider` interface:
```csharp
public interface IMessagingProvider
{
    Task<SendResult> SendAsync(NotificationMessage message);
}
```
Implement it four times:
- `SwiftSendProvider`
- `LegacyLinkProvider`
- `AsyncFlowProvider`
- `SecurePostProvider`

The Core layer never knows which provider is active — it only calls the interface. The correct provider is selected based on the organisation's subscription.

> **Why Strategy pattern?** Different hospitals use different providers. This lets you swap providers per organisation without changing core logic.

### 6. Retry & fallback mechanism
- On send failure: retry with **exponential backoff** (1 min → 2 min → 4 min → ...)
- Log each attempt (without PII)
- After max retries: mark as failed, raise alert
- Handle OpenMRS downtime independently (the module is self-contained)

---

## Phase 3 — Security

### 7. Secrets management
- Provider credentials go in **environment variables** or a secrets vault (Azure Key Vault / HashiCorp Vault)
- **Never** store credentials in `appsettings.json` or source code
- Encrypt sensitive DB fields (credentials, tokens) with **AES-256**

### 8. Transport security
- All HTTP endpoints enforce **TLS 1.3**
- Authenticate the OpenMRS → API connection via **API key per organisation** or OAuth2

---

## Phase 4 — Observability & Dashboard

### 9. OpenTelemetry
- Add distributed tracing and metrics to the C# service
- Export to: **Jaeger** (traces) and **Prometheus** (metrics)

### 10. Dashboard
- Per-organisation view: messages sent / failed / pending
- Throughput and error rate graphs
- Real-time updates via **SignalR** or polling

---

## Phase 5 — Data Retention

### 11. Automated cleanup jobs
- Scheduled job: **delete patient/appointment data within 14 days** of communication
- **Meta-information** (no PII) retained for up to **1 year** for billing verification
- Logs must **never** contain PII (patient name, BSN, phone number, etc.)

---

## Recommended coding order

1. Set up the C# solution structure
2. Design and create the database schema
3. Build the FHIR API endpoint (receive appointment from OpenMRS)
4. Build the scheduling engine with Hangfire
5. Implement `IMessagingProvider` + one mock provider for testing
6. Add security (secrets vault, AES-256 encryption, TLS)
7. Add retry/fallback logic
8. Build the dashboard
9. Instrument with OpenTelemetry
10. Add data retention cleanup jobs

---

## Architecture overview

```
OpenMRS (2.7.x+)
     |
     |  FHIR R4 REST / webhook
     v
+--------------------+
|   API Layer        |  <-- receives Appointment, validates, sends ACK
+--------------------+
          |
          v
+--------------------+     +------------------+
|  C# Core Service   | <-> |   SQL Database   |
|  - Scheduler       |     |   - Jobs         |
|  - Provider router |     |   - Logs (no PII)|
|  - Retry engine    |     |   - Org/Subs     |
+--------------------+     +------------------+
          |                        |
          |                        v
          |               +------------------+
          |               |   Dashboard      |
          |               |  (monitoring)    |
          |               +------------------+
          |
          v
+-----------------------------+
|       Provider Layer        |
|  SwiftSend / LegacyLink /   |
|  AsyncFlow / SecurePost     |
+-----------------------------+
          |
          v
   Patient's phone
```

---

## Security checklist

- [ ] AES-256 encryption for all sensitive data at rest
- [ ] TLS 1.3 for all transport
- [ ] No credentials/tokens in code or config files
- [ ] No PII in log files
- [ ] Multi-tenant credential isolation
- [ ] API authentication per organisation

## Requirements checklist

- [ ] Notifications at T-24h and T-1h
- [ ] Skip notification if appointment already started
- [ ] Patient can cancel appointment
- [ ] Log success/failure per notification
- [ ] FHIR R4 compliant (Appointment, Patient, Practitioner, Location resources)
- [ ] ACK messages implemented
- [ ] Retry/fallback mechanism documented and implemented
- [ ] Multi-charset (Unicode) support
- [ ] OpenTelemetry monitoring
- [ ] Real-time dashboard
- [ ] Patient data deleted within 14 days
- [ ] Meta-info retained up to 1 year (no PII)
