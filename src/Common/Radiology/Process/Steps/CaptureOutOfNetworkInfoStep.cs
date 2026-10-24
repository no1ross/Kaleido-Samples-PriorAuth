namespace Kaleido.Samples.PriorAuth.Radiology.Process.Steps;

/// <summary>
/// Why an out-of-network servicing facility is requested. Required after Capture Servicing Provider
/// when the chosen facility is out of network; the questions, their wording and options come from
/// the Configuration service, so this is an information step: its payload is the answers to the
/// request presented with it.
/// </summary>
[ProcessStep(
    DisplayName = "Capture Out-of-Network Information",
    Description = "Answers the questions configured for an out-of-network servicing facility.",
    Version = "1.0.0")]
[AvailableAfter<CaptureServicingProviderStep>]
[AvailableUntil<SubmitPriorAuthorizationStep>]
[Repeatable]
public sealed record CaptureOutOfNetworkInfoStep : IInformationStep
{
    public required string InformationRequestId { get; init; }

    public IReadOnlyList<InformationResponseItem> Items { get; init; } = [];
}
