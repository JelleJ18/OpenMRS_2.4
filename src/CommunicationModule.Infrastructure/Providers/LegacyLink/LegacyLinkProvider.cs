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

    public async Task<SendResult> SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        try
        {
            // ======================
            // BASIC AUTH (correct)
            // ======================
            var credentials = "legacylink-user:legacylink-password";
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

            var request = new HttpRequestMessage(HttpMethod.Post, "/LegacyLink/SendSms");

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", base64);

            request.Headers.Add("X-STUDENT-GROUP", "group-1");
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/xml"));

            // ======================
            // SOAP XML BODY
            // ======================
            var xml = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <SendSmsRequest xmlns="http://legacylink.fakecomworld.com/v1">
                    <PhoneNumber>{message.PhoneNumber}</PhoneNumber>
                    <MessageText>{message.MessageBody}</MessageText>
                    <SenderIdentification>CommunicationModule</SenderIdentification>
                </SendSmsRequest>
                """;

            request.Content = new StringContent(xml, Encoding.UTF8, "application/xml");

            // ======================
            // SEND REQUEST
            // ======================
            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var doc = XDocument.Parse(body);
                XNamespace ns = "http://legacylink.fakecomworld.com/v1";

                var id = doc.Root?
                    .Element(ns + "MessageReference")?
                    .Value;

                return new SendResult
                {
                    Success = true,
                    ProviderMessageId = id ?? "LegacyLink"
                };
            }

            _logger.LogWarning("LegacyLink failed: {Body}", body);

            return new SendResult
            {
                Success = false,
                ErrorMessage = body
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LegacyLink error");

            return new SendResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}