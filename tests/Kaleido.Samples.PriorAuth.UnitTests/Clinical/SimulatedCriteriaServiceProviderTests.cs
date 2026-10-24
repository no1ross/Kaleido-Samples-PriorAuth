using Kaleido.Samples.PriorAuth.Clinical;
using static Kaleido.Samples.PriorAuth.UnitTests.TestSupport.Questionnaires;

namespace Kaleido.Samples.PriorAuth.UnitTests.Clinical;

public sealed class SimulatedCriteriaServiceProviderTests
{
    private static readonly ClinicalContext Context = new() { ProcessId = Guid.NewGuid(), StepName = "CaptureClinicalInfoStep" };

    private readonly SimulatedCriteriaServiceProvider _provider = new();

    [Fact]
    public async Task StartsWithTheReasonInItsOwnSession()
    {
        var round = await _provider.StartAsync(Context, TestContext.Current.CancellationToken);

        Assert.NotNull(round);
        Assert.Equal("reason", round.RoundId);
        Assert.StartsWith("vendor-", round.SessionId, StringComparison.Ordinal);
        Assert.Equal(InformationItemType.Choice, round.Request.Items[0].Type);
        Assert.Equal(3, round.Request.Items[0].Options.Count);
    }

    [Theory]
    [InlineData("reason", "trauma", "trauma-weight", null)]
    [InlineData("reason", "chronic", "chronic-therapy", null)]
    [InlineData("reason", "other", "xray", null)]
    [InlineData("trauma-weight", "false", null, "CriteriaMet")]
    [InlineData("trauma-weight", "true", "xray", null)]
    [InlineData("chronic-therapy", "4", null, "CriteriaNotMet")]
    [InlineData("chronic-therapy", "8", "xray", null)]
    [InlineData("xray", "true", null, "CriteriaMet")]
    [InlineData("xray", "false", null, "ClinicalReviewRequired")]
    public async Task FollowsTheVendorsDecisionTree(string round, string answer, string? next, string? determination)
    {
        var result = await _provider.ContinueAsync(
            Context,
            new ClinicalRound(round, new InformationRequest { InformationRequestId = $"vendor-criteria|{round}" }, "vendor-1"),
            Answers($"vendor-criteria|{round}", (round, answer)),
            TestContext.Current.CancellationToken);

        Assert.Equal(next, result.Next?.RoundId);
        Assert.Equal(determination, result.Determination);

        if (result.Next is { } nextRound)
        {
            Assert.Equal("vendor-1", nextRound.SessionId);
        }
    }
}
