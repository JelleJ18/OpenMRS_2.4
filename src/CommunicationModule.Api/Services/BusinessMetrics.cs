using System.Diagnostics;
using System.Text;
using System.Threading;

namespace CommunicationModule.Api.Services;

public static class BusinessMetrics
{
    private static long _hl7Received;
    private static long _hl7Failed;
    private static long _appointmentsIngested;
    private static long _notificationJobsScheduled;
    private static long _notificationJobsSent;
    private static long _notificationJobsFailed;
    private static long _notificationJobsSkipped;

    private static long _hl7ParseDurationTicks;
    private static long _hl7ParseDurationCount;
    private static long _hl7MappingDurationTicks;
    private static long _hl7MappingDurationCount;
    private static long _appointmentIngestDurationTicks;
    private static long _appointmentIngestDurationCount;
    private static long _notificationDispatchDurationTicks;
    private static long _notificationDispatchDurationCount;

    public static void IncrementHl7Received() => Interlocked.Increment(ref _hl7Received);
    public static void IncrementHl7Failed() => Interlocked.Increment(ref _hl7Failed);
    public static void IncrementAppointmentsIngested() => Interlocked.Increment(ref _appointmentsIngested);
    public static void IncrementNotificationJobsScheduled() => Interlocked.Increment(ref _notificationJobsScheduled);
    public static void IncrementNotificationJobsSent() => Interlocked.Increment(ref _notificationJobsSent);
    public static void IncrementNotificationJobsFailed() => Interlocked.Increment(ref _notificationJobsFailed);
    public static void IncrementNotificationJobsSkipped() => Interlocked.Increment(ref _notificationJobsSkipped);

    public static void RecordHl7ParseDuration(double seconds) => RecordDuration(ref _hl7ParseDurationTicks, ref _hl7ParseDurationCount, seconds);
    public static void RecordHl7MappingDuration(double seconds) => RecordDuration(ref _hl7MappingDurationTicks, ref _hl7MappingDurationCount, seconds);
    public static void RecordAppointmentIngestDuration(double seconds) => RecordDuration(ref _appointmentIngestDurationTicks, ref _appointmentIngestDurationCount, seconds);
    public static void RecordNotificationDispatchDuration(double seconds) => RecordDuration(ref _notificationDispatchDurationTicks, ref _notificationDispatchDurationCount, seconds);

    public static string GetPrometheusText()
    {
        var sb = new StringBuilder();
        AppendCounter(sb, "communicationmodule_hl7_received_total", Interlocked.Read(ref _hl7Received), "Total HL7 messages received.");
        AppendCounter(sb, "communicationmodule_hl7_failed_total", Interlocked.Read(ref _hl7Failed), "Total HL7 messages that failed.");
        AppendCounter(sb, "communicationmodule_appointments_ingested_total", Interlocked.Read(ref _appointmentsIngested), "Total appointments ingested.");
        AppendCounter(sb, "communicationmodule_notification_jobs_scheduled_total", Interlocked.Read(ref _notificationJobsScheduled), "Total notification jobs scheduled.");
        AppendCounter(sb, "communicationmodule_notification_jobs_sent_total", Interlocked.Read(ref _notificationJobsSent), "Total notification jobs sent.");
        AppendCounter(sb, "communicationmodule_notification_jobs_failed_total", Interlocked.Read(ref _notificationJobsFailed), "Total notification jobs failed.");
        AppendCounter(sb, "communicationmodule_notification_jobs_skipped_total", Interlocked.Read(ref _notificationJobsSkipped), "Total notification jobs skipped.");

        AppendGauge(sb, "communicationmodule_hl7_parse_duration_seconds_avg", GetAverageSeconds(_hl7ParseDurationTicks, _hl7ParseDurationCount), "Average HL7 parse duration in seconds.");
        AppendGauge(sb, "communicationmodule_hl7_mapping_duration_seconds_avg", GetAverageSeconds(_hl7MappingDurationTicks, _hl7MappingDurationCount), "Average HL7 mapping duration in seconds.");
        AppendGauge(sb, "communicationmodule_appointment_ingest_duration_seconds_avg", GetAverageSeconds(_appointmentIngestDurationTicks, _appointmentIngestDurationCount), "Average appointment ingest duration in seconds.");
        AppendGauge(sb, "communicationmodule_notification_dispatch_duration_seconds_avg", GetAverageSeconds(_notificationDispatchDurationTicks, _notificationDispatchDurationCount), "Average notification dispatch duration in seconds.");

        return sb.ToString();
    }

    private static void RecordDuration(ref long totalTicks, ref long count, double seconds)
    {
        var ticks = (long)(seconds * Stopwatch.Frequency);
        Interlocked.Add(ref totalTicks, ticks);
        Interlocked.Increment(ref count);
    }

    private static double GetAverageSeconds(long totalTicks, long count)
        => count == 0 ? 0.0 : (double)totalTicks / Stopwatch.Frequency / count;

    private static void AppendCounter(StringBuilder sb, string name, long value, string help)
    {
        sb.AppendLine($"# TYPE {name} counter");
        sb.AppendLine($"# HELP {name} {help}");
        sb.AppendLine($"{name} {value}");
    }

    private static void AppendGauge(StringBuilder sb, string name, double value, string help)
    {
        sb.AppendLine($"# TYPE {name} gauge");
        sb.AppendLine($"# HELP {name} {help}");
        sb.AppendLine($"{name} {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }
}
