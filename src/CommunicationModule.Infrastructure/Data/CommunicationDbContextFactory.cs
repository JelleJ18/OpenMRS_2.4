using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;

namespace CommunicationModule.Infrastructure.Data;

public class CommunicationDbContextFactory : IDesignTimeDbContextFactory<CommunicationDbContext>
{
    public CommunicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveDesignTimeConnectionString();

        var optionsBuilder = new DbContextOptionsBuilder<CommunicationDbContext>();
        optionsBuilder.UseMySql(connectionString, DatabaseConnectionResolver.GetServerVersion());

        return new CommunicationDbContext(optionsBuilder.Options);
    }

    private static string ResolveDesignTimeConnectionString()
    {
        return DatabaseConnectionResolver.ResolveConnectionStringFromEnvironment();
    }

    private static string FindApiProjectDirectory()
    {
        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (currentDirectory is not null)
        {
            var candidate = Path.Combine(currentDirectory.FullName, "src", "CommunicationModule.Api");
            if (File.Exists(Path.Combine(candidate, "CommunicationModule.Api.csproj")))
            {
                return candidate;
            }

            if (File.Exists(Path.Combine(currentDirectory.FullName, "CommunicationModule.Api.csproj")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the CommunicationModule.Api project directory.");
    }
}