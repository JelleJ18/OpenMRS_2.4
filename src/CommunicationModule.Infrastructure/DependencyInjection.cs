using CommunicationModule.Core.Interfaces;
using CommunicationModule.Infrastructure.Messaging;
using CommunicationModule.Infrastructure.Providers.SwiftSend;
namespace CommunicationModule.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CommunicationModule.Infrastructure.Providers.LegacyLink;
using System.Net.Http.Headers;
using System.Text;
using CommunicationModule.Infrastructure.Providers.SecurePost;
using CommunicationModule.Infrastructure.Providers.AsyncFlow;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpClient<SwiftSendProvider>(client =>
        {
            var baseUrl = configuration["Providers:SwiftSend:BaseUrl"]
            ?? throw new InvalidOperationException(
                "Missing configuration: Providers:SwiftSend:BaseUrl");

            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("X-API-KEY", configuration["Providers:SwiftSend:ApiKey"]);
            client.DefaultRequestHeaders.Add("X-STUDENT-GROUP", configuration["Providers:SwiftSend:StudentGroup"]);
        });

        services.AddHttpClient<LegacyLinkProvider>(client =>
        {
            var baseUrl = configuration["Providers:LegacyLink:BaseUrl"]
                ?? throw new InvalidOperationException("Missing configuration: Providers:LegacyLink:BaseUrl");

            var username = configuration["Providers:LegacyLink:Username"]!;
            var password = configuration["Providers:LegacyLink:Password"]!;
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Add("X-STUDENT-GROUP", configuration["Providers:LegacyLink:StudentGroup"]);
        });

        services.AddHttpClient<SecurePostProvider>(client =>
        {
            var baseUrl = configuration["Providers:SecurePost:BaseUrl"]
                ?? throw new InvalidOperationException("Missing configuration: Providers:SecurePost:BaseUrl");

            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("X-STUDENT-GROUP", configuration["Providers:SecurePost:StudentGroup"]);
            client.DefaultRequestHeaders.Add("X-CLIENT-ID", configuration["Providers:SecurePost:ClientId"]);
            client.DefaultRequestHeaders.Add("X-CLIENT-SECRET", configuration["Providers:SecurePost:ClientSecret"]);
        });

        services.AddHttpClient<AsyncFlowProvider>(client =>
        {
            var baseUrl = configuration["Providers:AsyncFlow:BaseUrl"]
                ?? throw new InvalidOperationException("Missing configuration: Providers:AsyncFlow:BaseUrl");

            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("X-API-KEY", configuration["Providers:AsyncFlow:ApiKey"]);
            client.DefaultRequestHeaders.Add("X-STUDENT-GROUP", configuration["Providers:AsyncFlow:StudentGroup"]);
        });

        services.AddScoped<IMessagingProvider>(sp => sp.GetRequiredService<AsyncFlowProvider>());

        services.AddScoped<IMessagingProvider>(sp => sp.GetRequiredService<SecurePostProvider>());

        services.AddScoped<IMessagingProvider>(sp => sp.GetRequiredService<LegacyLinkProvider>());

        services.AddScoped<IMessagingProvider>(sp =>
            sp.GetRequiredService<SwiftSendProvider>());

        services.AddScoped<MessagingProviderResolver>();

        services.AddScoped<IMessageProviderResolver,
        MessagingProviderResolver>();

        return services;
    }
}