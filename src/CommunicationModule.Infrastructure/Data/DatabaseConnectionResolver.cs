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
                $"Missing connection string '{connectionName}'. Store it in user secrets or an external secret source and set Database:Profile to '{LocalProfile}' or '{ProductionProfile}'.");
        }

        return connectionString;
    }

    public static MySqlServerVersion GetServerVersion() => new(new Version(8, 0, 36));

    private static bool IsProduction(string profile) =>
        profile.Equals(ProductionProfile, StringComparison.OrdinalIgnoreCase);
}
