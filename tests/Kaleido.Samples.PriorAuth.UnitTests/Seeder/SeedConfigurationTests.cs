using Kaleido.Samples.PriorAuth.Seeder;
using Kaleido.Samples.PriorAuth.Seeder.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Kaleido.Samples.PriorAuth.UnitTests.Seeder;

public sealed class SeedConfigurationTests
{
    [Fact]
    public void SettingsComeFromConfigurationWithDefaults()
    {
        var configured = SeedConfiguration.ResolveSettings(Configuration(("Seed:DataRoot", "/data"), ("Seed:Domains:0", "CodeSet"), ("Seed:Domains:1", " ")));
        var defaults = SeedConfiguration.ResolveSettings(Configuration(("Seed:DataRoot", " ")));

        Assert.Equal("/data", configured.DataRoot);
        Assert.Equal(["CodeSet"], configured.Domains);
        Assert.Equal("data", defaults.DataRoot);
        Assert.Empty(defaults.Domains);
    }

    [Fact]
    public void TheConfiguredDomainsAreSeededByDefault()
    {
        var domains = SeedConfiguration.ResolveRequestedDomains([], new SeedSettings { Domains = ["CodeSet", "memberservice", "CodeSet"] });

        Assert.Equal([SupportedDomain.CodeSet, SupportedDomain.MemberService], domains);
    }

    [Fact]
    public void TheDomainsArgumentReplacesTheConfiguredOnes()
    {
        var domains = SeedConfiguration.ResolveRequestedDomains(["--other", "--DOMAINS= ReferenceData , Configuration"], new SeedSettings { Domains = ["CodeSet"] });

        Assert.Equal([SupportedDomain.ReferenceData, SupportedDomain.Configuration], domains);
    }

    [Fact]
    public void NoDomainsMeansNothingToSeed()
    {
        Assert.Empty(SeedConfiguration.ResolveRequestedDomains(["--domains="], new SeedSettings { Domains = ["CodeSet"] }));
    }

    [Fact]
    public void AnUnknownDomainFails()
    {
        Assert.Throws<ArgumentException>(() => SeedConfiguration.ResolveRequestedDomains(["--domains=Billing"], new SeedSettings()));
    }

    [Fact]
    public void ARelativeDatabaseGoesUnderTheDataRootAndItsFolderIsCreated()
    {
        var root = Path.Combine(Path.GetTempPath(), "priorauth-seed-tests", Guid.NewGuid().ToString("N"));

        try
        {
            var resolved = new SqliteConnectionStringBuilder(new ServiceProjectContextFactory(root).ResolveConnectionString("Data Source=sub/member.db"));

            Assert.Equal(Path.GetFullPath(Path.Combine(root, "sub", "member.db")), resolved.DataSource);
            Assert.True(Directory.Exists(Path.Combine(root, "sub")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnAbsoluteDatabaseIsKept()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "member.db");

        var resolved = new SqliteConnectionStringBuilder(new ServiceProjectContextFactory("unused").ResolveConnectionString($"Data Source={absolute}"));

        Assert.Equal(absolute, resolved.DataSource);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))).Build();
}
