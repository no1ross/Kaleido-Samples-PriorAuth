extern alias codeset;
extern alias configuration;
extern alias eventcollector;
extern alias history;
extern alias intake;
extern alias member;
extern alias provider;
extern alias radiology;
extern alias referencedata;
extern alias router;
extern alias seeder;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Kaleido.Samples.PriorAuth.Auth;
using seeder::Kaleido.Samples.PriorAuth.Seeder;
using seeder::Kaleido.Samples.PriorAuth.Seeder.CodeSet;
using seeder::Kaleido.Samples.PriorAuth.Seeder.Configuration;
using seeder::Kaleido.Samples.PriorAuth.Seeder.Infrastructure;
using seeder::Kaleido.Samples.PriorAuth.Seeder.MemberService;
using seeder::Kaleido.Samples.PriorAuth.Seeder.ProviderSearch;
using seeder::Kaleido.Samples.PriorAuth.Seeder.ReferenceData;

[assembly: AssemblyFixture(typeof(Kaleido.Samples.PriorAuth.FunctionalTests.Hosting.PriorAuthHosts))]

namespace Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;

/// <summary>
/// The whole sample in-process, once per test run: the reference databases seeded into a fresh
/// data folder, then every host (the router, the services, the EventCollector) on its Development
/// port. Hosts call each other through <see cref="HostRouter"/>, never the network.
/// </summary>
public sealed class PriorAuthHosts : IAsyncLifetime
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "priorauth-tests", Guid.NewGuid().ToString("N"));
    private readonly Dictionary<int, IPriorAuthHost> _hosts = [];
    private readonly ConcurrentDictionary<int, HttpMessageInvoker> _invokers = new();

    public IPriorAuthHost Router => _hosts[8080];

    public IPriorAuthHost ReferenceData => _hosts[8081];

    public IPriorAuthHost CodeSet => _hosts[8082];

    public IPriorAuthHost Provider => _hosts[8083];

    public IPriorAuthHost Member => _hosts[8084];

    public IPriorAuthHost Intake => _hosts[8085];

    public IPriorAuthHost EventCollector => _hosts[8086];

    public IPriorAuthHost Configuration => _hosts[8087];

    public IPriorAuthHost Radiology => _hosts[8088];

    public IPriorAuthHost History => _hosts[8089];

    public string DataRoot => _dataRoot;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_dataRoot);

        var contexts = new ServiceProjectContextFactory(_dataRoot);
        await new SeedRunner(
                [
                    new ReferenceDataSeeder(contexts),
                    new CodeSetSeeder(contexts),
                    new ConfigurationSeeder(contexts),
                    new ProviderSearchSeeder(contexts),
                    new MemberServiceSeeder(contexts)
                ])
            .RunAsync(Enum.GetValues<SupportedDomain>(), CancellationToken.None);

        Add(new PriorAuthHost<router::Program>(8080, Settings(), this));
        Add(new PriorAuthHost<referencedata::Program>(8081, Settings(("ReferenceData", "referencedata.db")), this));
        Add(new PriorAuthHost<codeset::Program>(8082, Settings(("CodeSet", "codeset.db")), this));
        Add(new PriorAuthHost<provider::Program>(8083, Settings(("Provider", "provider.db")), this));
        Add(new PriorAuthHost<member::Program>(8084, Settings(("Member", "member.db")), this));
        Add(new PriorAuthHost<intake::Program>(8085, Settings(("Intake", "intake.db"), ("IntakeProcess", "intake-process.db")), this));
        Add(new PriorAuthHost<eventcollector::Program>(8086, Settings(("EventCollector", "eventcollector.db")), this));
        Add(new PriorAuthHost<configuration::Program>(8087, Settings(("Configuration", "configuration.db")), this));
        Add(new PriorAuthHost<radiology::Program>(8088, Settings(("Radiology", "radiology.db"), ("RadiologyProcess", "radiology-process.db")), this));
        Add(new PriorAuthHost<history::Program>(8089, Settings(("History", "history.db")), this));

        // Start every host before any test calls one (a host's startup may call the others).
        foreach (var host in _hosts.Values)
        {
            _ = host.Server;
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var host in _hosts.Values)
        {
            host.Dispose();
        }

        foreach (var invoker in _invokers.Values)
        {
            invoker.Dispose();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open: the folder is under the temp directory and harmless.
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>A client for <paramref name="host"/> signed in as <paramref name="persona"/> (none: anonymous).</summary>
    public static HttpClient ClientFor(IPriorAuthHost host, Persona? persona = null)
    {
        var client = host.CreateClient();

        if (persona is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                DevTokenIssuer.Issue(persona.Name, persona.Roles, DevTokenIssuer.DefaultKey));
        }

        return client;
    }

    internal HttpMessageInvoker InvokerFor(int port) =>
        _invokers.GetOrAdd(
            port,
            p => _hosts.TryGetValue(p, out var host)
                ? new HttpMessageInvoker(host.Server.CreateHandler(), disposeHandler: true)
                : throw new InvalidOperationException($"No test host listens on port {p}."));

    private void Add(IPriorAuthHost host) => _hosts.Add(host.Port, host);

    private Dictionary<string, string> Settings(params (string Name, string File)[] connectionStrings)
    {
        var settings = new Dictionary<string, string>();

        foreach (var (name, file) in connectionStrings)
        {
            settings[$"ConnectionStrings:{name}"] = $"Data Source={Path.Combine(_dataRoot, file)}";
        }

        return settings;
    }
}

/// <summary>A dev persona (the router's login personas).</summary>
public sealed record Persona(string Name, params string[] Roles)
{
    public static readonly Persona Alice = new("alice");
    public static readonly Persona Bob = new("bob", "radiology");
    public static readonly Persona Carol = new("carol", "admin", "radiology");
}
