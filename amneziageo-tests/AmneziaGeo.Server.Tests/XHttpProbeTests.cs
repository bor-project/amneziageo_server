using AmneziaGeo.Server.Api.Services;
using Microsoft.AspNetCore.Http;

#pragma warning disable xUnit1030

namespace AmneziaGeo.Server.Tests;

public sealed class XHttpProbeTests
{
    [Theory]
    [InlineData("GET", 0, false, true, true, 200)]
    [InlineData("POST", 0, false, true, true, 405)]
    [InlineData("GET", 1, false, true, true, 400)]
    [InlineData("GET", 0, true, true, true, 400)]
    [InlineData("GET", 0, false, false, true, 401)]
    [InlineData("GET", 0, false, true, false, 503)]
    public async Task ACheckRequiresGetWithoutABodyCurrentAuthorizationAndARunningListener(
        string method, long length, bool upgrade, bool authorized, bool running, int status)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.ContentLength = length;
        if (upgrade)
        {
            context.Request.Headers.Upgrade = "websocket";
        }

        using var body = new MemoryStream();
        context.Response.Body = body;
        var nonce = Guid.NewGuid().ToString("N");
        var checkedAuth = false;
        await XHttpProbe.AnswerAsync(context, nonce, () =>
        {
            checkedAuth = true;
            return Task.FromResult(authorized);
        }, running).ConfigureAwait(false);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(status is 200 or 401 or 503, checkedAuth);
        Assert.Equal(status == 200, body.Length > 0);
        if (status == 200)
        {
            Assert.Contains(nonce, System.Text.Encoding.UTF8.GetString(body.ToArray()));
        }
    }

    [Fact]
    public void OnlyTheReservedProbePathExtractsANonce()
    {
        var nonce = Guid.NewGuid().ToString("N");
        Assert.True(XHttpProbe.Nonce("/relay/_amneziageo/probe/" + nonce, "relay", out var echoed));
        Assert.Equal(nonce, echoed);
        Assert.False(XHttpProbe.Nonce("/other/_amneziageo/probe/" + nonce, "relay", out _));
        Assert.False(XHttpProbe.Nonce("/relay/_amneziageo/probe/short", "relay", out _));
        Assert.False(XHttpProbe.Nonce("/relay/" + nonce + "/0", "relay", out _));
    }
}
