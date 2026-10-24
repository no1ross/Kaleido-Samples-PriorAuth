using Kaleido.Samples.PriorAuth.Configuration.Queryable.ViewSources.Views;
using Kaleido.Samples.PriorAuth.Questionnaires;
using static Kaleido.Samples.PriorAuth.UnitTests.TestSupport.Questionnaires;

namespace Kaleido.Samples.PriorAuth.UnitTests.Questionnaires;

public sealed class QuestionnaireTranslationTests
{
    [Fact]
    public void TheRequestIdIsTheQuestionnaireAndVersion()
    {
        var request = QuestionnaireTranslation.ToInformationRequest(Questionnaire("mri-basic"));

        Assert.Equal("mri-basic|1.0.0", request.InformationRequestId);
        Assert.Equal("Title of mri-basic", request.Title);
    }

    [Fact]
    public void ItemsAreOrderedAndIdentifiedByTheirBindingKey()
    {
        var request = QuestionnaireTranslation.ToInformationRequest(
            Questionnaire("q", Choice("Contrast", 2, "With", "Without"), Choice("BodyPart", 1, "Knee", "Spine")));

        Assert.Equal(["BodyPart", "Contrast"], request.Items.Select(x => x.Id));
        Assert.Equal(["Knee", "Spine"], request.Items[0].Options.Select(x => x.Value));
        Assert.Equal("Knee label", request.Items[0].Options[0].Display);
    }

    [Fact]
    public void AnItemWithoutABindingKeyIsIdentifiedByItsLinkId()
    {
        var request = QuestionnaireTranslation.ToInformationRequest(
            Questionnaire("q", new QuestionnaireItemView { LinkId = "note", Type = "display", Text = "Read this." }));

        Assert.Equal("note", request.Items[0].Id);
    }

    [Theory]
    [InlineData("choice", InformationItemType.Choice)]
    [InlineData("boolean", InformationItemType.Boolean)]
    [InlineData("integer", InformationItemType.WholeNumber)]
    [InlineData("decimal", InformationItemType.Number)]
    [InlineData("date", InformationItemType.Date)]
    [InlineData("datetime", InformationItemType.DateTime)]
    [InlineData("text", InformationItemType.LongText)]
    [InlineData("display", InformationItemType.Display)]
    [InlineData("group", InformationItemType.Group)]
    [InlineData("string", InformationItemType.Text)]
    [InlineData("BOOLEAN", InformationItemType.Boolean)]
    public void ConfiguredTypesMapToItemTypes(string configured, InformationItemType expected)
    {
        var request = QuestionnaireTranslation.ToInformationRequest(Questionnaire("q", Item("Key", configured)));

        Assert.Equal(expected, request.Items[0].Type);
    }

    [Fact]
    public void TokensAreFilledInTitleAndText()
    {
        var questionnaire = Questionnaire("q", Item("ConfirmCt", "boolean") with { Text = "Procedure {ProcedureCode} is a CT." }) with { Title = "Confirm {procedurecode}" };

        var request = QuestionnaireTranslation.ToInformationRequest(questionnaire, tokens: new Dictionary<string, string> { ["ProcedureCode"] = "70450" });

        Assert.Equal("Confirm 70450", request.Title);
        Assert.Equal("Procedure 70450 is a CT.", request.Items[0].Text);
    }

    [Fact]
    public void AStatedDetailIsProposedWhenItFits()
    {
        var request = QuestionnaireTranslation.ToInformationRequest(
            Questionnaire("q", Choice("BodyPart", 1, "Knee", "Spine"), Item("Urgent", "boolean", 2), Item("Notes", "string", 3)),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["BodyPart"] = "Knee", ["Urgent"] = "true", ["Notes"] = "Left knee" });

        Assert.Equal("Knee", Assert.Single(request.Items[0].ProposedAnswers).Value);
        Assert.Equal("true", Assert.Single(request.Items[1].ProposedAnswers).Value);
        Assert.Equal("Left knee", Assert.Single(request.Items[2].ProposedAnswers).Value);
    }

    [Fact]
    public void AStatedDetailThatDoesNotFitIsNotProposed()
    {
        var request = QuestionnaireTranslation.ToInformationRequest(
            Questionnaire("q", Choice("BodyPart", 1, "Knee", "Spine"), Item("Urgent", "boolean", 2), Item("Heading", "display", 3)),
            new Dictionary<string, string> { ["BodyPart"] = "Elbow", ["Urgent"] = "maybe", ["Heading"] = "x" });

        Assert.All(request.Items, item => Assert.Empty(item.ProposedAnswers));
    }

    [Fact]
    public void AConfiguredDefaultIsNeverProposed()
    {
        var request = QuestionnaireTranslation.ToInformationRequest(
            Questionnaire("q", Choice("BodyPart", 1, "Knee", "Spine") with { DefaultValue = "Spine" }));

        Assert.Empty(request.Items[0].ProposedAnswers);
    }

    [Theory]
    [InlineData("Equals", InformationConditionOperator.Equals)]
    [InlineData("notequals", InformationConditionOperator.NotEquals)]
    [InlineData("=", InformationConditionOperator.Equals)]
    [InlineData("!=", InformationConditionOperator.NotEquals)]
    [InlineData(">", InformationConditionOperator.GreaterThan)]
    [InlineData("<", InformationConditionOperator.LessThan)]
    [InlineData(">=", InformationConditionOperator.GreaterThanOrEqual)]
    [InlineData(" <= ", InformationConditionOperator.LessThanOrEqual)]
    public void ConditionsAreCarriedWithTheirOperator(string configured, InformationConditionOperator expected)
    {
        var request = QuestionnaireTranslation.ToInformationRequest(Questionnaire("q", Conditional(configured)));

        var condition = Assert.Single(request.Items[0].EnableWhen);
        Assert.Equal("BodyPart", condition.Question);
        Assert.Equal(expected, condition.Operator);
        Assert.Equal("Spine", condition.Answer);
    }

    [Fact]
    public void AnUnknownOperatorIsLeftUndefined()
    {
        var request = QuestionnaireTranslation.ToInformationRequest(Questionnaire("q", Conditional("roughly")));

        Assert.False(Enum.IsDefined(request.Items[0].EnableWhen[0].Operator));
    }

    [Fact]
    public void AnswersAreReadByBindingKey()
    {
        var answers = Answers("q|1.0.0", ("BodyPart", "Knee"), ("Contrast", "WithContrast"));

        Assert.Equal("Knee", QuestionnaireTranslation.Answer(answers, "bodypart"));
        Assert.Equal(["WithContrast"], QuestionnaireTranslation.Answers(answers, "Contrast"));
        Assert.Null(QuestionnaireTranslation.Answer(answers, "Laterality"));
        Assert.Empty(QuestionnaireTranslation.Answers(answers, "Laterality"));
    }

    private static QuestionnaireItemView Conditional(string @operator) =>
        Choice("Laterality", 1, "Left", "Right") with
        {
            EnableWhen = [new QuestionnaireEnableWhenView { QuestionBindingKey = "BodyPart", Operator = @operator, AnswerValue = "Spine" }]
        };
}
