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
    [Fact(Skip = "Handler calls go out as the service, not on behalf of the caller: no1ross/Kaleido#428")]
    public async Task BobSubmitsAnMriRequestFromIntakeToRadiology()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        var member = await flow.StartWithMemberAsync();

        var handoff = (await flow.CaptureServiceAsync()).Completed;
        Assert.Equal("radiology", handoff.TargetProcessorName);

        await flow.AnswerMriQuestionsAsync();
        await flow.CaptureRequestingProviderAsync();

        var servicing = await flow.CaptureServicingProviderAsync();
        Assert.Equal(nameof(RadiologySteps.CaptureClinicalInfoStep), servicing.RequiredStep?.Name);

        await flow.AnswerClinicalQuestionsAsync();
        var submitted = await flow.ReviewAndSubmitAsync();

        Assert.Equal(StepExecutionOutcome.Completed, submitted.Outcome);

        var record = await HistoryRecordAsync(flow.ProcessId);
        Assert.Equal(PriorAuthorizationStatus.Submitted, record.Status);
        Assert.Equal("radiology", record.ProcessorName);
        Assert.Equal(member.MemberNumber, record.MemberNumber);
        Assert.NotNull(record.PriorAuthorizationId);
        Assert.Equal("bob", record.UpdatedBy);
        Assert.Equal("radiology", record.UpdatedVia);
    }

    [Fact(Skip = "Handler calls go out as the service, not on behalf of the caller: no1ross/Kaleido#428")]
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

        Assert.NotEqual(StepExecutionOutcome.Completed, call.Response?.Outcome);
        Assert.Null(call.Response?.TargetProcessorName);
    }

    [Fact(Skip = "Handler calls go out as the service, not on behalf of the caller: no1ross/Kaleido#428")]
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

        Assert.NotEqual(StepExecutionOutcome.Completed, call.Response?.Outcome);
        Assert.Contains("pro_step_not_available", call.Raw, StringComparison.Ordinal);
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
