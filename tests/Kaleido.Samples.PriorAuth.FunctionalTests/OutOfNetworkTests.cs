using System.Text.Json;
using Kaleido.Http.Processor;
using Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;
using Kaleido.Samples.PriorAuth.History.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RadiologySteps = Kaleido.Samples.PriorAuth.Radiology.Process.Steps;

namespace Kaleido.Samples.PriorAuth.FunctionalTests;

/// <summary>An out-of-network servicing facility gets its configured questions; an in-network one does not (#10).</summary>
public sealed class OutOfNetworkTests(PriorAuthHosts hosts)
{
    [Fact]
    public async Task AnOutOfNetworkFacilityAsksWhyBeforeTheClinicalQuestions()
    {
        var flow = await ToServicingProviderAsync();

        var servicing = await flow.CaptureServicingProviderAsync(inNetwork: false);

        Assert.Equal(nameof(RadiologySteps.CaptureOutOfNetworkInfoStep), servicing.RequiredStep?.Name);
        var questions = servicing.RequiredStep?.InformationRequest?.Items.Select(x => x.Id);
        Assert.Equal(["OutOfNetworkReason", "OutOfNetworkReasonDetail", "MemberInformedOfCost"], questions);

        var answered = await flow.AnswerOutOfNetworkQuestionsAsync(("OutOfNetworkReason", "Other"), ("OutOfNetworkReasonDetail", "Closer to the member's home"), ("MemberInformedOfCost", "true"));

        Assert.Equal(nameof(RadiologySteps.CaptureClinicalInfoStep), answered.RequiredStep?.Name);
    }

    [Fact]
    public async Task AnOutOfNetworkRequestIsSubmittedWithItsAnswersInReviewAndFlaggedInHistory()
    {
        var flow = await ToServicingProviderAsync();
        await flow.CaptureServicingProviderAsync(inNetwork: false);
        await flow.AnswerOutOfNetworkQuestionsAsync(("OutOfNetworkReason", "ContinuityOfCare"), ("MemberInformedOfCost", "true"));
        await flow.AnswerClinicalQuestionsAsync();

        var review = (await flow.Client.ExecuteAsync("radiology", new RadiologySteps.ReviewPriorAuthorizationStep())).Completed;
        var outOfNetwork = review.Result.GetProperty("answers").EnumerateArray().Single(x => x.GetProperty("key").GetString() == nameof(RadiologySteps.CaptureOutOfNetworkInfoStep));
        Assert.Equal("Out-of-network facility", outOfNetwork.GetProperty("title").GetString());
        Assert.Contains(outOfNetwork.GetProperty("answers").EnumerateArray(), x => x.GetProperty("answers")[0].GetString() == "Continuity of care (the member is already treated there)");

        await flow.SubmitAsync(review);

        var record = await HistoryRecordAsync(flow.ProcessId);
        Assert.Equal(PriorAuthorizationStatus.Submitted, record.Status);
        Assert.False(record.ServicingProviderInNetwork);
    }

    [Fact]
    public async Task AnInNetworkFacilityGoesStraightToTheClinicalQuestions()
    {
        var flow = await ToServicingProviderAsync();

        var servicing = await flow.CaptureServicingProviderAsync(inNetwork: true);

        Assert.Equal(nameof(RadiologySteps.CaptureClinicalInfoStep), servicing.RequiredStep?.Name);
        Assert.True((await HistoryRecordAsync(flow.ProcessId)).ServicingProviderInNetwork);
    }

    [Fact]
    public async Task NothingElseRunsWhileTheOutOfNetworkQuestionsArePending()
    {
        var flow = await ToServicingProviderAsync();
        await flow.CaptureServicingProviderAsync(inNetwork: false);

        var call = await flow.Client.ExecuteAsync("radiology", new RadiologySteps.ReviewPriorAuthorizationStep());

        Assert.NotEqual(StepExecutionOutcome.Completed, call.Response?.Outcome);
        Assert.Equal(nameof(RadiologySteps.CaptureOutOfNetworkInfoStep), call.Response?.RequiredStep?.Name);
    }

    [Fact]
    public async Task TheHistoryListShowsTheNetworkStatus()
    {
        var flow = await ToServicingProviderAsync();
        await flow.CaptureServicingProviderAsync(inNetwork: false);

        var list = await flow.Client.QueryAsync("history", "PriorAuthRecordQueryContextSource", "PriorAuthRecordListViewSource",
            query: new Kaleido.Http.Queryable.QueryApiBody { Page = new Kaleido.Http.Queryable.QueryApiPage { Size = 100 } });

        var record = list.Results.Single(x => x.GetProperty("processId").GetGuid() == flow.ProcessId);
        Assert.Equal(JsonValueKind.False, record.GetProperty("servicingProviderInNetwork").ValueKind);
    }

    private async Task<PriorAuthFlow> ToServicingProviderAsync()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        await flow.StartWithMemberAsync();
        (await flow.CaptureServiceAsync()).Completed.ToString();
        await flow.AnswerMriQuestionsAsync();
        await flow.CaptureRequestingProviderAsync();
        return flow;
    }

    private async Task<History.Data.Entities.PriorAuthRecord> HistoryRecordAsync(Guid processId)
    {
        await using var scope = hosts.History.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<HistoryDbContext>().PriorAuthRecords
            .AsNoTracking()
            .SingleAsync(x => x.ProcessId == processId, TestContext.Current.CancellationToken);
    }
}
