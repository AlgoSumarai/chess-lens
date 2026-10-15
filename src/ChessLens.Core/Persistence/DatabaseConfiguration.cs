using System.Text;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ChessLens.Core.Persistence;

public static class DatabaseConfiguration
{
    public static string ConnectionString(IConfiguration configuration)
    {
        var supplied = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("ConnectionStrings__Database is required.");
        var passwordFile = configuration["Database:PasswordFile"];
        if (string.IsNullOrWhiteSpace(passwordFile)) return supplied;
        var connection = new NpgsqlConnectionStringBuilder(supplied);
        if (!string.IsNullOrEmpty(connection.Password))
            throw new InvalidOperationException("Configure a database password file or an inline password, not both.");
        var file = new FileInfo(passwordFile);
        if (!file.Exists || file.Length is < 1 or > 4096)
            throw new InvalidOperationException("The database password file must contain one bounded nonempty password.");
        var password = File.ReadAllText(file.FullName, new UTF8Encoding(false, true)).TrimEnd('\r', '\n');
        if (password.Length == 0 || password.Contains('\r') || password.Contains('\n') || password.Contains('\0'))
            throw new InvalidOperationException("The database password file must contain one nonempty line.");
        connection.Password = password;
        return connection.ConnectionString;
    }
}
