using Kaleido.Http.Processor;
using Kaleido.Samples.PriorAuth.CodeSet.Queryable.Contexts;
using Kaleido.Samples.PriorAuth.Configuration.Queryable.Contexts;
using Kaleido.Samples.PriorAuth.History.Process.Steps;
using Kaleido.Samples.PriorAuth.Intake.Data;
using Kaleido.Samples.PriorAuth.Intake.Data.Entities;
using Kaleido.Samples.PriorAuth.Intake.Process.Handlers;
using Kaleido.Samples.PriorAuth.Intake.Process.Services;
using Kaleido.Samples.PriorAuth.Intake.Process.Steps;
using Kaleido.Samples.PriorAuth.Member.Queryable.ViewSources.Parameters;
using Kaleido.Samples.PriorAuth.Member.Queryable.ViewSources.Views;
using Kaleido.Samples.PriorAuth.Radiology.Process.Steps;
using Kaleido.Samples.PriorAuth.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using CaptureMemberStep = Kaleido.Samples.PriorAuth.Intake.Process.Steps.CaptureMemberStep;
using CaptureRequestedServiceStep = Kaleido.Samples.PriorAuth.Intake.Process.Steps.CaptureRequestedServiceStep;
using ValidateMemberStep = Kaleido.Samples.PriorAuth.Intake.Process.Steps.ValidateMemberStep;

namespace Kaleido.Samples.PriorAuth.UnitTests.Intake;

/// <summary>Intake's step handlers against an in-memory database and canned answers from the other services.</summary>
public sealed class IntakeHandlersTests : IDisposable
{
    private static readonly DateOnly DateOfService = new(2026, 11, 2);

    private readonly SqliteDatabase<IntakeDbContext> _database = new(options => new IntakeDbContext(options));
    private readonly FakeKaleido _kaleido = new();
    private readonly List<MemberDetailsView> _members = [];
    private readonly List<ProcedureCodeQueryContext> _codes = [];
    private readonly List<ProductCodeMappingQueryContext> _mappings = [];
    private readonly ProcessStepContext _context = Steps.Context();
    private StepExecutionOutcome _historyOutcome = StepExecutionOutcome.Completed;

    public IntakeHandlersTests()
    {
        _kaleido
            .View<MemberDetailsViewParameters, MemberDetailsView>("Member", "MemberQueryContextSource", "MemberDetailsViewSource",
                parameters => _members.Where(x => x.MemberId == parameters?.MemberId && x.MemberEnrollmentId == parameters?.MemberEnrollmentId))
            .Source<ProcedureCodeQueryContext>("CodeSet", "ProcedureCodeQueryContextSource", _ => _codes)
            .Source<ProductCodeMappingQueryContext>("Configuration", "ProductCodeMappingQueryContextSource", _ => _mappings)
            .Step<UpsertPriorAuthRecordStep>("History", step => FakeKaleido.Answer(nameof(UpsertPriorAuthRecordStep), _historyOutcome));
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task ValidateMemberRequiresCaptureMemberForAKnownMember()
    {
        var member = AddMember();

        var result = await new ValidateMemberHandler(new MemberDetailsClient(_kaleido))
            .ExecuteAsync(new ValidateMemberStep { MemberId = member.MemberId, MemberEnrollmentId = member.MemberEnrollmentId, DateOfService = DateOfService }, _context, Token);

        Assert.True(result.Succeeded);
        Assert.Equal(typeof(CaptureMemberStep), result.RequiredStep);
    }

    [Fact]
    public async Task ValidateMemberRejectsAnUnknownMember()
    {
        var result = await new ValidateMemberHandler(new MemberDetailsClient(_kaleido))
            .ExecuteAsync(new ValidateMemberStep { MemberId = Guid.NewGuid(), MemberEnrollmentId = Guid.NewGuid(), DateOfService = DateOfService }, _context, Token);

        Assert.True(result.Rejected);
        Assert.Contains(result.Messages, x => x.Code == "MEMBER_NOT_FOUND");
    }

    [Fact]
    public async Task StartIntakeOpensASessionAndRecordsIt()
    {
        var result = await new StartIntakeHandler(_database.Context, HistoryClient())
            .ExecuteAsync(new StartIntakeStep(), _context, Token);

        Assert.True(result.Succeeded);
        Assert.Single(_database.NewContext().IntakeSessions, x => x.ProcessId == _context.ProcessId);
        var recorded = Assert.IsType<UpsertPriorAuthRecordStep>(Assert.Single(_kaleido.SentSteps).Step);
        Assert.Equal(PriorAuthorizationStatus.Draft, recorded.Status);
    }

    [Fact]
    public async Task CaptureMemberKeepsTheMemberAndRecordsIt()
    {
        var member = AddMember();

        var result = await CaptureMemberAsync(member.MemberId, member.MemberEnrollmentId);

        Assert.True(result.Succeeded);
        Assert.Equal(typeof(CaptureRequestedServiceStep), result.RequiredStep);
        var session = await Sessions().SingleAsync(Token);
        Assert.Equal("M-100", session.Member?.MemberNumber);
        Assert.Equal(DateOfService, session.Member?.DateOfService);
        var recorded = Assert.IsType<UpsertPriorAuthRecordStep>(Assert.Single(_kaleido.SentSteps).Step);
        Assert.Equal("M-100", recorded.MemberNumber);
        Assert.Equal("Jane Doe", recorded.MemberDisplayName);
    }

    [Fact]
    public async Task CaptureMemberRejectsAnUnknownMemberAndKeepsNothing()
    {
        var result = await CaptureMemberAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.Rejected);
        Assert.Contains(result.Messages, x => x.Code == "MEMBER_NOT_FOUND");
        Assert.Empty(_database.NewContext().IntakeSessions);
    }

