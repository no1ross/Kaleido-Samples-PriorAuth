using System.Net;
using System.Text.Json;
using Kaleido.Http.Queryable;
using Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;

namespace Kaleido.Samples.PriorAuth.FunctionalTests;

/// <summary>Each service's queries against the seeded data, as a signed-in user calls them.</summary>
public sealed class QueryTests(PriorAuthHosts hosts)
{
    private static readonly QueryApiBody FirstPage = new() { Page = new QueryApiPage { Size = 10 } };

    private SampleClient Bob => hosts.As(Persona.Bob);

    [Fact]
    public async Task ProcedureCodesAreFoundByText()
    {
        var result = await Bob.QueryAsync("codeset", "ProcedureCodeQueryContextSource", query: new QueryApiBody { SearchText = "73721", Page = new QueryApiPage { Size = 10 } });

        Assert.Contains(result.Results, x => x.GetProperty("codeValue").GetString() == "73721");
    }

    [Fact]
    public async Task DiagnosisCodesAreListed()
    {
        var result = await Bob.QueryAsync("codeset", "DiagnosisCodeQueryContextSource", query: FirstPage);

        Assert.NotEmpty(result.Results);
    }

    [Fact]
    public async Task StatesAndZipCodesAreListed()
    {
        Assert.NotEmpty((await Bob.QueryAsync("referencedata", "StateQueryContextSource", query: FirstPage)).Results);
        Assert.NotEmpty((await Bob.QueryAsync("referencedata", "ZipCodeQueryContextSource", query: FirstPage)).Results);
    }

    [Theory]
    [InlineData("referencedata", "PlanQueryContextSource")]
    [InlineData("provider", "RequestingProviderQueryContextSource")]
    [InlineData("configuration", "ProductCodeMappingQueryContextSource")]
    public async Task ServiceOnlySourcesRefuseAUser(string service, string source)
    {
        var call = await Bob.TryQueryAsync(service, source, query: FirstPage);

        Assert.False(call.Status.IsSuccess(), $"{service}/{source} answered {call.Status}.");
    }

    [Fact]
    public async Task AMemberIsFoundByMemberNumberAndHasDetails()
    {
        var page = await Bob.QueryAsync("member", "MemberQueryContextSource", "MemberSearchViewSource", query: FirstPage);
        var first = page.Results[0];
        var memberNumber = first.GetProperty("memberNumber").GetString()!;

        var found = await Bob.QueryAsync("member", "MemberQueryContextSource", "MemberSearchViewSource", query: new QueryApiBody { SearchText = memberNumber, Page = new QueryApiPage { Size = 10 } });
        Assert.All(found.Results, x => Assert.Equal(memberNumber, x.GetProperty("memberNumber").GetString()));

        var details = await Bob.QueryAsync("member", "MemberQueryContextSource", "MemberDetailsViewSource",
            parameters: new { memberId = first.GetProperty("memberId").GetGuid(), memberEnrollmentId = first.GetProperty("memberEnrollmentId").GetGuid() });
        Assert.Equal(memberNumber, Assert.Single(details.Results).GetProperty("memberNumber").GetString());
    }

    [Fact]
    public async Task TheHistoryListShowsARequestFromItsStart()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        var member = await flow.StartWithMemberAsync();

        var list = await flow.Client.QueryAsync("history", "PriorAuthRecordQueryContextSource", "PriorAuthRecordListViewSource", query: new QueryApiBody { Page = new QueryApiPage { Size = 100 } });

        var record = Assert.Single(list.Results, x => x.GetProperty("processId").GetGuid() == flow.ProcessId);
        Assert.Equal(member.MemberNumber, record.GetProperty("memberNumber").GetString());
    }

    [Fact]
    public async Task RadiologyListsTheRequestedServicesOfItsRequest()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        await flow.StartWithMemberAsync();
        (await flow.CaptureServiceAsync()).Completed.ToString();

        var services = await flow.Client.QueryAsync("radiology", "RequestedServiceQueryContextSource");

        var service = Assert.Single(services.Results, x => x.GetProperty("processId").GetGuid() == flow.ProcessId);
        Assert.Equal("73721", service.GetProperty("resolvedCodeValue").GetString());
    }

    [Fact]
    public async Task AProviderSearchBeforeTheMemberIsCapturedIsACallersMistake()
    {
        var call = await Bob.TryQueryAsync("radiology", "ServicingProviderSearchSource", parameters: new { processId = Guid.NewGuid() }, query: FirstPage);

        Assert.Equal(HttpStatusCode.BadRequest, call.Status);
    }

    [Fact]
    public async Task TheServicingSearchShowsNetworkStatus()
    {
        var flow = new PriorAuthFlow(hosts.As(Persona.Bob));
        await flow.StartWithMemberAsync();
        (await flow.CaptureServiceAsync()).Completed.ToString();

        var result = await flow.Client.QueryAsync("radiology", "ServicingProviderSearchSource", parameters: new { processId = flow.ProcessId }, query: new QueryApiBody { Page = new QueryApiPage { Size = 250 } });

        Assert.Contains(result.Results, x => x.GetProperty("isInNetwork").ValueKind is JsonValueKind.True or JsonValueKind.False);
    }
}

internal static class HttpStatusCodeExtensions
{
    public static bool IsSuccess(this HttpStatusCode status) => (int)status is >= 200 and < 300;
}
