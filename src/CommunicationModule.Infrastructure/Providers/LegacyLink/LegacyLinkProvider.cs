using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using CommunicationModule.Core.DTOs;
using CommunicationModule.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace CommunicationModule.Infrastructure.Providers.LegacyLink;

public class LegacyLinkProvider : IMessagingProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LegacyLinkProvider> _logger;

    public string ProviderName => "LegacyLink";

    public LegacyLinkProvider(HttpClient httpClient, ILogger<LegacyLinkProvider> logger)
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
            var xml = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <SendSmsRequest xmlns="http://legacylink.fakecomworld.com/v1">
                  <PhoneNumber>{message.PhoneNumber}</PhoneNumber>
                  <MessageText>{message.MessageBody}</MessageText>
                  <SenderIdentification>CommunicationModule</SenderIdentification>
                </SendSmsRequest>
                """;

            var content = new StringContent(xml, Encoding.UTF8, "application/xml");

            var response = await _httpClient.PostAsync(
                "LegacyLink/SendSms",
                content,
                cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var doc = XDocument.Parse(responseBody);
                XNamespace ns = "http://legacylink.fakecomworld.com/v1";
                var messageReference = doc.Root?.Element(ns + "MessageReference")?.Value;

                return new SendResult
                {
                    Success = true,
                    ProviderMessageId = messageReference ?? ProviderName
                };
            }

            _logger.LogWarning("LegacyLink failed with status {StatusCode}: {Body}",
                response.StatusCode, responseBody);

            return new SendResult
            {
                Success = false,
                ProviderMessageId = ProviderName,
                ErrorMessage = $"HTTP {(int)response.StatusCode}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message via LegacyLink");
            return new SendResult
            {
                Success = false,
                ProviderMessageId = ProviderName,
                ErrorMessage = ex.Message
            };
        }
    }
}