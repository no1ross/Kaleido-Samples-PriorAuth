using System.Net.Http.Json;
using System.Text.Json;
using Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;

namespace Kaleido.Samples.PriorAuth.FunctionalTests;

/// <summary>The services' events reach the EventCollector, with PHI erased.</summary>
public sealed class EventTests(PriorAuthHosts hosts)
{
    [Fact]
    public async Task AWholeRequestsEventsReachTheCollectorWithoutTheMembersName()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        await flow.StartWithMemberAsync();
        (await flow.CaptureServiceAsync()).Completed.ToString();
        await flow.AnswerMriQuestionsAsync();
        await flow.CaptureRequestingProviderAsync();
        await flow.CaptureServicingProviderAsync();
        await flow.AnswerClinicalQuestionsAsync();
        await flow.ReviewAndSubmitAsync();

        // Responses keep the name: the History list shows it.
        var memberName = await MemberNameAsync(flow.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(memberName));

        var events = await EventsAsync(flow.ProcessId, services: ["intake", "radiology", "history"]);

        Assert.Contains(events, x => x.GetProperty("serviceName").GetString() == "intake");
        Assert.Contains(events, x => x.GetProperty("serviceName").GetString() == "radiology");
        Assert.Contains(events, x => x.GetProperty("serviceName").GetString() == "history");
        Assert.All(events, x => Assert.DoesNotContain(memberName, x.GetProperty("eventJson").GetString(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> MemberNameAsync(Guid processId)
    {
        var list = await hosts.As(Persona.Bob).QueryAsync("history", "PriorAuthRecordQueryContextSource", "PriorAuthRecordListViewSource",
            query: new Kaleido.Http.Queryable.QueryApiBody { Page = new Kaleido.Http.Queryable.QueryApiPage { Size = 100 } });

        return list.Results.Single(x => x.GetProperty("processId").GetGuid() == processId).GetProperty("memberDisplayName").GetString()!;
    }

    // The outbox relay delivers in the background: wait until every service has delivered some.
    private async Task<IReadOnlyList<JsonElement>> EventsAsync(Guid processId, string[] services)
    {
        using var client = PriorAuthHosts.ClientFor(hosts.EventCollector);
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (true)
        {
            var events = await client.GetFromJsonAsync<JsonElement[]>($"/events/by-process/{processId}", TestContext.Current.CancellationToken) ?? [];

            if (services.All(service => events.Any(x => x.GetProperty("serviceName").GetString() == service)) || DateTime.UtcNow > deadline)
            {
                return events;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
    }
}
