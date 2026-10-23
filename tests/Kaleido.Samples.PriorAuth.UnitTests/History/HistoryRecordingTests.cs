using Kaleido.Samples.PriorAuth.History.Data;
using Kaleido.Samples.PriorAuth.History.Data.Entities;
using Kaleido.Samples.PriorAuth.History.Process;
using Kaleido.Samples.PriorAuth.UnitTests.TestSupport;

namespace Kaleido.Samples.PriorAuth.UnitTests.History;

public sealed class HistoryRecordingTests : IDisposable
{
    private static readonly ProcessStepHandlerResult NotRecorded =
        ProcessStepHandlerResult.Reject(new ProcessMessage { Code = "HISTORY_NOT_RECORDED", Type = MessageType.Error, Message = "Not recorded." });

    private readonly SqliteDatabase<HistoryDbContext> _database = new(options => new HistoryDbContext(options));

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task CommitsWhenTheStepSucceeds()
    {
        var result = await RunAsync(() => ProcessStepHandlerResult.Success());

        Assert.True(result.Succeeded);
        Assert.Single(_database.NewContext().PriorAuthRecords);
    }

    [Fact]
    public async Task RollsBackWhenTheStepIsRejected()
    {
        var rejected = ProcessStepHandlerResult.Reject(new ProcessMessage { Code = "NO", Type = MessageType.Error, Message = "No." });

        var result = await RunAsync(() => rejected);

        Assert.Same(rejected, result);
        Assert.Empty(_database.NewContext().PriorAuthRecords);
    }

    [Fact]
    public async Task RollsBackAndRejectsWhenHistoryDoesNotRecord()
    {
        var result = await RunAsync(() => throw new HistoryNotRecordedException("History could not be reached."));

        Assert.Same(NotRecorded, result);
        Assert.Empty(_database.NewContext().PriorAuthRecords);
    }

    private Task<ProcessStepHandlerResult> RunAsync(Func<ProcessStepHandlerResult> outcome) =>
        HistoryRecording.InTransactionAsync(
            _database.Context,
            async () =>
            {
                _database.Context.PriorAuthRecords.Add(new PriorAuthRecord { ProcessId = Guid.NewGuid(), ProcessorName = "intake" });
                await _database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
                return outcome();
            },
            _ => NotRecorded,
            TestContext.Current.CancellationToken);
}
