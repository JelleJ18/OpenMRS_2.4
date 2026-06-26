using System.Net.Http.Headers;
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
        CancellationToken ct = default)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();

            _httpClient.DefaultRequestHeaders.Add(
                "X-API-KEY",
                "asyncflow-api-key"); // later uit ProviderSubscription halen

            _httpClient.DefaultRequestHeaders.Add(
                "X-STUDENT-GROUP",
                "group-1");

            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.PostAsJsonAsync(
                "/asyncflow",
                new
                {
                    destination = message.PhoneNumber,
                    content = message.MessageBody,
                    priority = "normal"
                },
                ct);

            var submit = await response.Content.ReadFromJsonAsync<AsyncFlowSubmitResponse>(
                cancellationToken: ct);

            if (!response.IsSuccessStatusCode || submit?.Accepted != true)
            {
                return new SendResult
                {
                    Success = false,
                    ErrorMessage = $"HTTP {(int)response.StatusCode}"
                };
            }

            _logger.LogInformation(
                "AsyncFlow queued message {TrackingId}",
                submit.TrackingId);

            return await PollStatusAsync(submit.TrackingId!, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AsyncFlow error");

            return new SendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<SendResult> PollStatusAsync(
        string trackingId,
        CancellationToken ct)
    {
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(3000, ct);

            var status = await _httpClient.GetFromJsonAsync<AsyncFlowStatusResponse>(
                $"/asyncflow/{trackingId}",
                ct);

            if (status == null)
                continue;

            _logger.LogInformation(
                "AsyncFlow status: {Status}",
                status.Status);

            switch (status.Status)
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
                        ErrorMessage = status.ErrorDetails
                    };
            }

            // Queued / Processing -> opnieuw pollen
        }

        return new SendResult
        {
            Success = false,
            ProviderMessageId = trackingId,
            ErrorMessage = "Timeout waiting for AsyncFlow."
        };
    }
}

file class AsyncFlowSubmitResponse
{
    public bool Accepted { get; set; }
    public string? TrackingId { get; set; }
    public string? Message { get; set; }
    public DateTime SubmittedAt { get; set; }
}

file class AsyncFlowStatusResponse
{
    public string? TrackingId { get; set; }
    public string? Status { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorDetails { get; set; }
}