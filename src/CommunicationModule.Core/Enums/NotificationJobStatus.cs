namespace CommunicationModule.Core.Enums;

public enum NotificationJobStatus
{
    Pending,
    Sent,
    Failed,
    Skipped,   // appointment already started when job ran
    Cancelled  // appointment was cancelled before job ran
}
