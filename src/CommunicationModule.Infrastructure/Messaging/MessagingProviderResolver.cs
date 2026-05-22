using CommunicationModule.Core.Interfaces;

namespace CommunicationModule.Infrastructure.Messaging;

public class MessagingProviderResolver
    : IMessageProviderResolver
{
    private readonly IEnumerable<IMessagingProvider> _providers;

    public MessagingProviderResolver(
        IEnumerable<IMessagingProvider> providers)
    {
        _providers = providers;
    }

    public IMessagingProvider Resolve(string providerName)
    {
        var provider = _providers.FirstOrDefault(p =>
            p.ProviderName.Equals(
                providerName,
                StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            throw new InvalidOperationException(
                $"Provider '{providerName}' not found.");
        }

        return provider;
    }
}