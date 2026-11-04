using Kaleido.Exceptions;
using Kaleido.Provider.SQLite;
using Kaleido.Samples.PriorAuth.Compliance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kaleido.Samples.PriorAuth;

/// <summary>
/// Kaleido's stores for a PriorAuth service: process state, idempotency records and the event outbox in
/// the service's own SQLite database (<c>ConnectionStrings:{connectionName}</c>), so a step's state and its
/// events commit together (ADR 0023), and a relay on that database publishing its events to the
/// EventCollector through <see cref="HttpEventPublisher"/> (ADR 0036). Every service needs an outbox store,
/// a query-only one included (#28).
/// </summary>
/// <remarks>
/// Interim (#28): one database and one relay per service. It gives way to Kaleido's
/// <c>AddSqliteStores(connectionName)</c> (no1ross/Kaleido#558) and a single relay host (#31).
/// The host registers the <c>PriorAuthEventCollector</c> HttpClient the publisher uses.
/// </remarks>
public static class PriorAuthStores
{
    public static IKaleidoBuilder AddPriorAuthStores(this IKaleidoBuilder builder, string connectionName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var connectionString =
            builder.Configuration.GetConnectionString(connectionName)
            ?? throw new KaleidoConfigurationException(
                PriorAuthErrorCodes.MissingConnectionString,
                $"ConnectionStrings:{connectionName} is required.");

        return builder
            .AddSqliteContextStore(connectionString)
            .AddSqliteOutboxStore(connectionString)
            .AddSqliteIdempotencyStore(connectionString)
            .AddOutboxRelay<HttpEventPublisher>();
    }

    /// <summary>
    /// Creates the stores' tables when the database is new. <c>EnsureCreated</c> never changes an existing
    /// database: after a store schema change, delete the file (AGENTS.md, Local run workflow).
    /// </summary>
    public static async Task EnsurePriorAuthStoresCreatedAsync(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<SqliteKaleidoContextDbContext>()
            .Database.EnsureCreatedAsync();
    }
}
