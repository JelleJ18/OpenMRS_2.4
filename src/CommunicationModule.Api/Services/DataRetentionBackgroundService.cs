using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CommunicationModule.Api.Services;

public class DataRetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public DataRetentionBackgroundService(
        IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();

            var retentionService =
                scope.ServiceProvider.GetRequiredService<DataRetentionService>();

            await retentionService.CleanupAsync();

            await Task.Delay(
                TimeSpan.FromDays(1),
                stoppingToken);
        }
    }
}
