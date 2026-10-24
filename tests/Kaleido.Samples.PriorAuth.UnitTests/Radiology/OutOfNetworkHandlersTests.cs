using Kaleido.Samples.PriorAuth.Clinical;
using Kaleido.Samples.PriorAuth.History.Process.Steps;
using Kaleido.Samples.PriorAuth.Provider.Queryable.Parameters;
using Kaleido.Samples.PriorAuth.Provider.Queryable.ViewSources.Views;
using Kaleido.Samples.PriorAuth.Radiology.Process.Handlers;
using Kaleido.Samples.PriorAuth.Radiology.Process.Services;
using Kaleido.Samples.PriorAuth.Radiology.Process.Steps;
using Microsoft.EntityFrameworkCore;
using static Kaleido.Samples.PriorAuth.UnitTests.TestSupport.Questionnaires;

namespace Kaleido.Samples.PriorAuth.UnitTests.Radiology;

/// <summary>The servicing facility's network status decides whether the out-of-network questions come first (#10).</summary>
public sealed class OutOfNetworkHandlersTests : IDisposable
{
    private readonly RadiologyTestBed _bed = new();
    private readonly Mock<IClinicalQuestionProvider> _clinical = new();
    private readonly List<RequestingProviderSearchView> _facilities = [];

    public OutOfNetworkHandlersTests()
    {
        _bed.Kaleido.View<RequestingProviderSearchParameters, RequestingProviderSearchView>("Provider", "RequestingProviderQueryContextSource", "RequestingProviderSearchViewSource", _ => _facilities);
        _clinical.Setup(x => x.StartAsync(It.IsAny<ClinicalContext>(), It.IsAny<CancellationToken>())).ReturnsAsync((ClinicalRound?)null);
    }

    public void Dispose() => _bed.Dispose();

    [Fact]
    public async Task AnOutOfNetworkFacilityRequiresTheConfiguredQuestions()
    {
        _bed.QuestionnairesByStep[nameof(CaptureOutOfNetworkInfoStep)] = Questionnaire("out-of-network", Choice("OutOfNetworkReason", 1, "MemberChoice", "Other"));
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await CaptureFacilityAsync(inNetwork: false);

        Assert.Equal(typeof(CaptureOutOfNetworkInfoStep), result.RequiredStep);
        Assert.Equal("out-of-network|1.0.0", result.InformationRequest?.InformationRequestId);
        _clinical.Verify(x => x.StartAsync(It.IsAny<ClinicalContext>(), It.IsAny<CancellationToken>()), Times.Never);
        var servicing = await _bed.Database.NewContext().PriorAuthorizationServicingProviders.SingleAsync(RadiologyTestBed.Token);
        Assert.False(servicing.IsInNetwork);
        Assert.False(Assert.IsType<UpsertPriorAuthRecordStep>(_bed.Kaleido.SentSteps.Last().Step).ServicingProviderInNetwork);
    }

    [Fact]
    public async Task WithoutConfiguredQuestionsAnOutOfNetworkFacilityGoesOnToTheClinicalReview()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await CaptureFacilityAsync(inNetwork: false);

        Assert.Equal(typeof(ReviewPriorAuthorizationStep), result.RequiredStep);
        _clinical.Verify(x => x.StartAsync(It.IsAny<ClinicalContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnInNetworkFacilityIsNeverAskedTheOutOfNetworkQuestions()
    {
        _bed.QuestionnairesByStep[nameof(CaptureOutOfNetworkInfoStep)] = Questionnaire("out-of-network", Choice("OutOfNetworkReason", 1, "MemberChoice", "Other"));
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await CaptureFacilityAsync(inNetwork: true);

        Assert.Equal(typeof(ReviewPriorAuthorizationStep), result.RequiredStep);
        Assert.Empty(_bed.Database.NewContext().PriorAuthorizationQuestionnaireAssignments);
        Assert.True(Assert.IsType<UpsertPriorAuthRecordStep>(_bed.Kaleido.SentSteps.Last().Step).ServicingProviderInNetwork);
    }

    [Fact]
    public async Task TheAnswersAreKeptAndTheClinicalReviewStarts()
    {
        _bed.QuestionnairesByStep[nameof(CaptureOutOfNetworkInfoStep)] = Questionnaire("out-of-network", Choice("OutOfNetworkReason", 1, "MemberChoice", "Other"));
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");
        await CaptureFacilityAsync(inNetwork: false);
        var round = new ClinicalRound("indication", new InformationRequest { InformationRequestId = "indication|1.0.0" });
        _clinical.Setup(x => x.StartAsync(It.IsAny<ClinicalContext>(), It.IsAny<CancellationToken>())).ReturnsAsync(round);

        var result = await new CaptureOutOfNetworkInfoHandler(_bed.InformationRequests(), ClinicalReview())
            .ExecuteAsync(new CaptureOutOfNetworkInfoStep { InformationRequestId = "out-of-network|1.0.0", Items = Items(("OutOfNetworkReason", "MemberChoice")) }, _bed.Context, RadiologyTestBed.Token);

        Assert.Equal(typeof(CaptureClinicalInfoStep), result.RequiredStep);
        var answered = await _bed.InformationRequests().AnswersAsync(_bed.Context.ProcessId, RadiologyTestBed.Token);
        var outOfNetwork = Assert.Single(answered, x => x.Key == nameof(CaptureOutOfNetworkInfoStep));
        Assert.Equal(["MemberChoice label"], outOfNetwork.Answers[0].Answers);
    }

    private async Task<ProcessStepHandlerResult> CaptureFacilityAsync(bool inNetwork)
    {
        var facility = new RequestingProviderSearchView { ProviderId = Guid.NewGuid(), ProviderLocationId = Guid.NewGuid(), ProviderName = "Imaging Center", LocationName = "Main", IsInNetwork = inNetwork };
        _facilities.Add(facility);

        return await new CaptureServicingProviderHandler(_bed.Db, new RequestingProviderSearchClient(_bed.Kaleido, _bed.Db), ClinicalReview(), _bed.InformationRequests(), _bed.HistoryClient())
            .ExecuteAsync(new CaptureServicingProviderStep { ProviderId = facility.ProviderId, ProviderLocationId = facility.ProviderLocationId }, _bed.Context, RadiologyTestBed.Token);
    }

    private RadiologyClinicalReview ClinicalReview() =>
        new(_clinical.Object, _bed.Db, new ProcedureModalityClient(_bed.Kaleido), _bed.InformationRequests());
}
