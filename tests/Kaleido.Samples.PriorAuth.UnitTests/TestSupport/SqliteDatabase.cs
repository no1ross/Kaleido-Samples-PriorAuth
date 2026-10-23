using Kaleido.Observability;
using Kaleido.Samples.PriorAuth.Audit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kaleido.Samples.PriorAuth.UnitTests.TestSupport;

/// <summary>
/// An in-memory SQLite database for one test: open while the instance lives, schema created, audit
/// fields stamped for <see cref="Caller"/>. <see cref="NewContext"/> gives a fresh context on the same
/// database (to read back what another context saved).
/// </summary>
public sealed class SqliteDatabase<TContext> : IDisposable
    where TContext : DbContext
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly Func<DbContextOptions<TContext>, TContext> _create;

    public SqliteDatabase(Func<DbContextOptions<TContext>, TContext> create)
    {
        _create = create;
        _connection.Open();

        using var context = NewContext();
        context.Database.EnsureCreated();
        Context = NewContext();
    }

    /// <summary>The caller the audit interceptor stamps (by default bob, directly).</summary>
    public KaleidoCorrelationContext Caller { get; set; } = new() { CallerName = "bob" };

    /// <summary>The context a test hands to the class it tests.</summary>
    public TContext Context { get; }

    public TContext NewContext() =>
        _create(
            new DbContextOptionsBuilder<TContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new AuditSaveChangesInterceptor(new CallerAccessor(this)))
                .Options);

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }

    private sealed class CallerAccessor(SqliteDatabase<TContext> database) : IKaleidoCorrelationContextAccessor
    {
        public KaleidoCorrelationContext Current => database.Caller;
    }
}
