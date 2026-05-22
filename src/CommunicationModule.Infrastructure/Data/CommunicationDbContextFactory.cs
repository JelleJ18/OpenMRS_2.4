using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Infrastructure.Data;

public class CommunicationDbContextFactory : IDesignTimeDbContextFactory<CommunicationDbContext>
{
    public CommunicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<CommunicationDbContextFactory>(optional: true)
            .Build();

        var connectionString = DatabaseConnectionResolver.ResolveConnectionString(configuration);

        var optionsBuilder = new DbContextOptionsBuilder<CommunicationDbContext>();
        optionsBuilder.UseMySql(connectionString, DatabaseConnectionResolver.GetServerVersion());

        return new CommunicationDbContext(optionsBuilder.Options);
    }
}