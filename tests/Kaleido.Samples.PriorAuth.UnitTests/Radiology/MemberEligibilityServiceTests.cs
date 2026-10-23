using Kaleido.Samples.PriorAuth.Radiology.Process.Services;
using Kaleido.Samples.PriorAuth.Radiology.Process.Steps;
using static Kaleido.Samples.PriorAuth.UnitTests.TestSupport.Questionnaires;

namespace Kaleido.Samples.PriorAuth.UnitTests.Radiology;

public sealed class MemberEligibilityServiceTests : IDisposable
{
    private static readonly DateOnly DateOfService = new(2026, 11, 2);

    private readonly RadiologyTestBed _bed = new();

    public void Dispose() => _bed.Dispose();

    [Fact]
    public async Task AnActiveMemberIsEligible()
    {
        var member = _bed.AddMember();

        var result = await CheckAsync(member.MemberId, member.MemberEnrollmentId);

        Assert.True(result.Succeeded);
        Assert.Same(member, result.MemberDetails);
    }

    [Fact]
    public async Task AnUnknownMemberIsNotFound()
    {
        var result = await CheckAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal("MEMBER_NOT_FOUND", result.FailureMessage?.Code);
    }

    [Fact]
    public async Task CoverageThatStartsAfterTheDateOfServiceIsNotYetEffective()
    {
        var member = _bed.AddMember(effective: DateOfService.AddDays(1));

        var result = await CheckAsync(member.MemberId, member.MemberEnrollmentId);

        Assert.Equal("COVERAGE_NOT_YET_EFFECTIVE", result.FailureMessage?.Code);
    }

    [Fact]
    public async Task CoverageThatEndedBeforeTheDateOfServiceIsTerminated()
    {
        var member = _bed.AddMember(terminated: DateOfService.AddDays(-1));

        var result = await CheckAsync(member.MemberId, member.MemberEnrollmentId);

        Assert.Equal("COVERAGE_TERMINATED", result.FailureMessage?.Code);
    }

    [Fact]
    public async Task CoverageEndingOnTheDateOfServiceStillCovers()
    {
        var member = _bed.AddMember(terminated: DateOfService);

        Assert.True((await CheckAsync(member.MemberId, member.MemberEnrollmentId)).Succeeded);
    }

    [Fact]
    public async Task ValidatingNeedsTheRecordedRequest()
    {
        var member = _bed.AddMember();

        var result = await _bed.Eligibility().ValidateAsync(member.MemberId, member.MemberEnrollmentId, DateOfService, _bed.Context.ProcessId, RadiologyTestBed.Token);

        Assert.Equal("PRIOR_AUTHORIZATION_NOT_FOUND", result.FailureMessage?.Code);
    }

    [Fact]
    public async Task AnMriRoutesToTheConfiguredMriQuestions()
    {
        _bed.QuestionnairesByStep[nameof(CaptureMriInfoStep)] = Questionnaire("mri-basic", Choice("BodyPart", 1, "Knee", "Spine"));
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await RouteAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(ModalityRoute.CaptureMriInfo, result.Route);
        Assert.Equal("mri-basic|1.0.0", result.InformationRequest?.InformationRequestId);
    }

    [Fact]
    public async Task ACtRoutesToTheConfiguredCtConfirmation()
    {
        _bed.QuestionnairesByStep[nameof(ConfirmCtInsteadOfMriStep)] = Questionnaire("ct-confirmation", Item("ConfirmCt", "boolean"));
        await _bed.AddPriorAuthorizationAsync(codeValues: "70450");

        var result = await RouteAsync();

        Assert.Equal(ModalityRoute.ConfirmCtInsteadOfMri, result.Route);
    }

    [Fact]
    public async Task AModalityWithoutQuestionsIsNotConfigured()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "73721");

        var result = await RouteAsync();

        Assert.Equal("INFORMATION_REQUEST_NOT_CONFIGURED", result.FailureMessage?.Code);
    }

    [Fact]
    public async Task AnotherModalityIsNotSupported()
    {
        await _bed.AddPriorAuthorizationAsync(codeValues: "99213");

        var result = await RouteAsync();

        Assert.Equal("MODALITY_NOT_SUPPORTED", result.FailureMessage?.Code);
    }

    [Fact]
    public async Task RoutingNeedsARequestedService()
    {
        var result = await RouteAsync();

        Assert.Equal("PRIOR_AUTHORIZATION_NOT_FOUND", result.FailureMessage?.Code);
    }

    private Task<MemberEligibilityResult> CheckAsync(Guid memberId, Guid enrollmentId) =>
        _bed.Eligibility().CheckEligibilityAsync(memberId, enrollmentId, DateOfService, RadiologyTestBed.Token);

    private Task<ModalityRoutingResult> RouteAsync() =>
        _bed.Eligibility().RouteByModalityAsync(_bed.Context.ProcessId, _bed.AddMember(), RadiologyTestBed.Token);
}
