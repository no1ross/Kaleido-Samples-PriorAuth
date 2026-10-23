using Kaleido.Samples.PriorAuth.Clinical;
using Kaleido.Samples.PriorAuth.Configuration.Queryable.Contexts;
using Kaleido.Samples.PriorAuth.Configuration.Queryable.ViewSources.Parameters;
using Kaleido.Samples.PriorAuth.Configuration.Queryable.ViewSources.Views;
using Kaleido.Samples.PriorAuth.Questionnaires;
using Kaleido.Samples.PriorAuth.UnitTests.TestSupport;
using static Kaleido.Samples.PriorAuth.UnitTests.TestSupport.Questionnaires;

namespace Kaleido.Samples.PriorAuth.UnitTests.Clinical;

public sealed class ConfiguredClinicalPathwayProviderTests
{
    private static readonly ClinicalContext Context = new()
    {
        ProcessId = Guid.NewGuid(),
        StepName = "CaptureClinicalInfoStep",
        PlanId = "PLAN-1",
        LineOfBusiness = "Commercial",
        ProcedureModality = ProcedureModality.Mri,
        ProcedureCodeValue = "73721"
    };

    private readonly FakeKaleido _kaleido = new();
    private readonly Dictionary<string, QuestionnaireDefinitionView> _questionnaires = [];
    private readonly List<ClinicalPathwayTransitionQueryContext> _transitions = [];

    public ConfiguredClinicalPathwayProviderTests()
    {
        _kaleido
            .View<QuestionnaireDefinitionViewParameters, QuestionnaireDefinitionView>(
                "Configuration",
                "QuestionnaireDefinitionQueryContextSource",
                "QuestionnaireDefinitionViewSource",
                parameters => _questionnaires.TryGetValue(parameters?.QuestionnaireId ?? parameters?.StepName ?? string.Empty, out var found) ? [found] : [])
            .Source<ClinicalPathwayTransitionQueryContext>("Configuration", "ClinicalPathwayTransitionQueryContextSource", _ => _transitions);
    }

    [Fact]
    public async Task StartsWithTheQuestionnaireTheStepSelects()
    {
        _questionnaires["CaptureClinicalInfoStep"] = Questionnaire("indication", Choice("ClinicalIndication", 1, "Injury", "Other"));

        var round = await CreateProvider().StartAsync(Context, TestContext.Current.CancellationToken);

        Assert.NotNull(round);
        Assert.Equal("indication", round.RoundId);
        Assert.Equal("ClinicalIndication", round.Request.Items[0].Id);
        var parameters = Assert.IsType<QuestionnaireDefinitionViewParameters>(_kaleido.SentQueries[0].Request);
        Assert.Equal("PLAN-1", parameters.PlanId);
        Assert.Equal(ProcedureModality.Mri, parameters.ProcedureModality);
    }

    [Fact]
    public async Task NoQuestionnaireMeansNoClinicalReview()
    {
        Assert.Null(await CreateProvider().StartAsync(Context, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEqualsTransitionLeadsToItsQuestionnaire()
    {
        _questionnaires["injury"] = Questionnaire("injury", Item("InjuryDate", "date"));
        Transition(when: "ClinicalIndication", op: "Equals", value: "Injury", to: "injury", priority: 20);
        Transition(to: "imaging", priority: 0);

        var next = await ContinueAsync(("ClinicalIndication", "injury"));

        Assert.Equal("injury", next.Next?.RoundId);
    }

    [Fact]
    public async Task WithoutAMatchTheUnconditionalTransitionApplies()
    {
        _questionnaires["imaging"] = Questionnaire("imaging", Item("XrayCompleted", "boolean"));
        Transition(when: "ClinicalIndication", op: "Equals", value: "Injury", to: "injury", priority: 20);
        Transition(to: "imaging", priority: 0);

        var next = await ContinueAsync(("ClinicalIndication", "Other"));

        Assert.Equal("imaging", next.Next?.RoundId);
    }

    [Fact]
    public async Task ATransitionWithoutATargetFinishesWithItsDetermination()
    {
        Transition(when: "RedFlags", op: "NotEquals", value: "None", determination: "UrgentClinicalReview", priority: 30);
        Transition(when: "XrayCompleted", op: "Equals", value: "true", determination: "CriteriaMet", priority: 20);

        var next = await ContinueAsync(("XrayCompleted", "true"), ("RedFlags", "None"));

        Assert.Null(next.Next);
        Assert.Equal("CriteriaMet", next.Determination);
    }

    [Fact]
    public async Task NotEqualsNeedsAnAnswer()
    {
        Transition(when: "RedFlags", op: "NotEquals", value: "None", determination: "UrgentClinicalReview", priority: 30);
        Transition(determination: "ClinicalReviewRequired", priority: 0);

        Assert.Equal("UrgentClinicalReview", (await ContinueAsync(("RedFlags", "Locking"))).Determination);
        Assert.Equal("ClinicalReviewRequired", (await ContinueAsync(("XrayCompleted", "false"))).Determination);
    }

    [Fact]
    public async Task NoTransitionFinishesWithoutADetermination()
    {
        var next = await ContinueAsync(("Anything", "x"));

        Assert.Null(next.Next);
        Assert.Null(next.Determination);
    }

    [Fact]
    public async Task AMissingTargetQuestionnaireFinishesWithTheTransitionsDetermination()
    {
        Transition(to: "missing", determination: "ClinicalReviewRequired", priority: 0);

        var next = await ContinueAsync(("Anything", "x"));

        Assert.Null(next.Next);
        Assert.Equal("ClinicalReviewRequired", next.Determination);
    }

    private Task<ClinicalNext> ContinueAsync(params (string ItemId, string Value)[] answers) =>
        CreateProvider().ContinueAsync(
            Context,
            new ClinicalRound("current", new InformationRequest { InformationRequestId = "current|1.0.0" }),
            Answers("current|1.0.0", answers),
            TestContext.Current.CancellationToken);

    private void Transition(string? when = null, string op = "Equals", string? value = null, string? to = null, string? determination = null, int priority = 0) =>
        _transitions.Add(new ClinicalPathwayTransitionQueryContext
        {
            FromQuestionnaireId = "current",
            WhenBindingKey = when,
            Operator = op,
            AnswerValue = value,
            ToQuestionnaireId = to,
            Determination = determination,
            Priority = priority
        });

    private ConfiguredClinicalPathwayProvider CreateProvider() => new(new ConfiguredQuestionnaireClient(_kaleido));
}
