using CommunicationModule.Core.DTOs;

namespace CommunicationModule.Core.Interfaces;

public interface IMessagingProvider
{
    string ProviderName { get; }
    Task<SendResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default);
}
