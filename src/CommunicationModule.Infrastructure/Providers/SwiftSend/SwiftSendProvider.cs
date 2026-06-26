using System.Net.Http.Headers;
using System.Net.Http.Json;
using CommunicationModule.Core.DTOs;
using CommunicationModule.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace CommunicationModule.Infrastructure.Providers.SwiftSend;

public class SwiftSendProvider : IMessagingProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SwiftSendProvider> _logger;
    private static readonly Random _random = new();

    public string ProviderName => "SwiftSend";

    public SwiftSendProvider(HttpClient httpClient, ILogger<SwiftSendProvider> logger)
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

            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "your-api-key-here");
            _httpClient.DefaultRequestHeaders.Add("X-STUDENT-GROUP", "group-1");
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            // =========================
            // 🔥 TEST FAILURE SIMULATIE
            // =========================

            var roll = _random.Next(1, 101);

            // 30% failure
            if (roll <= 30)
            {
                _logger.LogWarning("SwiftSend SIMULATED FAILURE (no HTTP call)");

                return new SendResult
                {
                    Success = false,
                    ErrorMessage = "Simulated SwiftSend failure"
                };
            }

            // 10% fake HTTP 500
            if (roll <= 40)
            {
                _logger.LogError("SwiftSend SIMULATED HTTP 500");

                return new SendResult
                {
                    Success = false,
                    ErrorMessage = "HTTP 500 simulated"
                };
            }

            // =========================
            // REAL CALL
            // =========================

            var response = await _httpClient.PostAsJsonAsync(
                "/swiftsend",
                new
                {
                    type = "SMS",
                    recipients = new[] { message.PhoneNumber },
                    content = message.MessageBody
                },
                ct);

            var body = await response.Content.ReadFromJsonAsync<SwiftSendResponse>(
                cancellationToken: ct);

            if (response.IsSuccessStatusCode && body?.Success == true)
            {
                return new SendResult
                {
                    Success = true,
                    ProviderMessageId = body.MessageId
                };
            }

            _logger.LogWarning("SwiftSend failed: {Error}", body?.Error);

            return new SendResult
            {
                Success = false,
                ErrorMessage = body?.Error ?? $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SwiftSend exception");

            return new SendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

file class SwiftSendResponse
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string[]? FailedRecipients { get; set; }
    public string? Error { get; set; }
}