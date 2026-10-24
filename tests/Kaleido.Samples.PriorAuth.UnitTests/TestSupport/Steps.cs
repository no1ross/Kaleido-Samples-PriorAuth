using Kaleido.Processor.Context;

namespace Kaleido.Samples.PriorAuth.UnitTests.TestSupport;

/// <summary>Builds the context the runtime passes to a step handler.</summary>
public static class Steps
{
    public static ProcessStepContext Context(Guid? processId = null, string stepName = "Step") =>
        new(processId ?? Guid.NewGuid(), new StepContext { StepName = stepName }, []);
}
