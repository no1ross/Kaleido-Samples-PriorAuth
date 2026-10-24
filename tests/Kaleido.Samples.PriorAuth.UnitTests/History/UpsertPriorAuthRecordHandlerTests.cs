using Kaleido.Observability;
using Kaleido.Samples.PriorAuth.History.Data;
using Kaleido.Samples.PriorAuth.History.Process.Handlers;
using Kaleido.Samples.PriorAuth.History.Process.Steps;
using Kaleido.Samples.PriorAuth.UnitTests.TestSupport;

namespace Kaleido.Samples.PriorAuth.UnitTests.History;

public sealed class UpsertPriorAuthRecordHandlerTests : IDisposable
{
    private readonly SqliteDatabase<HistoryDbContext> _database = new(options => new HistoryDbContext(options));
    private readonly Mock<IKaleidoCorrelationContextAccessor> _correlation = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task RejectsACallFromOutsideAProcessorStep()
    {
        _correlation.SetupGet(x => x.Current).Returns(new KaleidoCorrelationContext());

        var result = await CreateHandler().ExecuteAsync(Snapshot(), Steps.Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Rejected);
        Assert.Contains(result.Messages, x => x.Code == "CALLING_PROCESSOR_REQUIRED");
        Assert.Empty(_database.Context.PriorAuthRecords);
    }

    [Fact]
    public async Task AddsOneRecordPerProcess()
    {
        CalledBy("intake");
        var context = Steps.Context();

        var result = await CreateHandler().ExecuteAsync(Snapshot(), context, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        var record = Assert.Single(_database.NewContext().PriorAuthRecords);
        Assert.Equal(context.ProcessId, record.ProcessId);
        Assert.Equal("intake", record.ProcessorName);
        Assert.Equal("M-1", record.MemberNumber);
        Assert.Null(record.PriorAuthorizationId);
    }

    [Fact]
    public async Task OverwritesTheRecordWithTheLatestSnapshot()
    {
        var context = Steps.Context();
        CalledBy("intake");
        await CreateHandler().ExecuteAsync(Snapshot(), context, TestContext.Current.CancellationToken);

        CalledBy("radiology");
        var priorAuthorizationId = Guid.NewGuid();
        await CreateHandler().ExecuteAsync(
            Snapshot() with { PriorAuthorizationId = priorAuthorizationId, Status = PriorAuthorizationStatus.Submitted, PrimaryProcedureCode = "73721" },
            context,
            TestContext.Current.CancellationToken);

        var record = Assert.Single(_database.NewContext().PriorAuthRecords);
        Assert.Equal("radiology", record.ProcessorName);
        Assert.Equal(priorAuthorizationId, record.PriorAuthorizationId);
        Assert.Equal(PriorAuthorizationStatus.Submitted, record.Status);
        Assert.Equal("73721", record.PrimaryProcedureCode);
    }

    private UpsertPriorAuthRecordHandler CreateHandler() => new(_database.Context, _correlation.Object);

    private void CalledBy(string processorName) =>
        _correlation.SetupGet(x => x.Current).Returns(new KaleidoCorrelationContext { CallingProcessorName = processorName });

    private static UpsertPriorAuthRecordStep Snapshot() => new()
    {
        Status = PriorAuthorizationStatus.Draft,
        MemberNumber = "M-1",
        MemberDisplayName = "Jane Doe",
        DateOfService = new DateOnly(2026, 11, 2),
        PrimaryProcedureCode = "70553",
        PrimaryProcedureDescription = "MRI brain"
    };
}
