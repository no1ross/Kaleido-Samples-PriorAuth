using System.Text.Json;
using Kaleido.Http.Processor;
using Kaleido.Http.Queryable;

namespace Kaleido.Samples.PriorAuth.UnitTests.TestSupport;

/// <summary>
/// Stands in for the Kaleido client factories: each query (client, source, view) answers what the
/// test registered, and each step call is recorded and answered with a canned response.
/// </summary>
public sealed class FakeKaleido : IKaleidoQueryableClientFactory, IKaleidoProcessorClientFactory
{
    private readonly Dictionary<string, Func<object?, object[]>> _queries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<object, StepExecutionResponse>> _steps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every step sent, with the client it went through.</summary>
    public List<(string Client, object Step)> SentSteps { get; } = [];

    /// <summary>Every query request sent (parameters for a view, the request for a source).</summary>
    public List<(string Client, string Source, object? Request)> SentQueries { get; } = [];

    public FakeKaleido View<TParameters, TView>(string client, string source, string view, Func<TParameters?, IEnumerable<TView>> results)
        where TParameters : class
        where TView : class
    {
        _queries[Key(client, source, view)] = request => [.. results(request as TParameters)];
        return this;
    }

    public FakeKaleido Source<TResult>(string client, string source, Func<QueryApiRequest?, IEnumerable<TResult>> results)
        where TResult : class
    {
        _queries[Key(client, source, null)] = request => [.. results(request as QueryApiRequest)];
        return this;
    }

    public FakeKaleido Source<TResult>(string client, string source, params TResult[] results)
        where TResult : class =>
        Source<TResult>(client, source, _ => results);

    public FakeKaleido Step<TStep>(string client, Func<TStep, StepExecutionResponse> response)
        where TStep : class
    {
        _steps[Key(client, typeof(TStep).Name, null)] = step => response((TStep)step);
        return this;
    }

    /// <summary>A step answer with the given outcome (completed by default).</summary>
    public static StepExecutionResponse Answer(string stepName, StepExecutionOutcome outcome = StepExecutionOutcome.Completed, string? targetProcessorName = null) =>
        new() { ProcessId = Guid.NewGuid(), StepName = stepName, Outcome = outcome, TargetProcessorName = targetProcessorName };

    IKaleidoQueryableClient IKaleidoQueryableClientFactory.GetClient(string name) => new QueryableClient(this, name);

    IKaleidoProcessorClient IKaleidoProcessorClientFactory.GetClient(string name) => new ProcessorClient(this, name);

    bool IKaleidoQueryableClientFactory.TryGetClientForService(string serviceName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IKaleidoQueryableClient? client)
    {
        client = new QueryableClient(this, serviceName);
        return true;
    }

    bool IKaleidoProcessorClientFactory.TryGetClientForService(string serviceName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out IKaleidoProcessorClient? client)
    {
        client = new ProcessorClient(this, serviceName);
        return true;
    }

    private static string Key(string client, string name, string? view) => $"{client}|{name}|{view}";

    private QueryResult<T> Query<T>(string client, string source, string? view, object? request)
        where T : class
    {
        SentQueries.Add((client, source, request));

        if (!_queries.TryGetValue(Key(client, source, view), out var results))
        {
            throw new InvalidOperationException($"No answer registered for {client}/{source}/{view}.");
        }

        var rows = results(request).Cast<T>().ToArray();
        return new QueryResult<T>(rows.Length, 0, rows.Length, rows);
    }

    private StepExecutionResponse Execute(string client, object step)
    {
        SentSteps.Add((client, step));

        return _steps.TryGetValue(Key(client, step.GetType().Name, null), out var response)
            ? response(step)
            : throw new InvalidOperationException($"No answer registered for {client}/{step.GetType().Name}.");
    }

    private sealed class QueryableClient(FakeKaleido fake, string name) : IKaleidoQueryableClient
    {
        public Task<IReadOnlyList<QueryableSourceResponse>> GetRegistryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QueryableSourceResponse>>([]);

        public Task InvalidateRegistryAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<QueryResult<TView>> QueryViewAsync<TParameters, TView>(string source, string view, QueryApiRequest<TParameters> request, CancellationToken cancellationToken)
            where TParameters : class
            where TView : class =>
            Task.FromResult(fake.Query<TView>(name, source, view, request.Parameters));

        public Task<QueryResult<TResult>> QuerySourceAsync<TResult>(string source, QueryApiRequest request, CancellationToken cancellationToken)
            where TResult : class =>
            Task.FromResult(fake.Query<TResult>(name, source, null, request));

        public Task<QueryResult<TResult>> QuerySourceAsync<TParameters, TResult>(string source, QueryApiRequest<TParameters> request, CancellationToken cancellationToken)
            where TParameters : class
            where TResult : class =>
            Task.FromResult(fake.Query<TResult>(name, source, null, request.Parameters));
    }

    private sealed class ProcessorClient(FakeKaleido fake, string name) : IKaleidoProcessorClient
    {
        public Task<IReadOnlyList<ProcessorRegistryResponse>> GetRegistryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessorRegistryResponse>>([]);

        public Task InvalidateRegistryAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ProcessStateResponse?> GetProcessStateAsync(Guid processId, CancellationToken cancellationToken) =>
            Task.FromResult<ProcessStateResponse?>(null);

        public Task<ProcessExecutionResponse> ExecuteAsync(ExecuteProcessRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StepExecutionResponse> ExecuteStepAsync<TStep>(TStep processStep, CancellationToken cancellationToken)
            where TStep : class =>
            Task.FromResult(fake.Execute(name, processStep));

        public Task<StepExecutionResponse<TResponse>> ExecuteStepAsync<TStep, TResponse>(TStep processStep, CancellationToken cancellationToken)
            where TStep : class
        {
            var answer = fake.Execute(name, processStep);
            return Task.FromResult(new StepExecutionResponse<TResponse>
            {
                ProcessId = answer.ProcessId,
                StepName = answer.StepName,
                Outcome = answer.Outcome,
                TargetProcessorName = answer.TargetProcessorName,
                BusinessMessages = answer.BusinessMessages
            });
        }

        public Task<StepExecutionResponse<JsonElement>> ExecuteStepAsync(string stepName, JsonElement processStep, Guid? processId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
