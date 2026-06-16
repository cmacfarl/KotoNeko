using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KotoNeko.Data;

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Register the KotoNeko DbContext against MySQL using the given connection
    /// string. Uses a pinned server version to avoid a startup round-trip; adjust
    /// if your MySQL version differs significantly.
    /// </summary>
    public static IServiceCollection AddKotoNekoData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContextFactory<KotoNekoDbContext>(options =>
            options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 30))));

        return services;
    }
}
