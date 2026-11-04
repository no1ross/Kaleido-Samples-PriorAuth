using Kaleido.Http.Processor;
using Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;
using Kaleido.Samples.PriorAuth.History.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IntakeSteps = Kaleido.Samples.PriorAuth.Intake.Process.Steps;
using RadiologySteps = Kaleido.Samples.PriorAuth.Radiology.Process.Steps;

namespace Kaleido.Samples.PriorAuth.FunctionalTests;

/// <summary>A request end to end: Intake, the handoff to Radiology, submission, and History along the way.</summary>
public sealed class PriorAuthRequestTests(PriorAuthHosts hosts)
{
    [Fact]
    public async Task BobSubmitsAnMriRequestFromIntakeToRadiology()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        var member = await flow.StartWithMemberAsync();

        var handoff = (await flow.CaptureServiceAsync()).Completed;
        Assert.Equal("radiology", handoff.TargetProcessorName);

        // The hand-off answers with Radiology's next step, its questions and its URL: no second call (#27).
        Assert.Equal(nameof(RadiologySteps.CaptureMriInfoStep), handoff.RequiredStep?.Name);
        Assert.NotNull(handoff.RequiredStep?.InformationRequest?.ExchangeId);
        Assert.StartsWith("/radiology/", handoff.RequiredStep?.ExecuteUrl, StringComparison.Ordinal);

        await flow.AnswerMriQuestionsAsync();
        await flow.CaptureRequestingProviderAsync();

        var servicing = await flow.CaptureServicingProviderAsync();
        Assert.Equal(nameof(RadiologySteps.CaptureClinicalInfoStep), servicing.RequiredStep?.Name);

        await flow.AnswerClinicalQuestionsAsync();
        var submitted = await flow.ReviewAndSubmitAsync();

        Assert.Equal(StepExecutionStatus.Completed, submitted.Status);

        var record = await HistoryRecordAsync(flow.ProcessId);
        Assert.Equal(PriorAuthorizationStatus.Submitted, record.Status);
        Assert.Equal("radiology", record.ProcessorName);
        Assert.Equal(member.MemberNumber, record.MemberNumber);
        Assert.NotNull(record.PriorAuthorizationId);
        Assert.Equal("bob", record.UpdatedBy);
        Assert.Equal("radiology", record.UpdatedVia);
    }

    [Fact]
    public async Task IntakeRunsNoStepOnceItHandedTheRequestOff()
    {
        // The service is captured before any member: Intake's member steps have not run.
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        (await flow.Client.ExecuteAsync("intake", new IntakeSteps.StartIntakeStep())).Completed.ToString();
        (await flow.CaptureServiceAsync()).Completed.ToString();

        // Radiology owns the request now (ADR 0033): Intake refuses its own steps (#27).
        var call = await flow.Client.ExecuteAsync("intake", new IntakeSteps.ValidateMemberStep
        {
            MemberId = Guid.NewGuid(),
            MemberEnrollmentId = Guid.NewGuid(),
            DateOfService = PriorAuthFlow.DateOfService
        });

        Assert.Equal(StepExecutionStatus.Pending, call.Response?.Status);
        Assert.Contains("pro_process_ended", call.Response?.FrameworkMessages.Select(x => x.Code) ?? []);
    }

    [Fact]
    public async Task HistoryHasTheRequestWhileItIsInIntake()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        var member = await flow.StartWithMemberAsync();

        var record = await HistoryRecordAsync(flow.ProcessId);

        Assert.Equal("intake", record.ProcessorName);
        Assert.Equal(member.MemberNumber, record.MemberNumber);
        Assert.Null(record.PriorAuthorizationId);
        Assert.Equal("bob", record.UpdatedBy);
        Assert.Equal("intake", record.UpdatedVia);
    }

    [Fact]
    public async Task AliceIsRefusedAtTheHandoffToRadiology()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Alice));
        await flow.StartWithMemberAsync();

        var call = await flow.CaptureServiceAsync();

        Assert.NotEqual(StepExecutionStatus.Completed, call.Response?.Status);
        Assert.Null(call.Response?.TargetProcessorName);
    }

    [Fact]
    public async Task AMemberStepIsRefusedOnceTheMriQuestionsAreAnswered()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        var member = await flow.StartWithMemberAsync();
        (await flow.CaptureServiceAsync()).Completed.ToString();
        await flow.AnswerMriQuestionsAsync();

        var call = await flow.Client.ExecuteAsync("radiology", new RadiologySteps.CaptureMemberStep
        {
            MemberId = member.MemberId,
            MemberEnrollmentId = member.MemberEnrollmentId,
            DateOfService = PriorAuthFlow.DateOfService
        });

        Assert.NotEqual(StepExecutionStatus.Completed, call.Response?.Status);
        Assert.Contains("pro_step_not_available", call.Raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACtRequestAsksToConfirmTheCt()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Carol));
        await flow.StartWithMemberAsync();
        (await flow.CaptureServiceAsync("70450")).Completed.ToString();

        var request = await flow.RequiredInformationAsync(nameof(RadiologySteps.ConfirmCtInsteadOfMriStep));
        var confirmed = (await flow.Client.ExecuteAsync("radiology", new RadiologySteps.ConfirmCtInsteadOfMriStep
        {
            InformationRequestId = request.InformationRequestId,
            ExchangeId = request.ExchangeId,
            Items = Answers.For(request, ("ConfirmCt", "true"))
        })).Completed;

        Assert.Contains(confirmed.AvailableSteps, x => x.Name == nameof(RadiologySteps.CaptureRequestingProviderStep));
    }

    [Fact]
    public async Task AnAnonymousCallerIsRefused()
    {
        var call = await hosts.As(null).ExecuteAsync("intake", new IntakeSteps.StartIntakeStep());

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, call.Status);
    }

    private async Task<History.Data.Entities.PriorAuthRecord> HistoryRecordAsync(Guid processId)
    {
        await using var scope = hosts.History.Services.CreateAsyncScope();
        var history = scope.ServiceProvider.GetRequiredService<HistoryDbContext>();

        return await history.PriorAuthRecords.AsNoTracking().SingleAsync(x => x.ProcessId == processId, TestContext.Current.CancellationToken);
    }
}
