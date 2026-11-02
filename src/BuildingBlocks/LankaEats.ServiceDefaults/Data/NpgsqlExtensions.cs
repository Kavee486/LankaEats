using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace LankaEats.ServiceDefaults.Data;

public static class NpgsqlExtensions
{
    /// <summary>
    /// UseNpgsql with tracing limited to commands that run inside an existing trace (a request or
    /// message). Without this filter, the outbox's once-a-second polling query would create a new
    /// root trace every second and flood the trace view.
    /// </summary>
    public static DbContextOptionsBuilder UseLankaEatsNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.ConfigureDataSource(dataSource =>
            dataSource.ConfigureTracing(tracing => tracing
                .ConfigureCommandFilter(_ => Activity.Current is not null)
                .ConfigureBatchFilter(_ => Activity.Current is not null))));
}
