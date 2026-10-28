using System.Net;
using AmneziaGeo.Server.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public sealed class UpdateWindowTests : IDisposable
{
    private const string Page = "<!doctype html><html><head><base href=\"/\" /><title>x</title></head><body></body></html>";

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("amneziageo-window-");

    private readonly DirectoryInfo _releases = Directory.CreateTempSubdirectory("amneziageo-releases-");

    public void Dispose()
    {
        _root.Delete(recursive: true);
        _releases.Delete(recursive: true);
    }

    [Fact]
    public async Task TheWindowAnswersItsPageAndFilesUnderItsPath_AndLeavesTheRestToTheReleases()
    {
        File.WriteAllText(Path.Combine(_root.FullName, UpdateWindow.PageName), Page);
        Directory.CreateDirectory(Path.Combine(_root.FullName, "assets"));
        File.WriteAllText(Path.Combine(_root.FullName, "assets", "app-1.js"), "run()");
        File.WriteAllText(Path.Combine(_root.FullName, "icons.svg"), "<svg/>");
        var token = UpdateToken.Make();
        var store = new ReleaseStore(_releases.FullName);
        using var window = new UpdateWindow(_root.FullName, "w4", "/", "1.2.3.4");
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.Run(async context =>
        {
            if (await window.TryAnswerAsync(context))
            {
                return;
            }

            if (!await UpdateApi.TryAnswerAsync(context, "/", store, asking => UpdateToken.Holds(asking, token)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(address) };

        using var bare = await http.GetAsync("/w4?from=x");
        using var home = await http.GetAsync("/w4/");
        using var route = await http.GetAsync("/w4/v1.10.9.0/edit");
        using var script = await http.GetAsync("/w4/assets/app-1.js");
        using var gone = await http.GetAsync("/w4/assets/gone.js");
        using var icons = await http.GetAsync("/w4/icons.svg");
        using var posted = await http.PostAsync("/w4/", new StringContent("x"));
        using var manifest = await http.GetAsync("/stable/update.json");
        using var status = await http.GetAsync("/status");
        await app.StopAsync();

        Assert.Equal(HttpStatusCode.Redirect, bare.StatusCode);
        Assert.Equal("/w4/?from=x", bare.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Equal("text/html", home.Content.Headers.ContentType?.MediaType);
        var text = await home.Content.ReadAsStringAsync();
        Assert.Contains("<base href=\"/w4/\" />", text, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"amneziageo-version\" content=\"1.2.3.4\" />", text, StringComparison.Ordinal);
        Assert.Equal("DENY", home.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", home.Headers.GetValues("Referrer-Policy").Single());
        Assert.True(home.Headers.CacheControl?.NoCache);
        Assert.Equal(text, await route.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
        Assert.Equal("run()", await script.Content.ReadAsStringAsync());
        Assert.True(script.Headers.CacheControl?.Public);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal("image/svg+xml", icons.Content.Headers.ContentType?.MediaType);
        Assert.True(icons.Headers.CacheControl?.NoCache);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, posted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, manifest.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, status.StatusCode);
    }

    [Fact]
    public async Task WithoutItsPage_TheWindowAnswersNothingUnderItsPath()
    {
        using var window = new UpdateWindow(Path.Combine(_root.FullName, "none"), "w4", "/", "1.2.3.4");
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/w4/";

        var answered = await window.TryAnswerAsync(context);

        Assert.True(answered);
        Assert.False(window.Present);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("w4", "/", true)]
    [InlineData("/a/b.c-d_e/", "", true)]
    [InlineData("", "/", false)]
    [InlineData("/", "/", false)]
    [InlineData("stable", "/", false)]
    [InlineData("releases/x", "/", false)]
    [InlineData("health", "/", false)]
    [InlineData("a/../b", "/", false)]
    [InlineData("a b", "/", false)]
    [InlineData("u", "u", false)]
    [InlineData("u/status", "/u/", false)]
    [InlineData("u/web", "u", true)]
    [InlineData("stable", "u", true)]
    public void APath_HoldsTheWindowUnlessItMeetsARouteOfTheReleases(string path, string releases, bool fits)
    {
        Assert.Equal(fits, UpdateWindow.Fits(path, releases));
    }

    [Fact]
    public void ThePage_CarriesThePathOfTheWindowThePathOfTheReleasesAndTheVersion()
    {
        var page = UpdateWindow.Fill(Page, "/w4/", "u", "1.2.3.4");
        var bare = UpdateWindow.Fill("<html><head><title>x</title></head></html>", "/w4/", "", "1.2.3.4");

        Assert.Contains("<base href=\"/w4/\" />", page, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"amneziageo-releases\" content=\"/u/\" />", page, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"amneziageo-version\" content=\"1.2.3.4\" />", page, StringComparison.Ordinal);
        Assert.Contains("<base href=\"/w4/\" />", bare, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"amneziageo-releases\" content=\"/\" />", bare, StringComparison.Ordinal);
    }

    [Fact]
    public void AMadePath_IsSixteenLettersAndDigitsThatHoldTheWindow()
    {
        var path = UpdateWindow.Make();

        Assert.Matches("^[a-z0-9]{16}$", path);
        Assert.True(UpdateWindow.Fits(path, "/"));
    }
}
