namespace CommunicationModule.Core.DTOs;

public class NotificationMessage
{
    public Guid NotificationJobId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string MessageBody { get; set; } = string.Empty;
}
