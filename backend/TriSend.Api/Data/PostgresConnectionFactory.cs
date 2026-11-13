using Npgsql;

namespace TriSend.Api.Data;

public static class PostgresConnectionFactory
{
    public static string? GetConnectionString(IConfiguration configuration)
    {
        var host = configuration["Postgres:Host"];
        if (string.IsNullOrWhiteSpace(host))
            return configuration.GetConnectionString("Postgres");

        var username = configuration["Postgres:Username"];
        var password = configuration["Postgres:Password"];
        var database = configuration["Postgres:Database"];

        if (string.IsNullOrWhiteSpace(username) ||
            password is null ||
            string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException(
                "Postgres:Host, Postgres:Database, Postgres:Username, and Postgres:Password must all be configured.");
        }

        var port = configuration.GetValue("Postgres:Port", 5432);
        var sslModeValue = configuration["Postgres:SslMode"];
        var sslMode = string.IsNullOrWhiteSpace(sslModeValue)
            ? SslMode.Require
            : Enum.Parse<SslMode>(sslModeValue, ignoreCase: true);

        // Assigning values as properties safely handles special characters in passwords
        // (including semicolons) without parsing a hand-built connection string.
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password,
            SslMode = sslMode,
            TrustServerCertificate = false
        };

        return builder.ConnectionString;
    }
}
