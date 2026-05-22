using System.Net.Http.Json;
using CommunicationModule.Core.DTOs;
using CommunicationModule.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace CommunicationModule.Infrastructure.Providers.AsyncFlow;

public class AsyncFlowProvider : IMessagingProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AsyncFlowProvider> _logger;

    public string ProviderName => "AsyncFlow";

    public AsyncFlowProvider(HttpClient httpClient, ILogger<AsyncFlowProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SendResult> SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Submit het bericht
            var response = await _httpClient.PostAsJsonAsync("asyncflow", new
            {
                destination = message.PhoneNumber,
                content = message.MessageBody,
                priority = "normal"
            }, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new SendResult
                {
                    Success = false,
                    ProviderMessageId = ProviderName,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}"
                };
            }

            var submitted = await response.Content.ReadFromJsonAsync<AsyncFlowSubmitResponse>(
                cancellationToken: cancellationToken);

            if (submitted?.TrackingId == null)
            {
                return new SendResult
                {
                    Success = false,
                    ProviderMessageId = ProviderName,
                    ErrorMessage = "No tracking ID received"
                };
            }

            _logger.LogInformation("AsyncFlow message queued with trackingId {TrackingId}",
                submitted.TrackingId);

            // Poll totdat Completed of Failed
            return await PollStatusAsync(submitted.TrackingId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message via AsyncFlow");
            return new SendResult
            {
                Success = false,
                ProviderMessageId = ProviderName,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<SendResult> PollStatusAsync(
        string trackingId,
        CancellationToken cancellationToken)
    {
        var maxAttempts = 20;
        var delay = TimeSpan.FromSeconds(3);

        for (int i = 0; i < maxAttempts; i++)
        {
            await Task.Delay(delay, cancellationToken);

            var statusResponse = await _httpClient.GetFromJsonAsync<AsyncFlowStatusResponse>(
                $"asyncflow/{trackingId}",
                cancellationToken);

            _logger.LogInformation("AsyncFlow status for {TrackingId}: {Status}",
                trackingId, statusResponse?.Status);

            switch (statusResponse?.Status)
            {
                case "Completed":
                    return new SendResult
                    {
                        Success = true,
                        ProviderMessageId = trackingId
                    };
                case "Failed":
                    return new SendResult
                    {
                        Success = false,
                        ProviderMessageId = trackingId,
                        ErrorMessage = statusResponse.ErrorDetails ?? "Processing failed"
                    };
            }
            // Queued of Processing, blijf pollen
        }

        return new SendResult
        {
            Success = false,
            ProviderMessageId = trackingId,
            ErrorMessage = "Timeout waiting for AsyncFlow to process message"
        };
    }
}

file class AsyncFlowSubmitResponse
{
    public bool Accepted { get; set; }
    public string? TrackingId { get; set; }
}

file class AsyncFlowStatusResponse
{
    public string? TrackingId { get; set; }
    public string? Status { get; set; }
    public string? ErrorDetails { get; set; }
}