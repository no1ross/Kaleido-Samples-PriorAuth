using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kaleido.Http;
using Kaleido.Http.Processor;
using Kaleido.Http.Queryable;

namespace Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;

/// <summary>
/// Calls the sample's services as a UI in direct mode does: as one persona, each service on its
/// own host, with the request's process id on every call once a process exists.
/// </summary>
public sealed class SampleClient(PriorAuthHosts hosts, Persona? persona)
{
    private static readonly JsonSerializerOptions Json = KaleidoJsonOptions.Options;

    /// <summary>The process the calls belong to; set by the first step that answers with one.</summary>
    public Guid? ProcessId { get; set; }

    public async Task<StepCall> ExecuteAsync(string service, IProcessStep step)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/{service}/processes/steps/{step.GetType().Name.ToLowerInvariant()}")
        {
            Content = JsonContent.Create(new { processStep = (object)step }, options: Json)
        };

        var call = await SendAsync<StepExecutionResponse<JsonElement>>(service, request);

        if (call.Body is { } response)
        {
            ProcessId ??= response.ProcessId;
        }

        return new StepCall(call.Status, call.Body, call.Raw);
    }

    public async Task<ProcessStateResponse> StateAsync(string service)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/{service}/processes/{ProcessId}");
        var call = await SendAsync<ProcessStateResponse>(service, request);

        return call.Body ?? throw new InvalidOperationException($"No state for {ProcessId} at {service}: {call.Status} {call.Raw}");
    }

    public async Task<QueryResults> QueryAsync(string service, string source, string? view = null, object? parameters = null, QueryApiBody? query = null)
    {
        var call = await TryQueryAsync(service, source, view, parameters, query);

        return call.Results ?? throw new InvalidOperationException($"Query {service}/{source}/{view} answered {call.Status}: {call.Raw}");
    }

    /// <summary>A query that may be refused: its status, and its results when it succeeded.</summary>
    public async Task<(HttpStatusCode Status, QueryResults? Results, string Raw)> TryQueryAsync(string service, string source, string? view = null, object? parameters = null, QueryApiBody? query = null)
    {
        var path = view is null
            ? $"/{service}/queryable/{source.ToLowerInvariant()}/query"
            : $"/{service}/queryable/{source.ToLowerInvariant()}/{view.ToLowerInvariant()}/query";

        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { parameters = parameters ?? new { }, query = query ?? new QueryApiBody() }, options: Json)
        };

        return await SendAsync<QueryResults>(service, request);
    }

    private async Task<(HttpStatusCode Status, T? Body, string Raw)> SendAsync<T>(string service, HttpRequestMessage request)
        where T : class
    {
        if (ProcessId is { } processId)
        {
            request.Headers.Add(KaleidoCorrelationHeaders.ProcessId, processId.ToString());
        }

        using var client = PriorAuthHosts.ClientFor(hosts.For(service), persona);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        T? body = null;

        if (response.IsSuccessStatusCode && raw.Length > 0)
        {
            body = JsonSerializer.Deserialize<T>(raw, Json);
        }

        return (response.StatusCode, body, raw);
    }
}

/// <summary>A query's answer, its results read as JSON.</summary>
public sealed record QueryResults(IReadOnlyList<JsonElement> Results, int TotalCount);

/// <summary>A step call's HTTP status, its answer (on success), and the raw body (for assertions on errors).</summary>
public sealed record StepCall(HttpStatusCode Status, StepExecutionResponse<JsonElement>? Response, string Raw)
{
    public StepExecutionResponse<JsonElement> Completed =>
        Response is { Outcome: StepExecutionOutcome.Completed } response
            ? response
            : throw new Xunit.Sdk.XunitException($"The step did not complete: {Status} {Raw}");
}
