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
    private DateTime _tokenExpiry = DateTime.MinValue;

    public string ProviderName => "SecurePost";

    public SecurePostProvider(HttpClient httpClient, ILogger<SecurePostProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;

        var response = await _httpClient.PostAsJsonAsync("securepost/auth", new
        {
            clientId = _httpClient.DefaultRequestHeaders.GetValues("X-CLIENT-ID").First(),
            clientSecret = _httpClient.DefaultRequestHeaders.GetValues("X-CLIENT-SECRET").First()
        }, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<SecurePostTokenResponse>(
            cancellationToken: cancellationToken);

        _cachedToken = result?.AccessToken;
        _tokenExpiry = DateTime.UtcNow.AddSeconds((result?.ExpiresIn ?? 180) - 30); // 30s buffer

        return _cachedToken;
    }

    public async Task<SendResult> SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var token = await GetTokenAsync(cancellationToken);
            if (token == null)
            {
                return new SendResult
                {
                    Success = false,
                    ProviderMessageId = ProviderName,
                    ErrorMessage = "Failed to obtain access token"
                };
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "securepost/message");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new
            {
                format = "SMS",
                recipient = message.PhoneNumber,
                body = message.MessageBody,
                subject = "Notification"
            });

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var result = await response.Content.ReadFromJsonAsync<SecurePostMessageResponse>(
                cancellationToken: cancellationToken);

            if (response.IsSuccessStatusCode && result?.Delivered == true)
            {
                return new SendResult
                {
                    Success = true,
                    ProviderMessageId = result.TrackingId ?? ProviderName
                };
            }

            _logger.LogWarning("SecurePost failed: {Error}", result?.ErrorMessage);
            return new SendResult
            {
                Success = false,
                ProviderMessageId = ProviderName,
                ErrorMessage = result?.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message via SecurePost");
            return new SendResult
            {
                Success = false,
                ProviderMessageId = ProviderName,
                ErrorMessage = ex.Message
            };
        }
    }
}

file class SecurePostTokenResponse
{
    public string? AccessToken { get; set; }
    public int ExpiresIn { get; set; }
}

file class SecurePostMessageResponse
{
    public bool Delivered { get; set; }
    public string? TrackingId { get; set; }
    public string? ErrorMessage { get; set; }
}