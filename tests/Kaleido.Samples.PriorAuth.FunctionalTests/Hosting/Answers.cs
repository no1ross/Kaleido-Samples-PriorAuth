using System.Globalization;

namespace Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;

/// <summary>
/// Answers an information request as a caller would: the given answer where one is given, a plain
/// default otherwise, and only the questions that apply (their conditions hold on the answers so far).
/// </summary>
public static class Answers
{
    public static IReadOnlyList<InformationResponseItem> For(InformationRequest request, params (string ItemId, string Value)[] given)
    {
        var chosen = given.ToDictionary(x => x.ItemId, x => x.Value, StringComparer.OrdinalIgnoreCase);
        var answered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<InformationResponseItem>();

        foreach (var item in request.Items)
        {
            if (item.Type is InformationItemType.Display or InformationItemType.Group || !Applies(item, answered))
            {
                continue;
            }

            var value = chosen.TryGetValue(item.Id, out var given1) ? given1 : Default(item);
            answered[item.Id] = value;
            items.Add(new InformationResponseItem { ItemId = item.Id, Answers = [new InformationAnswer { Value = value }] });
        }

        return items;
    }

    private static bool Applies(InformationItem item, Dictionary<string, string> answered) =>
        item.EnableWhen.All(condition =>
            answered.TryGetValue(condition.Question, out var answer) &&
            condition.Operator switch
            {
                InformationConditionOperator.Equals => string.Equals(answer, condition.Answer, StringComparison.OrdinalIgnoreCase),
                InformationConditionOperator.NotEquals => !string.Equals(answer, condition.Answer, StringComparison.OrdinalIgnoreCase),
                _ => true
            });

    private static string Default(InformationItem item) =>
        item.Type switch
        {
            InformationItemType.Choice => item.Options[0].Value,
            InformationItemType.Boolean => "true",
            InformationItemType.WholeNumber or InformationItemType.Number => "6",
            InformationItemType.Date => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => "Test answer"
        };
}
