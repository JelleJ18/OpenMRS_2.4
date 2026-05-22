using System.Threading;
using System.Threading.Tasks;
using CommunicationModule.Core.Events;

namespace CommunicationModule.Core.Interfaces;

public interface IIntegrationEventHandler<TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent evt, CancellationToken cancellationToken = default);
}
