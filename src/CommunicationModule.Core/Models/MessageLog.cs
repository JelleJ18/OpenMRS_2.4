namespace CommunicationModule.Core.Models;

// No PII here — patient name, phone, and appointment details are never stored in logs
public class MessageLog
{
    public Guid Id { get; set; }

    public Guid NotificationJobId { get; set; }
    public Guid OrganisationId { get; set; }

    public string ProviderName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ErrorMessage { get; set; }

    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;
}
