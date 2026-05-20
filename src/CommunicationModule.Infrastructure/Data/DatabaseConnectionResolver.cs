using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace CommunicationModule.Infrastructure.Data;

public static class DatabaseConnectionResolver
{
    public const string LocalProfile = "Local";
    public const string ProductionProfile = "Production";

    public const string LocalConnectionName = "LocalConnection";
    public const string ProductionConnectionName = "ProductionConnection";

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var profile = configuration["Database:Profile"] ?? LocalProfile;
        var connectionName = IsProduction(profile) ? ProductionConnectionName : LocalConnectionName;

        var connectionString = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Missing connection string '{connectionName}'. Set Database:Profile to '{LocalProfile}' or '{ProductionProfile}' and configure the matching connection string.");
        }

        return connectionString;
    }

    public static string ResolveConnectionStringFromEnvironment()
    {
        var profile = Environment.GetEnvironmentVariable("Database__Profile") ?? LocalProfile;
        var connectionName = IsProduction(profile) ? ProductionConnectionName : LocalConnectionName;

        var connectionString = Environment.GetEnvironmentVariable($"ConnectionStrings__{connectionName}");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        if (IsProduction(profile))
        {
            throw new InvalidOperationException(
                $"Missing environment variable ConnectionStrings__{ProductionConnectionName}. Configure the production database connection before running migrations or the app.");
        }

        return "Server=localhost;Port=3306;Database=communication;User=root;Password=root;";
    }

    public static MySqlServerVersion GetServerVersion() => new(new Version(8, 0, 36));

    private static bool IsProduction(string profile) =>
        profile.Equals(ProductionProfile, StringComparison.OrdinalIgnoreCase);
}
