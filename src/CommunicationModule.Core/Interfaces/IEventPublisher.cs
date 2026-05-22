using System.Threading;
using System.Threading.Tasks;
using CommunicationModule.Core.Events;

namespace CommunicationModule.Core.Interfaces;

public interface IEventPublisher
{
    Task PublishAsync(IIntegrationEvent evt, CancellationToken cancellationToken = default);
}
