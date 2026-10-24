using Kaleido.Http.Processor;
using Kaleido.Samples.PriorAuth.Configuration.Queryable.Contexts;
using Kaleido.Samples.PriorAuth.History.Process.Steps;
using Kaleido.Samples.PriorAuth.Radiology.Process.Handlers;
using Kaleido.Samples.PriorAuth.Radiology.Process.Services;
using Kaleido.Samples.PriorAuth.Radiology.Process.Steps;
using Microsoft.EntityFrameworkCore;
using static Kaleido.Samples.PriorAuth.UnitTests.TestSupport.Questionnaires;

namespace Kaleido.Samples.PriorAuth.UnitTests.Radiology;

/// <summary>Radiology's step handlers whose outcome depends on the answers or the request's state.</summary>
public sealed class RadiologyHandlersTests : IDisposable
{
    private readonly RadiologyTestBed _bed = new();

    public void Dispose() => _bed.Dispose();

    [Fact]
    public async Task SubmitWithoutTheAttestationIsRejectedAndTheRequestStaysADraft()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await SubmitAsync("false");

        Assert.True(result.Rejected);
        Assert.Contains(result.Messages, x => x.Code == "ATTESTATION_REQUIRED");
        Assert.Equal(PriorAuthorizationStatus.Draft, (await _bed.Database.NewContext().PriorAuthorizations.SingleAsync(RadiologyTestBed.Token)).Status);
    }

    [Fact]
    public async Task SubmitWithTheAttestationSubmitsAndRecordsIt()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await SubmitAsync("true");

        Assert.True(result.Succeeded);
        Assert.Contains(result.Messages, x => x.Code == "PRIOR_AUTHORIZATION_SUBMITTED");
        var saved = await _bed.Database.NewContext().PriorAuthorizations.SingleAsync(RadiologyTestBed.Token);
        Assert.Equal(PriorAuthorizationStatus.Submitted, saved.Status);
        Assert.NotNull(saved.SubmittedUtc);
        Assert.Equal(PriorAuthorizationStatus.Submitted, Assert.IsType<UpsertPriorAuthRecordStep>(Assert.Single(_bed.Kaleido.SentSteps).Step).Status);
    }

    [Fact]
    public async Task SubmitIsRolledBackWhenHistoryDoesNotRecord()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");
        _bed.HistoryOutcome = StepExecutionOutcome.Failed;

        var result = await SubmitAsync("true");

        Assert.Contains(result.Messages, x => x.Code == "HISTORY_NOT_RECORDED");
        Assert.Equal(PriorAuthorizationStatus.Draft, (await _bed.Database.NewContext().PriorAuthorizations.SingleAsync(RadiologyTestBed.Token)).Status);
    }

    [Fact]
    public async Task RemovingAnUnknownServiceSaysSoAndChangesNothing()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await RemoveAsync(Guid.NewGuid());

        Assert.True(result.Succeeded);
        Assert.Contains(result.Messages, x => x.Code == "REQUESTED_SERVICE_NOT_FOUND");
        Assert.Single(_bed.Database.NewContext().PriorAuthorizationRequestedServices);
    }

    [Fact]
    public async Task RemovingTheLastServiceRequiresANewOne()
    {
        var priorAuthorization = await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await RemoveAsync(priorAuthorization.RequestedServices.Single().PriorAuthorizationRequestedServiceId);

        Assert.Equal(typeof(CaptureRequestedServiceStep), result.RequiredStep);
        Assert.Empty(_bed.Database.NewContext().PriorAuthorizationRequestedServices);
    }

    [Fact]
    public async Task RemovingOneOfSeveralServicesRequiresNothing()
    {
        var priorAuthorization = await _bed.AddPriorAuthorizationAsync(null, "73721", "73722");

        var result = await RemoveAsync(priorAuthorization.RequestedServices.First().PriorAuthorizationRequestedServiceId);

        Assert.True(result.Succeeded);
        Assert.Null(result.RequiredStep);
        Assert.Single(_bed.Database.NewContext().PriorAuthorizationRequestedServices);
    }

    [Fact]
    public async Task AnAnswerThatIsNotAnOptionIsRejected()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await MriInfoAsync(("BodyPart", "Elbow"), ("Contrast", "WithoutContrast"));

        Assert.True(result.Rejected);
        Assert.Contains(result.Messages, x => x.Code == "UNEXPECTED_ANSWER");
    }

    [Fact]
    public async Task TheMriAnswersResolveTheProcedureCode()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");
        _bed.MriRules.Add(new MriProcedureCodeRuleQueryContext { BodyPart = MriBodyPart.Knee, Laterality = Laterality.Left, Contrast = ContrastOption.WithContrast, ResolvedCodeValue = "73722", ResolvedCodeSystem = ProcedureCodeSystem.Cpt });

        var result = await MriInfoAsync(("BodyPart", "Knee"), ("Laterality", "Left"), ("Contrast", "WithContrast"));

        Assert.True(result.Succeeded);
        Assert.Contains(result.Messages, x => x.Code == "PROCEDURE_CODE_UPDATED");
        var service = await _bed.Database.NewContext().PriorAuthorizationRequestedServices.SingleAsync(RadiologyTestBed.Token);
        Assert.Equal("73722", service.ResolvedCodeValue);
        Assert.Equal("MRI Knee", service.Description);
    }

    [Fact]
    public async Task ASpineMriHasNoSide()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "72141");

        var result = await MriInfoAsync(("BodyPart", "Spine"), ("Contrast", "WithoutContrast"));

        Assert.True(result.Succeeded);
        Assert.Empty(result.Messages);
        Assert.Equal("72141", (await _bed.Database.NewContext().PriorAuthorizationRequestedServices.SingleAsync(RadiologyTestBed.Token)).ResolvedCodeValue);
    }

    private Task<ProcessStepHandlerResult> SubmitAsync(string attested) =>
        new SubmitPriorAuthorizationHandler(_bed.Db, _bed.InformationRequests(), _bed.HistoryClient())
            .ExecuteAsync(new SubmitPriorAuthorizationStep { InformationRequestId = "attestation|1.0.0", Items = Items(("Attested", attested)) }, _bed.Context, RadiologyTestBed.Token);

    private Task<ProcessStepHandlerResult> RemoveAsync(Guid requestedServiceId) =>
        new RemoveRequestedServiceHandler(_bed.Db, _bed.HistoryClient())
            .ExecuteAsync(new RemoveRequestedServiceStep { PriorAuthorizationRequestedServiceId = requestedServiceId }, _bed.Context, RadiologyTestBed.Token);

    private Task<ProcessStepHandlerResult> MriInfoAsync(params (string ItemId, string Value)[] answers) =>
        new CaptureMriInfoHandler(_bed.Db, new MriProcedureCodeResolverClient(_bed.Kaleido), _bed.InformationRequests())
            .ExecuteAsync(new CaptureMriInfoStep { InformationRequestId = "mri-basic|1.0.0", Items = Items(answers) }, _bed.Context, RadiologyTestBed.Token);
}