    [Fact]
    public async Task CaptureMemberIsRolledBackWhenHistoryDoesNotRecord()
    {
        var member = AddMember();
        _historyOutcome = StepExecutionOutcome.Rejected;

        var result = await CaptureMemberAsync(member.MemberId, member.MemberEnrollmentId);

        Assert.True(result.Rejected);
        Assert.Contains(result.Messages, x => x.Code == "HISTORY_NOT_RECORDED");
        Assert.Empty(_database.NewContext().IntakeSessions);
    }

    [Fact]
    public async Task CaptureRequestedServiceRejectsAnUnknownCode()
    {
        var result = await CaptureServiceAsync("99999");

        Assert.True(result.Rejected);
        Assert.Contains(result.Messages, x => x.Code == "PROCEDURE_CODE_NOT_FOUND");
    }

    [Fact]
    public async Task CaptureRequestedServiceRejectsACodeNoProductHandles()
    {
        AddCode("73721");

        var result = await CaptureServiceAsync("73721");

        Assert.True(result.Rejected);
        Assert.Contains(result.Messages, x => x.Code == "PROCESSOR_NOT_FOUND_FOR_CODE");
    }

    [Fact]
    public async Task CaptureRequestedServiceHandsTheRequestToItsProduct()
    {
        var member = AddMember();
        await CaptureMemberAsync(member.MemberId, member.MemberEnrollmentId);
        AddCode("73721", processorName: "radiology");
        _kaleido.Step<StartRadiologyIntakeStep>("radiology", _ => FakeKaleido.Answer(nameof(StartRadiologyIntakeStep)));

        var result = await CaptureServiceAsync("73721", MriBodyPart.Knee);

        Assert.True(result.Succeeded);
        Assert.Equal("radiology", result.TargetProcessorName);
        var start = Assert.IsType<StartRadiologyIntakeStep>(_kaleido.SentSteps.Single(x => x.Client == "radiology").Step);
        Assert.Equal(member.MemberId, start.MemberId);
        Assert.Equal(DateOfService, start.DateOfService);
        Assert.Equal("73721", start.CodeValue);
        Assert.Equal(MriBodyPart.Knee, start.BodyPart);
        var session = await Sessions().SingleAsync(Token);
        Assert.Equal("radiology", session.Procedure?.ResolvedProcessorName);
    }

    [Fact]
    public async Task CaptureRequestedServiceKeepsNothingWhenTheProductRefusesTheRequest()
    {
        AddCode("73721", processorName: "radiology");
        _kaleido.Step<StartRadiologyIntakeStep>("radiology", _ => FakeKaleido.Answer(nameof(StartRadiologyIntakeStep), StepExecutionOutcome.Rejected));

        var result = await CaptureServiceAsync("73721");

        Assert.True(result.Rejected);
        Assert.Null(result.TargetProcessorName);
        Assert.Empty(_database.NewContext().IntakeSessionProcedures);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private HistoryClient HistoryClient() => new(_database.Context, _kaleido, NullLogger<HistoryClient>.Instance);

    private IQueryable<IntakeSession> Sessions() =>
        _database.NewContext().IntakeSessions.Include(x => x.Member).Include(x => x.Procedure).Where(x => x.ProcessId == _context.ProcessId);

    private Task<ProcessStepHandlerResult> CaptureMemberAsync(Guid memberId, Guid enrollmentId) =>
        new CaptureMemberHandler(_database.Context, new MemberDetailsClient(_kaleido), HistoryClient())
            .ExecuteAsync(new CaptureMemberStep { MemberId = memberId, MemberEnrollmentId = enrollmentId, DateOfService = DateOfService }, _context, Token);

    private Task<ProcessStepHandlerResult> CaptureServiceAsync(string codeValue, MriBodyPart? bodyPart = null) =>
        new CaptureRequestedServiceHandler(_database.Context, new ProcedureCodeClient(_kaleido), new ProductCodeMappingClient(_kaleido), _kaleido, HistoryClient())
            .ExecuteAsync(new CaptureRequestedServiceStep { CodeValue = codeValue, CodeSystem = ProcedureCodeSystem.Cpt, BodyPart = bodyPart }, _context, Token);

    private MemberDetailsView AddMember()
    {
        var member = new MemberDetailsView { MemberId = Guid.NewGuid(), MemberEnrollmentId = Guid.NewGuid(), MemberNumber = "M-100", DisplayName = "Jane Doe" };
        _members.Add(member);
        return member;
    }

    private void AddCode(string codeValue, string? processorName = null)
    {
        _codes.Add(new ProcedureCodeQueryContext { CodeValue = codeValue, CodeSystem = ProcedureCodeSystem.Cpt, ShortDescription = "MRI knee" });

        if (processorName is not null)
        {
            _mappings.Add(new ProductCodeMappingQueryContext { CodeValue = codeValue, CodeSystem = ProcedureCodeSystem.Cpt, ProcessorName = processorName });
        }
    }
}
