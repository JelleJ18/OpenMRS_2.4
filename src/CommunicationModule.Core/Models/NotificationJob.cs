using CommunicationModule.Core.Enums;

namespace CommunicationModule.Core.Models;

public class NotificationJob
{
    public Guid Id { get; set; }

    public Guid AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;

    public NotificationJobType Type { get; set; }
    public NotificationJobStatus Status { get; set; } = NotificationJobStatus.Pending;

    public DateTime ScheduledFor { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int RetryCount { get; set; } = 0;
}
