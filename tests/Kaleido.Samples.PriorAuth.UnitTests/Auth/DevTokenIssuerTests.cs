using Kaleido.Samples.PriorAuth.Auth;

namespace Kaleido.Samples.PriorAuth.UnitTests.Auth;

public sealed class DevTokenIssuerTests
{
    private const string Key = "test-key";

    [Fact]
    public void AUserTokenValidatesWithItsNameRolesAndNoActor()
    {
        var token = DevTokenIssuer.Issue("bob", ["radiology"], Key);

        Assert.True(DevTokenIssuer.TryValidate(token, Key, out var identity));
        Assert.Equal("bob", identity!.Name);
        Assert.Equal(["radiology"], identity.Roles);
        Assert.Null(identity.Actor);
        Assert.Equal(DevPrincipalTypes.User, identity.PrincipalType);
    }

    [Fact]
    public void AnOnBehalfOfTokenKeepsTheUserAndNamesTheActor()
    {
        var token = DevTokenIssuer.Issue("bob", ["radiology"], Key, actor: "intake");

        Assert.True(DevTokenIssuer.TryValidate(token, Key, out var identity));
        Assert.Equal("bob", identity!.Name);
        Assert.Equal("intake", identity.Actor);
        Assert.Equal(DevPrincipalTypes.User, identity.PrincipalType);
    }

    [Fact]
    public void AServiceTokenIsTheServiceActingOnItsOwn()
    {
        var token = DevTokenIssuer.IssueServiceToken("router", Key);

        Assert.True(DevTokenIssuer.TryValidate(token, Key, out var identity));
        Assert.Equal("svc-router", identity!.Name);
        Assert.Equal("router", identity.Actor);
        Assert.Equal(DevPrincipalTypes.Service, identity.PrincipalType);
        Assert.Contains("radiology", identity.Roles);
    }

    [Fact]
    public void AUserWithoutRolesHasNone()
    {
        Assert.True(DevTokenIssuer.TryValidate(DevTokenIssuer.Issue("alice", [], Key), Key, out var identity));
        Assert.Empty(identity!.Roles);
    }

    [Fact]
    public void ATokenSignedWithAnotherKeyIsRefused()
    {
        var token = DevTokenIssuer.Issue("bob", ["radiology"], "other-key");

        Assert.False(DevTokenIssuer.TryValidate(token, Key, out var identity));
        Assert.Null(identity);
    }

    [Fact]
    public void AChangedPayloadIsRefused()
    {
        var parts = DevTokenIssuer.Issue("alice", [], Key).Split('.');
        var forged = DevTokenIssuer.Issue("alice", ["radiology"], "other-key").Split('.')[1];

        Assert.False(DevTokenIssuer.TryValidate($"dev.{forged}.{parts[2]}", Key, out _));
    }

    [Fact]
    public void AnExpiredTokenIsRefused()
    {
        var token = DevTokenIssuer.Issue("bob", ["radiology"], Key, lifetime: TimeSpan.FromSeconds(-5));

        Assert.False(DevTokenIssuer.TryValidate(token, Key, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("jwt.abc.def")]
    [InlineData("dev.abc")]
    public void AMalformedTokenIsRefused(string token)
    {
        Assert.False(DevTokenIssuer.TryValidate(token, Key, out _));
    }
}
