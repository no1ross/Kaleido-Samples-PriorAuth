using System.Text.Json;
using Kaleido.Http.Processor;
using Kaleido.Http.Queryable;
using IntakeSteps = Kaleido.Samples.PriorAuth.Intake.Process.Steps;
using RadiologySteps = Kaleido.Samples.PriorAuth.Radiology.Process.Steps;

namespace Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;

/// <summary>The steps of a request, from Intake to submission in Radiology, as a caller takes them.</summary>
public sealed class PriorAuthFlow(SampleClient client)
{
    /// <summary>A date of service every active enrollment covers.</summary>
    public static readonly DateOnly DateOfService = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);

    public SampleClient Client => client;

    public Guid ProcessId => client.ProcessId ?? throw new InvalidOperationException("No process yet.");

    /// <summary>Starts the intake and captures an active member: the request then waits for its service.</summary>
    public async Task<MemberMatch> StartWithMemberAsync()
    {
        (await client.ExecuteAsync("intake", new IntakeSteps.StartIntakeStep())).Completed.ToString();

        var member = await FindActiveMemberAsync();

        var validated = (await client.ExecuteAsync("intake", new IntakeSteps.ValidateMemberStep
        {
            MemberId = member.MemberId,
            MemberEnrollmentId = member.MemberEnrollmentId,
            DateOfService = DateOfService
        })).Completed;

        Assert.Equal(nameof(IntakeSteps.CaptureMemberStep), validated.RequiredStep?.Name);

        (await client.ExecuteAsync("intake", new IntakeSteps.CaptureMemberStep
        {
            MemberId = member.MemberId,
            MemberEnrollmentId = member.MemberEnrollmentId,
            DateOfService = DateOfService
        })).Completed.ToString();

        return member;
    }

    /// <summary>Captures the requested service in Intake (the call that hands the request off).</summary>
    public Task<StepCall> CaptureServiceAsync(string codeValue = "73721") =>
        client.ExecuteAsync("intake", new IntakeSteps.CaptureRequestedServiceStep { CodeValue = codeValue, CodeSystem = ProcedureCodeSystem.Cpt });

    /// <summary>Answers the MRI questions Radiology requires after the handoff of an MRI knee.</summary>
    public async Task AnswerMriQuestionsAsync()
    {
        var request = await RequiredInformationAsync(nameof(RadiologySteps.CaptureMriInfoStep));

        (await client.ExecuteAsync("radiology", new RadiologySteps.CaptureMriInfoStep
        {
            InformationRequestId = request.InformationRequestId,
            Items = Answers.For(request, ("BodyPart", "Knee"), ("Laterality", "Left"), ("Contrast", "WithoutContrast"))
        })).Completed.ToString();
    }

    public async Task CaptureRequestingProviderAsync()
    {
        var provider = await SearchProvidersAsync("RequestingProviderSearchSource", _ => true);

        (await client.ExecuteAsync("radiology", new RadiologySteps.CaptureRequestingProviderStep
        {
            ProviderId = provider.GetProperty("providerId").GetGuid(),
            ProviderLocationId = provider.GetProperty("providerLocationId").GetGuid()
        })).Completed.ToString();
    }

    /// <summary>Captures a servicing facility in or out of network; returns the step's answer.</summary>
    public async Task<StepExecutionResponse<JsonElement>> CaptureServicingProviderAsync(bool inNetwork = true)
    {
        var facility = await SearchProvidersAsync("ServicingProviderSearchSource", x => x.GetProperty("isInNetwork").GetBoolean() == inNetwork);

        return (await client.ExecuteAsync("radiology", new RadiologySteps.CaptureServicingProviderStep
        {
            ProviderId = facility.GetProperty("providerId").GetGuid(),
            ProviderLocationId = facility.GetProperty("providerLocationId").GetGuid()
        })).Completed;
    }

    /// <summary>Answers the out-of-network questions Radiology requires after an out-of-network facility.</summary>
    public async Task<StepExecutionResponse<JsonElement>> AnswerOutOfNetworkQuestionsAsync(params (string ItemId, string Value)[] answers)
    {
        var request = await RequiredInformationAsync(nameof(RadiologySteps.CaptureOutOfNetworkInfoStep));

        return (await client.ExecuteAsync("radiology", new RadiologySteps.CaptureOutOfNetworkInfoStep
        {
            InformationRequestId = request.InformationRequestId,
            Items = Answers.For(request, answers)
        })).Completed;
    }

    /// <summary>Answers every clinical round (X-ray done, no red flags) until the clinical review is done.</summary>
    public async Task AnswerClinicalQuestionsAsync()
    {
        while ((await client.StateAsync("radiology")).RequiredStep is { Name: nameof(RadiologySteps.CaptureClinicalInfoStep), InformationRequest: { } request })
        {
            (await client.ExecuteAsync("radiology", new RadiologySteps.CaptureClinicalInfoStep
            {
                InformationRequestId = request.InformationRequestId,
                Items = Answers.For(request, ("ClinicalIndication", "Other"), ("XrayCompleted", "true"), ("RedFlags", "None"))
            })).Completed.ToString();
        }
    }

    /// <summary>Reviews the request and confirms the attestation.</summary>
    public async Task<StepExecutionResponse<JsonElement>> ReviewAndSubmitAsync() =>
        await SubmitAsync((await client.ExecuteAsync("radiology", new RadiologySteps.ReviewPriorAuthorizationStep())).Completed);

    /// <summary>Confirms the attestation <paramref name="review"/> presented.</summary>
    public async Task<StepExecutionResponse<JsonElement>> SubmitAsync(StepExecutionResponse<JsonElement> review)
    {
        var attestation = review.RequiredStep?.InformationRequest ?? throw new InvalidOperationException("Review required no attestation.");

        return (await client.ExecuteAsync("radiology", new RadiologySteps.SubmitPriorAuthorizationStep
        {
            InformationRequestId = attestation.InformationRequestId,
            Items = Answers.For(attestation, ("Attested", "true"))
        })).Completed;
    }

    /// <summary>The information request Radiology requires next, for <paramref name="stepName"/>.</summary>
    public async Task<InformationRequest> RequiredInformationAsync(string stepName)
    {
        var state = await client.StateAsync("radiology");

        Assert.Equal(stepName, state.RequiredStep?.Name);

        return state.RequiredStep?.InformationRequest ?? throw new InvalidOperationException($"{stepName} has no information request.");
    }

    private async Task<MemberMatch> FindActiveMemberAsync()
    {
        var result = await client.QueryAsync("member", "MemberQueryContextSource", "MemberSearchViewSource", query: new QueryApiBody { Page = new QueryApiPage { Size = 100 } });

        return result.Results
            .Select(x => new MemberMatch(
                x.GetProperty("memberId").GetGuid(),
                x.GetProperty("memberEnrollmentId").GetGuid(),
                x.GetProperty("memberNumber").GetString() ?? string.Empty,
                DateOnly.Parse(x.GetProperty("effectiveDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                x.GetProperty("terminationDate").ValueKind == JsonValueKind.Null
                    ? null
                    : DateOnly.Parse(x.GetProperty("terminationDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture)))
            .First(x => x.EffectiveDate <= DateOfService && x.TerminationDate is null);
    }

    private async Task<JsonElement> SearchProvidersAsync(string source, Func<JsonElement, bool> match)
    {
        var result = await client.QueryAsync("radiology", source, parameters: new { processId = ProcessId }, query: new QueryApiBody { Page = new QueryApiPage { Size = 250 } });

        return result.Results.First(match);
    }
}

public sealed record MemberMatch(Guid MemberId, Guid MemberEnrollmentId, string MemberNumber, DateOnly EffectiveDate, DateOnly? TerminationDate);
