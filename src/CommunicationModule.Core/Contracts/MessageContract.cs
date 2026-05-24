namespace CommunicationModule.Core.Contracts;

public record SendNotificationCommand(
    Guid NotificationJobId,
    string PhoneNumber,
    string MessageBody,
    string ProviderName);