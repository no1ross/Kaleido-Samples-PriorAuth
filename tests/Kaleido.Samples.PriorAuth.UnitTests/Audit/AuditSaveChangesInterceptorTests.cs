using Kaleido.Samples.PriorAuth.Audit;
using Kaleido.Samples.PriorAuth.History.Data;
using Kaleido.Samples.PriorAuth.History.Data.Entities;
using Kaleido.Samples.PriorAuth.UnitTests.TestSupport;

namespace Kaleido.Samples.PriorAuth.UnitTests.Audit;

public sealed class AuditSaveChangesInterceptorTests : IDisposable
{
    private readonly SqliteDatabase<HistoryDbContext> _database = new(options => new HistoryDbContext(options));

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AddedRowGetsEqualCreatedAndUpdatedValues()
    {
        _database.Caller = new() { CallerName = "bob", CallerActor = "intake" };

        var record = await AddRecordAsync();

        Assert.Equal("bob", record.CreatedBy);
        Assert.Equal("intake", record.CreatedVia);
        Assert.Equal(record.CreatedBy, record.UpdatedBy);
        Assert.Equal(record.CreatedVia, record.UpdatedVia);
        Assert.Equal(record.CreatedUtc, record.UpdatedUtc);
    }

    [Fact]
    public async Task ChangedRowGetsNewUpdatedValuesAndKeepsCreatedValues()
    {
        var record = await AddRecordAsync();
        var created = record.CreatedUtc;

        _database.Caller = new() { CallerName = "carol", CallerActor = "radiology" };
        record.MemberDisplayName = "Changed";
        record.CreatedBy = "someone else";
        await _database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var readBack = _database.NewContext();
        var saved = await readBack.PriorAuthRecords.FindAsync([record.ProcessId], TestContext.Current.CancellationToken);

        Assert.NotNull(saved);
        Assert.Equal("bob", saved.CreatedBy);
        // Stored as a binary value: precise to about a tenth of a millisecond.
        Assert.Equal(created, saved.CreatedUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal("carol", saved.UpdatedBy);
        Assert.Equal("radiology", saved.UpdatedVia);
        Assert.True(saved.UpdatedUtc >= created);
    }

    [Fact]
    public async Task UnchangedRowKeepsItsUpdatedValues()
    {
        var record = await AddRecordAsync();
        var updated = record.UpdatedUtc;

        _database.Caller = new() { CallerName = "carol" };
        record.MemberDisplayName = record.MemberDisplayName;
        await _database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal("bob", record.UpdatedBy);
        Assert.Equal(updated, record.UpdatedUtc);
    }

    [Fact]
    public async Task CallerIdIsUsedWhenThereIsNoName()
    {
        _database.Caller = new() { CallerId = "subject-1" };

        var record = await AddRecordAsync();

        Assert.Equal("subject-1", record.CreatedBy);
    }

    [Fact]
    public async Task NoCallerIsRecordedAsSystem()
    {
        _database.Caller = new();

        var record = await AddRecordAsync();

        Assert.Equal(AuditSaveChangesInterceptor.System, record.CreatedBy);
        Assert.Null(record.CreatedVia);
    }

    [Fact]
    public void SynchronousSaveStampsToo()
    {
        var record = new PriorAuthRecord { ProcessId = Guid.NewGuid(), ProcessorName = "intake" };
        _database.Context.PriorAuthRecords.Add(record);

        _database.Context.SaveChanges();

        Assert.Equal("bob", record.CreatedBy);
    }

    private async Task<PriorAuthRecord> AddRecordAsync()
    {
        var record = new PriorAuthRecord { ProcessId = Guid.NewGuid(), ProcessorName = "intake", MemberDisplayName = "Jane Doe" };
        _database.Context.PriorAuthRecords.Add(record);
        await _database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return record;
    }
}
