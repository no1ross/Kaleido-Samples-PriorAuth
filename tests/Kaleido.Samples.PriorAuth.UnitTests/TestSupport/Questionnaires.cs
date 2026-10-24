using Kaleido.Samples.PriorAuth.Configuration.Queryable.ViewSources.Views;
using Kaleido.Samples.PriorAuth.Radiology.Process.Steps;

namespace Kaleido.Samples.PriorAuth.UnitTests.TestSupport;

/// <summary>Configured questionnaires and answers for tests.</summary>
public static class Questionnaires
{
    public static QuestionnaireDefinitionView Questionnaire(string id, params QuestionnaireItemView[] items) =>
        new() { QuestionnaireId = id, Version = "1.0.0", Name = id, Title = $"Title of {id}", Items = items };

    public static QuestionnaireItemView Choice(string bindingKey, int order, params string[] values) =>
        new()
        {
            LinkId = bindingKey.ToLowerInvariant(),
            BindingKey = bindingKey,
            Text = $"Question {bindingKey}?",
            Type = "choice",
            Order = order,
            AnswerOptions = [.. values.Select((value, index) => new QuestionnaireAnswerOptionView { Value = value, DisplayText = $"{value} label", Order = index })]
        };

    public static QuestionnaireItemView Item(string bindingKey, string type, int order = 1) =>
        new() { LinkId = bindingKey.ToLowerInvariant(), BindingKey = bindingKey, Text = $"Question {bindingKey}?", Type = type, Order = order };

    /// <summary>Answers as an information step (any one carries them the same way): item id and value pairs.</summary>
    public static CaptureClinicalInfoStep Answers(string requestId, params (string ItemId, string Value)[] answers) =>
        new()
        {
            InformationRequestId = requestId,
            Items = Items(answers)
        };

    public static InformationResponseItem[] Items(params (string ItemId, string Value)[] answers) =>
        [.. answers.Select(x => new InformationResponseItem { ItemId = x.ItemId, Answers = [new InformationAnswer { Value = x.Value }] })];
}
