using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunicationModule.Core.Events;
using CommunicationModule.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CommunicationModule.Infrastructure.Services;

public class EventPublisher : IEventPublisher
{
    private readonly IServiceProvider _provider;
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(IServiceProvider provider, ILogger<EventPublisher> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task PublishAsync(IIntegrationEvent evt, CancellationToken cancellationToken = default)
    {
        if (evt is null) return;

        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(evt.GetType());

        var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);
        var resolved = _provider.GetService(enumerableType) as IEnumerable;

        if (resolved is not null)
        {
            var tasks = new List<Task>();
            foreach (var h in resolved)
            {
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        var method = h.GetType().GetMethod("HandleAsync");
                        if (method is null) return;
                        var t = (Task)method.Invoke(h, new object[] { evt, cancellationToken })!;
                        await t.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Event handler threw an exception for event {EventType}", evt.GetType().Name);
                    }
                }));
            }

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
}
