using Kaleido.Samples.PriorAuth.Radiology.Process.Models;
using Kaleido.Samples.PriorAuth.Radiology.Process.Services;
using static Kaleido.Samples.PriorAuth.UnitTests.TestSupport.Questionnaires;

namespace Kaleido.Samples.PriorAuth.UnitTests.Radiology;

public sealed class ConfiguredInformationRequestsTests : IDisposable
{
    private const string Step = "CaptureMriInfoStep";

    private readonly RadiologyTestBed _bed = new();

    public void Dispose() => _bed.Dispose();

    [Fact]
    public async Task NoQuestionnaireMeansNoRequest()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        Assert.Null(await CreateAsync());
        Assert.Empty(_bed.Database.NewContext().PriorAuthorizationQuestionnaireAssignments);
    }

    [Fact]
    public async Task TheConfiguredRequestIsSelectedForTheRequestAndKept()
    {
        _bed.QuestionnairesByStep[Step] = Questionnaire("mri-basic", Choice("BodyPart", 1, "Knee", "Spine"));
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var request = await CreateAsync();

        Assert.Equal("mri-basic|1.0.0", request?.InformationRequestId);
        var parameters = _bed.Kaleido.SentQueries.Select(x => x.Request).OfType<Configuration.Queryable.ViewSources.Parameters.QuestionnaireDefinitionViewParameters>().Single();
        Assert.Equal("PLAN-1", parameters.PlanId);
        Assert.Equal("Commercial", parameters.LineOfBusiness);
        Assert.Equal("73721", parameters.ProcedureCodeValue);
        var assignment = Assert.Single(_bed.Database.NewContext().PriorAuthorizationQuestionnaireAssignments);
        Assert.Equal(Step, assignment.StepName);
        Assert.Equal("mri-basic", assignment.QuestionnaireId);
        Assert.Null(assignment.AnswersJson);
    }

    [Fact]
    public async Task WhatTheCallerStatedIsProposed()
    {
        _bed.QuestionnairesByStep[Step] = Questionnaire("mri-basic", Choice("BodyPart", 1, "Knee", "Spine"), Choice("Contrast", 2, "WithContrast", "WithoutContrast"));
        await _bed.AddPriorAuthorizationAsync(StatedServiceDetails.ToJson(MriBodyPart.Knee, null, ContrastOption.Unknown), "73721");

        var request = await CreateAsync();

        Assert.Equal("Knee", Assert.Single(request!.Items[0].ProposedAnswers).Value);
        Assert.Empty(request.Items[1].ProposedAnswers);
    }

    [Fact]
    public async Task AnswersAreKeptWithTheirQuestionsAndOptionLabels()
    {
        _bed.QuestionnairesByStep[Step] = Questionnaire("mri-basic", Choice("BodyPart", 1, "Knee", "Spine"));
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");
        var informationRequests = _bed.InformationRequests();
        await CreateAsync(informationRequests);

        await informationRequests.RecordAnswersAsync(_bed.Context.ProcessId, Step, Answers("mri-basic|1.0.0", ("BodyPart", "Knee"), ("Extra", "free text")), RadiologyTestBed.Token);

        var answered = Assert.Single(await informationRequests.AnswersAsync(_bed.Context.ProcessId, RadiologyTestBed.Token));
        Assert.Equal(Step, answered.Key);
        Assert.Equal("Title of mri-basic", answered.Title);
        Assert.Equal("BodyPart", answered.Answers[0].ItemId);
        Assert.Equal("Question BodyPart?", answered.Answers[0].Question);
        Assert.Equal(["Knee label"], answered.Answers[0].Answers);
        Assert.Equal("Extra", answered.Answers[1].Question);
        Assert.Equal(["free text"], answered.Answers[1].Answers);
    }

    [Fact]
    public async Task PresentingTheRequestAgainClearsItsAnswers()
    {
        _bed.QuestionnairesByStep[Step] = Questionnaire("mri-basic", Choice("BodyPart", 1, "Knee", "Spine"));
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");
        var informationRequests = _bed.InformationRequests();
        await CreateAsync(informationRequests);
        await informationRequests.RecordAnswersAsync(_bed.Context.ProcessId, Step, Answers("mri-basic|1.0.0", ("BodyPart", "Knee")), RadiologyTestBed.Token);

        await CreateAsync(informationRequests);

        Assert.Empty(await informationRequests.AnswersAsync(_bed.Context.ProcessId, RadiologyTestBed.Token));
    }

    [Fact]
    public async Task AnswersToARequestNeverPresentedAreNotKept()
    {
        var informationRequests = _bed.InformationRequests();

        await informationRequests.RecordAnswersAsync(_bed.Context.ProcessId, Step, Answers("x|1", ("BodyPart", "Knee")), RadiologyTestBed.Token);

        Assert.Empty(await informationRequests.AnswersAsync(_bed.Context.ProcessId, RadiologyTestBed.Token));
    }

    [Fact]
    public void StatedDetailsKeepOnlyWhatWasStated()
    {
        Assert.Null(StatedServiceDetails.ToJson(null, Laterality.Unknown, null));
        Assert.Equal("{\"BodyPart\":\"Knee\",\"Laterality\":\"Left\"}", StatedServiceDetails.ToJson(MriBodyPart.Knee, Laterality.Left, null));
    }

    private Task<InformationRequest?> CreateAsync(ConfiguredInformationRequests? informationRequests = null) =>
        (informationRequests ?? _bed.InformationRequests()).CreateAsync(_bed.Context.ProcessId, Step, ProcedureModality.Mri, "73721", RadiologyTestBed.Token);
}
