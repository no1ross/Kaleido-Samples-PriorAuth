using Kaleido.Http.Client;
using Kaleido.Samples.PriorAuth.Radiology.Process.Messages;
using Kaleido.Samples.PriorAuth.Radiology.Process.Services;
using Kaleido.Samples.PriorAuth.Radiology.Process.Steps;

namespace Kaleido.Samples.PriorAuth.Radiology.Process.Handlers;

/// <summary>
/// Keeps the answers about the out-of-network facility (shown in review), then starts the clinical
/// review: its first round, or review when no clinical review applies. Whatever the answers, the
/// request goes on; the reviewer decides.
/// </summary>
public sealed class CaptureOutOfNetworkInfoHandler(
    ConfiguredInformationRequests informationRequests,
    RadiologyClinicalReview clinicalReview)
    : IProcessStepHandler<CaptureOutOfNetworkInfoStep>
{
    public async Task<ProcessStepHandlerResult> ExecuteAsync(
        CaptureOutOfNetworkInfoStep processStep,
        ProcessStepContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await informationRequests.RecordAnswersAsync(context.ProcessId, nameof(CaptureOutOfNetworkInfoStep), processStep, cancellationToken);

            return await clinicalReview.StartAsync(context.ProcessId, cancellationToken) is { } clinical
                ? ProcessStepHandlerResult.RequireInformation<CaptureClinicalInfoStep>(clinical)
                : ProcessStepHandlerResult.Success<ReviewPriorAuthorizationStep>();
        }
        catch (KaleidoHttpClientException ex)
        {
            return ProcessStepHandlerResult.Reject(
                RadiologyProcessMessages.QueryableRequestFailed(ex.Errors.FirstOrDefault()?.Code ?? "QUERYABLE_REQUEST_FAILED", ex.Message));
        }
    }
}
