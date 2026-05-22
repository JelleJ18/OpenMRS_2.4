using CommunicationModule.Core.DTOs;
using CommunicationModule.Core.Interfaces;

namespace CommunicationModule.Core.Services;

public class NotificationService
{
    private readonly IMessageProviderResolver _resolver;

    public NotificationService(
        IMessageProviderResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<SendResult> SendAsync(
        string providerName,
        NotificationMessage message)
    {
        var provider = _resolver.Resolve(providerName);

        return await provider.SendAsync(message);
    }
}