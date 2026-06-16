using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KotoNeko.Data;

/// <summary>
/// Used by the EF Core tooling (e.g. <c>dotnet ef migrations add</c>) to build a
/// context at design time. It uses a fixed server version so no live MySQL server
/// is required just to scaffold a migration. The real connection string is
/// supplied at runtime by the web host.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KotoNekoDbContext>
{
    public KotoNekoDbContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable("KOTONEKO_CONNECTION")
            ?? "Server=localhost;Port=3306;Database=kotoneko;User=root;Password=password;";

        DbContextOptionsBuilder<KotoNekoDbContext> builder = new();
        builder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 30)));

        return new KotoNekoDbContext(builder.Options);
    }
}
