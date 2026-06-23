# Monitoring

This project uses OpenTelemetry metrics in the API, Prometheus for scraping, and Grafana for visualization.

## Start

Run the stack from the repository root:

```bash
docker compose up --build
```

## Endpoints

- API metrics scrape endpoint: `http://localhost:5079/metrics`
- Prometheus UI: `http://localhost:9090`
- Grafana UI: `http://localhost:3000`

## Grafana login

- Username: `admin`
- Password: `admin`

## Data source

Grafana is provisioned with a Prometheus data source at `http://prometheus:9090`.

## Useful metrics

- `communicationmodule_hl7_messages_received_total`
- `communicationmodule_hl7_messages_failed_total`
- `communicationmodule_appointments_ingested_total`
- `communicationmodule_notification_jobs_scheduled_total`
- `communicationmodule_notification_jobs_sent_total`
- `communicationmodule_notification_jobs_failed_total`
- `communicationmodule_hl7_parse_duration_seconds`
- `communicationmodule_hl7_mapping_duration_seconds`
- `communicationmodule_appointment_ingest_duration_seconds`
- `communicationmodule_notification_dispatch_duration_seconds`
