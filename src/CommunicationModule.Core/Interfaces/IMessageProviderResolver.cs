namespace CommunicationModule.Core.Interfaces;

public interface IMessageProviderResolver
{
    IMessagingProvider Resolve(string providerName);
}