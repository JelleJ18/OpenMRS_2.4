using System.Diagnostics.Metrics;

namespace CommunicationModule.Api.Services;

public static class Telemetry
{
    public const string MeterName = "CommunicationModule.Api";

    public static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> Hl7MessagesReceived = Meter.CreateCounter<long>(
        "communicationmodule_hl7_messages_received",
        description: "Total number of HL7 messages received.");

    public static readonly Counter<long> Hl7MessagesFailed = Meter.CreateCounter<long>(
        "communicationmodule_hl7_messages_failed",
        description: "Total number of HL7 messages that failed during validation or mapping.");

    public static readonly Counter<long> AppointmentsIngested = Meter.CreateCounter<long>(
        "communicationmodule_appointments_ingested",
        description: "Total number of appointments accepted into the database.");

    public static readonly Counter<long> NotificationJobsScheduled = Meter.CreateCounter<long>(
        "communicationmodule_notification_jobs_scheduled",
        description: "Total number of notification jobs scheduled.");

    public static readonly Counter<long> NotificationJobsSkipped = Meter.CreateCounter<long>(
        "communicationmodule_notification_jobs_skipped",
        description: "Total number of notification jobs skipped because the appointment already started.");

    public static readonly Counter<long> NotificationJobsSent = Meter.CreateCounter<long>(
        "communicationmodule_notification_jobs_sent",
        description: "Total number of notification jobs sent successfully.");

    public static readonly Counter<long> NotificationJobsFailed = Meter.CreateCounter<long>(
        "communicationmodule_notification_jobs_failed",
        description: "Total number of notification jobs that failed.");

    public static readonly Histogram<double> Hl7ParseDuration = Meter.CreateHistogram<double>(
        "communicationmodule_hl7_parse_duration_seconds",
        unit: "s",
        description: "Time spent parsing raw HL7 messages.");

    public static readonly Histogram<double> Hl7MappingDuration = Meter.CreateHistogram<double>(
        "communicationmodule_hl7_mapping_duration_seconds",
        unit: "s",
        description: "Time spent mapping HL7 messages to FHIR.");

    public static readonly Histogram<double> AppointmentIngestDuration = Meter.CreateHistogram<double>(
        "communicationmodule_appointment_ingest_duration_seconds",
        unit: "s",
        description: "Time spent ingesting appointments into the database.");

    public static readonly Histogram<double> NotificationDispatchDuration = Meter.CreateHistogram<double>(
        "communicationmodule_notification_dispatch_duration_seconds",
        unit: "s",
        description: "Time spent dispatching notification jobs.");
}