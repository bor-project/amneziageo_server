using System.Security.Cryptography;
using System.Text;
using AmneziaGeo.Server.Api.Configs;
using AmneziaGeo.Server.Api.Services;
using AmneziaGeo.Server.Api.Subscriptions;
using AmneziaGeo.Server.Awg.Client;
using AmneziaGeo.Server.Awg.Config;
using AmneziaGeo.Server.Core.Panel;
using Microsoft.AspNetCore.Http;

namespace AmneziaGeo.Server.Tests;

public sealed class HelloConfigsTests
{
    private static readonly SubscriptionSettings Settings = SubscriptionDefaults.Settings with { IsEnabled = true, Path = "sub", Domains = [] };

    [Fact]
    public void AConfigurationIsNamedByItsNumberAndDatedByItsEndpoint()
    {
        var updated = DateTimeOffset.Parse("2026-10-30T09:00:00Z", null);
        const string Link = "the link of the file";

        var items = ConfigItems.Of(
            [new ClientSheet("Home", "awg0", "awg0", "Home.conf", "the text", Link, 12, 3, updated, true, true)],
            "https://vpn.example.net:2096/sub/abcdefgh");

        var item = Assert.Single(items);
        Assert.Equal("12", item.Id);
        Assert.Equal("Home", item.Name);
        Assert.Equal(updated, item.Updated);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Link))), item.Revision);
        Assert.True(item.Default);
        Assert.True(item.Required);
        Assert.Equal("https://vpn.example.net:2096/sub/abcdefgh/12", item.Url);
    }

    [Fact]
    public void ASheetWithoutANumberOrAFileIsLeftOut()
    {
        var items = ConfigItems.Of(
            [
                new ClientSheet("Home", "awg0", "awg0", "Home.conf", "the text", string.Empty, 12, 3, default, false, false),
                new ClientSheet("Work", "awg0", "awg0", "Work.conf", "the text", "the link"),
            ],
            "https://vpn.example.net/sub/abcdefgh");

        Assert.Empty(items);
    }

    [Fact]
    public void TheAddressOfAConfigurationCarriesNoSlashBeforeItsNumber()
    {
        var items = ConfigItems.Of(
            [new ClientSheet("Home", "awg0", "awg0", "Home.conf", "the text", "the link", 7)],
            "https://vpn.example.net/sub/abcdefgh/");

        Assert.Equal("https://vpn.example.net/sub/abcdefgh/7", Assert.Single(items).Url);
    }

    [Fact]
    public void TheMarkOfAConfigurationIsTheDigestOfTheFileItHandsOut()
    {
        Assert.Equal(ConfigItems.Mark("the link"), ConfigItems.Mark("the link"));
        Assert.NotEqual(ConfigItems.Mark("the link"), ConfigItems.Mark("another link"));
        Assert.Equal(64, ConfigItems.Mark("the link").Length);
    }

    [Theory]
    [InlineData("/sub/abcdefgh/12", "abcdefgh", "12")]
    [InlineData("/sub/abcdefgh/0", null, null)]
    [InlineData("/sub/abcdefgh/hold", null, null)]
    [InlineData("/sub/abcdefgh", null, null)]
    [InlineData("/sub/abcdefgh/12/hold", null, null)]
    [InlineData("/api/abcdefgh/12", null, null)]
    [InlineData("/sub/abcdefgh/12/345", null, null)]
    public void APathMayNameOneConfigurationOfASubscription(string path, string? subscription, string? config)
    {
        var asked = SubscriptionAnswer.AskedConfig(new PathString(path), Settings);

        Assert.Equal(subscription is null, asked is null);
        if (asked is { } one)
        {
            Assert.Equal(subscription, one.Subscription);
            Assert.Equal(config, one.Config);
        }
    }

    [Fact]
    public async Task TheConfigurationIsOfferedAsDefaultOnlyWhenThePanelMarksIt()
    {
        Assert.NotNull(await new DefaultOffer().OfferAsync(Peer(new ServerConfig { Name = "home", IsDefault = true }), default));
        Assert.Null(await new DefaultOffer().OfferAsync(Peer(new ServerConfig { Name = "home" }), default));
    }

    [Fact]
    public async Task TheConfigurationIsAskedToUpdateOnlyWhenThePanelMarksIt()
    {
        var asked = Assert.IsType<UpdateFeature>(
            await new UpdateOffer().OfferAsync(Peer(new ServerConfig { Name = "home", RequireUpdate = true }), default));
        Assert.True(asked.Required);
        Assert.Null(await new UpdateOffer().OfferAsync(Peer(new ServerConfig { Name = "home" }), default));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ThePanelKeepsBothMarksOfAnEndpoint(bool isDefault, bool requireUpdate)
    {
        var config = ConfigAnswers.Config(
            new ServerConfig { Name = "home", IsDefault = isDefault, RequireUpdate = requireUpdate },
            secrets: false);

        Assert.Equal(isDefault, config.IsDefault);
        Assert.Equal(requireUpdate, config.RequireUpdate);

        var draft = ConfigAnswers.Draft(
            new ConfigRequest(
                null,
                null,
                ConfigDefaults.ListenPort,
                null,
                null,
                null,
                ConfigDefaults.Mtu,
                ConfigDefaults.Keepalive,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                IsDefault: isDefault,
                RequireUpdate: requireUpdate),
            null);

        Assert.Equal(isDefault, draft.IsDefault);
        Assert.Equal(requireUpdate, draft.RequireUpdate);
    }

    private static HelloPeer Peer(ServerConfig endpoint) =>
        new(new TunnelClient { Id = 1, Name = "phone" }, endpoint, null, new DefaultHttpContext());
}
