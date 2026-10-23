using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kaleido.Samples.PriorAuth.Auth;
using Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;

namespace Kaleido.Samples.PriorAuth.FunctionalTests;

/// <summary>The router: the dev login and the MCP front door.</summary>
public sealed class RouterTests(PriorAuthHosts hosts)
{
    [Theory]
    [InlineData("alice", new string[0])]
    [InlineData("bob", new[] { "radiology" })]
    [InlineData("carol", new[] { "admin", "radiology" })]
    public async Task ThePersonasLogInWithTheirRoles(string name, string[] roles)
    {
        using var client = PriorAuthHosts.ClientFor(hosts.Router);

        var response = await client.PostAsJsonAsync("/auth/login", new { name }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(DevTokenIssuer.TryValidate(body.GetProperty("token").GetString()!, DevTokenIssuer.DefaultKey, out var identity));
        Assert.Equal(name, identity!.Name);
        Assert.Equal(roles, identity.Roles);
        Assert.Null(identity.Actor);
    }

    [Fact]
    public async Task AnUnknownPersonaCannotLogIn()
    {
        using var client = PriorAuthHosts.ClientFor(hosts.Router);

        var response = await client.PostAsJsonAsync("/auth/login", new { name = "mallory" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TheMcpFrontDoorListsTheHistoryTool()
    {
        using var client = PriorAuthHosts.ClientFor(hosts.Router, Persona.Bob);

        var initialize = await McpAsync(client, null, new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "tests", version = "1.0" } }
        });
        var session = initialize.Headers.TryGetValues("Mcp-Session-Id", out var values) ? values.First() : null;
        Assert.True(initialize.IsSuccessStatusCode, await initialize.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        if (session is not null)
        {
            (await McpAsync(client, session, new { jsonrpc = "2.0", method = "notifications/initialized" })).Dispose();
        }

        using var tools = await McpAsync(client, session, new { jsonrpc = "2.0", id = 2, method = "tools/list" });
        var body = await tools.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.True(tools.IsSuccessStatusCode, body);
        Assert.Contains("history_prior_auth_record_list", body, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> McpAsync(HttpClient client, string? session, object message)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonContent.Create(message) };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        if (session is not null)
        {
            request.Headers.Add("Mcp-Session-Id", session);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
