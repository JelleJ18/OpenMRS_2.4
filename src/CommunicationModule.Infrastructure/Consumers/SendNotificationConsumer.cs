using CommunicationModule.Core.Contracts;
using CommunicationModule.Core.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace CommunicationModule.Infrastructure.Consumers;

public class SendNotificationConsumer : IConsumer<SendNotificationCommand>
{
    private readonly IMessageProviderResolver _resolver;
    private readonly ILogger<SendNotificationConsumer> _logger;

    public SendNotificationConsumer(
        IMessageProviderResolver resolver,
        ILogger<SendNotificationConsumer> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SendNotificationCommand> context)
    {
        var command = context.Message;

        _logger.LogInformation(
            "Processing notification {NotificationJobId} via {Provider}",
            command.NotificationJobId, command.ProviderName);

        var provider = _resolver.Resolve(command.ProviderName);

        var result = await provider.SendAsync(new Core.DTOs.NotificationMessage
        {
            NotificationJobId = command.NotificationJobId,
            PhoneNumber = command.PhoneNumber,
            MessageBody = command.MessageBody
        }, context.CancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning(
                "Failed to send notification {NotificationJobId}: {Error}",
                command.NotificationJobId, result.ErrorMessage);

            throw new Exception(result.ErrorMessage);
        }

        _logger.LogInformation(
            "Notification {NotificationJobId} sent successfully via {ProviderMessageId}",
            command.NotificationJobId, result.ProviderMessageId);
    }
}