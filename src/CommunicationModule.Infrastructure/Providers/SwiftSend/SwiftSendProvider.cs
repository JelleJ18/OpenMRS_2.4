using System.Net.Http.Json;
using CommunicationModule.Core.DTOs;
using CommunicationModule.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace CommunicationModule.Infrastructure.Providers.SwiftSend;

public class SwiftSendProvider : IMessagingProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SwiftSendProvider> _logger;

    public string ProviderName => "SwiftSend";

    public SwiftSendProvider(HttpClient httpClient, ILogger<SwiftSendProvider> logger)
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
            var response = await _httpClient.PostAsJsonAsync("swiftsend", new
            {
                Recipients = new[] { message.PhoneNumber },
                Content = message.MessageBody
            }, cancellationToken);

            var responseBody = await response.Content.ReadFromJsonAsync<SwiftSendResponse>(
                cancellationToken: cancellationToken);

            if (response.IsSuccessStatusCode && responseBody?.Success == true)
            {
                return new SendResult
                {
                    Success = true,
                    ProviderMessageId = responseBody.MessageId
                };
            }

            _logger.LogWarning("SwiftSend failed with status {StatusCode}: {Error}",
                response.StatusCode, responseBody?.Error);

            return new SendResult
            {
                Success = false,
                ErrorMessage = responseBody?.Error ?? $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message via SwiftSend");
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
    public string? Error { get; set; }
}