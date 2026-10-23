using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Kaleido.Samples.PriorAuth.FunctionalTests.Hosting;

/// <summary>
/// One sample host run in-process with its Development settings (the localhost ports the hosts
/// call each other on), its databases in the test run's data folder, and every outgoing HTTP call
/// routed to the host on that port (<see cref="HostRouter"/>).
/// </summary>
public interface IPriorAuthHost : IDisposable
{
    int Port { get; }

    TestServer Server { get; }

    IServiceProvider Services { get; }

    HttpClient CreateClient();
}

public sealed class PriorAuthHost<TEntryPoint>(
    int port,
    IReadOnlyDictionary<string, string> settings,
    PriorAuthHosts hosts)
    : WebApplicationFactory<TEntryPoint>, IPriorAuthHost
    where TEntryPoint : class
{
    public int Port => port;

    IServiceProvider IPriorAuthHost.Services => Services;

    HttpClient IPriorAuthHost.CreateClient() => CreateClient();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting, not ConfigureAppConfiguration: the hosts read their connection strings before Build.
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
            services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new RouteToHosts(hosts)));
    }

    private sealed class RouteToHosts(PriorAuthHosts hosts) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) =>
            builder =>
            {
                next(builder);
                builder.PrimaryHandler = new HostRouter(hosts);
            };
    }
}

/// <summary>Sends a request to the in-process host listening on the request's port.</summary>
public sealed class HostRouter(PriorAuthHosts hosts) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        hosts.InvokerFor(request.RequestUri?.Port ?? 0).SendAsync(request, cancellationToken);
}
