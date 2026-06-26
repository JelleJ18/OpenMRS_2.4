using System.Net.Http.Headers;
using System.Net.Http.Json;
using CommunicationModule.Core.DTOs;
using CommunicationModule.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace CommunicationModule.Infrastructure.Providers.SecurePost;

public class SecurePostProvider : IMessagingProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SecurePostProvider> _logger;

    private string? _cachedToken;
    private DateTime _expiresAt = DateTime.MinValue;

    public string ProviderName => "SecurePost";

    public SecurePostProvider(HttpClient httpClient, ILogger<SecurePostProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken != null && DateTime.UtcNow < _expiresAt)
            return _cachedToken;

        _httpClient.DefaultRequestHeaders.Clear();

        _httpClient.DefaultRequestHeaders.Add(
            "X-STUDENT-GROUP",
            "group-1");

        var response = await _httpClient.PostAsJsonAsync(
            "/securepost/auth",
            new
            {
                clientId = "securepost-client-id",
                clientSecret = "securepost-secret-key"
            },
            ct);

        if (!response.IsSuccessStatusCode)
            return null;

        var token = await response.Content.ReadFromJsonAsync<SecurePostTokenResponse>(
            cancellationToken: ct);

        if (token == null)
            return null;

        _cachedToken = token.AccessToken;
        _expiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 30);

        return _cachedToken;
    }

    public async Task<SendResult> SendAsync(
        NotificationMessage message,
        CancellationToken ct = default)
    {
        try
        {
            var token = await GetTokenAsync(ct);

            if (token == null)
            {
                return new SendResult
                {
                    Success = false,
                    ErrorMessage = "Could not obtain JWT token."
                };
            }

            _httpClient.DefaultRequestHeaders.Clear();

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            _httpClient.DefaultRequestHeaders.Add(
                "X-STUDENT-GROUP",
                "group-1");

            var response = await _httpClient.PostAsJsonAsync(
                "/securepost/message",
                new
                {
                    format = "SMS",
                    recipient = message.PhoneNumber,
                    body = message.MessageBody,
                    subject = "Reminder"
                },
                ct);

            var result = await response.Content.ReadFromJsonAsync<SecurePostMessageResponse>(
                cancellationToken: ct);

            if (response.IsSuccessStatusCode && result?.Delivered == true)
            {
                return new SendResult
                {
                    Success = true,
                    ProviderMessageId = result.TrackingId
                };
            }

            return new SendResult
            {
                Success = false,
                ProviderMessageId = result?.TrackingId,
                ErrorMessage = result?.ErrorMessage ?? $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SecurePost error");

            return new SendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

file class SecurePostTokenResponse
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
    public DateTime IssuedAt { get; set; }
}

file class SecurePostMessageResponse
{
    public bool Delivered { get; set; }
    public string? TrackingId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? DeliveryTimestamp { get; set; }
}