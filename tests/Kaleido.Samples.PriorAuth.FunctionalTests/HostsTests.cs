using System.Net;
using Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;

namespace Kaleido.Samples.PriorAuth.FunctionalTests;

public sealed class HostsTests(PriorAuthHosts hosts)
{
    public static TheoryData<string> HostNames =>
        ["Router", "ReferenceData", "CodeSet", "Provider", "Member", "Intake", "EventCollector", "Configuration", "Radiology", "History"];

    [Theory]
    [MemberData(nameof(HostNames))]
    public async Task EveryHostIsHealthy(string name)
    {
        var host = (IPriorAuthHost)typeof(PriorAuthHosts).GetProperty(name)!.GetValue(hosts)!;
        using var client = PriorAuthHosts.ClientFor(host);

        var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheRouterAggregatesEveryServiceRegistry()
    {
        using var client = PriorAuthHosts.ClientFor(hosts.Router, Persona.Carol);

        var response = await client.GetAsync("/router/registry", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isPartial\":false", body, StringComparison.Ordinal);
    }
}
