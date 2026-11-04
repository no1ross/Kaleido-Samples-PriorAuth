using Kaleido.Processor;

namespace Kaleido.Samples.PriorAuth.Intake.Process.Steps;

[ProcessStep(
    DisplayName = "Intake - Start",
    Description = "Initiates an intake session and returns the process ID for correlation across all service calls.")]
[AvailableUntil<CaptureMemberStep>]
[AvailableUntil<CaptureRequestedServiceStep>]
public sealed record StartIntakeStep : IProcessStep;
