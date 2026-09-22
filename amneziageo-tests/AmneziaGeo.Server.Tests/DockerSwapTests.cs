using System.Text;
using System.Text.Json.Nodes;
using AmneziaGeo.Server.Api.Updates;

namespace AmneziaGeo.Server.Tests;

public sealed class DockerSwapTests
{
    private const string Data = "/var/lib/amneziageo-server";

    private const string Picture = "amneziageo-server:0.0.4.0";

    [Fact]
    public void TheNewContainerKeepsWhatComposeGaveTheOldOne()
    {
        var spec = DockerSwap.Spec(Container(), Image(), Picture);

        Assert.Equal(Picture, spec["Image"]!.GetValue<string>());
        Assert.Equal("host", spec["HostConfig"]!["NetworkMode"]!.GetValue<string>());
        Assert.Equal(new[] { "NET_ADMIN" }, Words(spec["HostConfig"]!["CapAdd"]));
        Assert.Equal("unless-stopped", spec["HostConfig"]!["RestartPolicy"]!["Name"]!.GetValue<string>());
        Assert.Contains(Data + ":" + Data, Words(spec["HostConfig"]!["Binds"]));
        Assert.Equal("panel", spec["Labels"]!["com.docker.compose.service"]!.GetValue<string>());
        Assert.Contains("AMNEZIAGEO_HEALTH=https://127.0.0.1:8443/api/health", Words(spec["Env"]));
    }

    [Fact]
    public void WhatTheImageItselfSetsIsLeftToTheNewImage()
    {
        var spec = DockerSwap.Spec(Container(), Image(), Picture);

        Assert.DoesNotContain("PATH=/usr/bin", Words(spec["Env"]));
        Assert.DoesNotContain("AMNEZIAGEO_DB=" + Data + "/server.db", Words(spec["Env"]));
        Assert.Null(spec["Entrypoint"]);
        Assert.Null(spec["WorkingDir"]);
        Assert.Null(spec["Healthcheck"]);
        Assert.Null(spec["Labels"]!["org.opencontainers.image.version"]);
    }

    [Fact]
    public void WhatComposeSetItselfComesAlong()
    {
        var container = Container();
        container["Config"]!["Healthcheck"] = new JsonObject { ["Test"] = new JsonArray("CMD", "true") };
        container["Config"]!["User"] = "panel";

        var spec = DockerSwap.Spec(container, Image(), Picture);

        Assert.Equal(new[] { "CMD", "true" }, Words(spec["Healthcheck"]!["Test"]));
        Assert.Equal("panel", spec["User"]!.GetValue<string>());
    }

    [Fact]
    public void AVolumeOfTheContainerComesAlong()
    {
        var spec = DockerSwap.Spec(Container(), Image(), Picture);

        var mount = spec["HostConfig"]!["Mounts"]!.AsArray().Single();
        Assert.Equal("volume", mount!["Type"]!.GetValue<string>());
        Assert.Equal("amneziageo-server_settings", mount["Source"]!.GetValue<string>());
        Assert.Equal("/etc/amneziageo-server", mount["Target"]!.GetValue<string>());
    }

    [Fact]
    public void AContainerOnTheNetworkOfTheHostTakesNeitherHostnameNorNetwork()
    {
        var spec = DockerSwap.Spec(Container(), Image(), Picture);

        Assert.Null(spec["Hostname"]);
        Assert.Null(spec["NetworkingConfig"]);
    }

    [Fact]
    public void AContainerOnANetworkKeepsItsNamesThere()
    {
        var container = Container();
        container["HostConfig"]!["NetworkMode"] = "amneziageo-server_default";
        container["NetworkSettings"] = new JsonObject
        {
            ["Networks"] = new JsonObject
            {
                ["amneziageo-server_default"] = new JsonObject
                {
                    ["Aliases"] = new JsonArray("panel", "bbbbbbbbbbbb"),
                    ["IPAMConfig"] = new JsonObject { ["IPv4Address"] = "172.20.0.5" },
                },
            },
        };

        var spec = DockerSwap.Spec(container, Image(), Picture);

        var end = spec["NetworkingConfig"]!["EndpointsConfig"]!["amneziageo-server_default"];
        Assert.Equal("panel", spec["Hostname"]!.GetValue<string>());
        Assert.Equal(new[] { "panel" }, Words(end!["Aliases"]));
        Assert.Equal("172.20.0.5", end["IPAMConfig"]!["IPv4Address"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("host", true)]
    [InlineData("none", true)]
    [InlineData("container:abc", true)]
    [InlineData("bridge", false)]
    public void ANetworkOfItsOwnIsToldFromASharedOne(string mode, bool shared)
    {
        Assert.Equal(shared, DockerSwap.Shared(mode));
    }

    [Fact]
    public void TheLinesOfALogComeOutOfTheirFrames()
    {
        var one = Encoding.UTF8.GetBytes("the panel answers\n");
        var two = Encoding.UTF8.GetBytes("it does not\n");
        var stream = new List<byte>();
        stream.AddRange([1, 0, 0, 0, 0, 0, 0, (byte)one.Length]);
        stream.AddRange(one);
        stream.AddRange([2, 0, 0, 0, 0, 0, 0, (byte)two.Length]);
        stream.AddRange(two);

        Assert.Equal("the panel answers\nit does not\n", DockerEngine.Plain([.. stream]));
        Assert.Equal("plain text", DockerEngine.Plain(Encoding.UTF8.GetBytes("plain text")));
    }

    [Fact]
    public void TheEnvFileOfTheProjectTakesTheTag()
    {
        Assert.Equal("AMNEZIAGEO_TAG=0.0.4.0\n", HandoverCommand.Tagged(null, "AMNEZIAGEO_TAG=0.0.4.0"));
        Assert.Equal(
            "AMNEZIAGEO_VERSION=1.0.0\nAMNEZIAGEO_TAG=0.0.4.0\n",
            HandoverCommand.Tagged("AMNEZIAGEO_VERSION=1.0.0\n", "AMNEZIAGEO_TAG=0.0.4.0"));
        Assert.Equal(
            "AMNEZIAGEO_TAG=0.0.4.0\nAMNEZIAGEO_VERSION=1.0.0\n",
            HandoverCommand.Tagged("AMNEZIAGEO_TAG=0.0.3.0\nAMNEZIAGEO_VERSION=1.0.0\n", "AMNEZIAGEO_TAG=0.0.4.0"));
    }

    private static JsonNode Container() => new JsonObject
    {
        ["Id"] = new string('b', 64),
        ["Name"] = "/amneziageo-server-panel-1",
        ["Config"] = new JsonObject
        {
            ["Hostname"] = "panel",
            ["Image"] = "amneziageo-server:0.0.3.0",
            ["Env"] = new JsonArray(
                "PATH=/usr/bin",
                "AMNEZIAGEO_DB=" + Data + "/server.db",
                "AMNEZIAGEO_HEALTH=https://127.0.0.1:8443/api/health"),
            ["Entrypoint"] = new JsonArray("tini", "--", "amneziageo-server-entrypoint"),
            ["WorkingDir"] = "/opt/amneziageo-server",
            ["Healthcheck"] = new JsonObject { ["Test"] = new JsonArray("CMD-SHELL", "curl -fsSk $AMNEZIAGEO_HEALTH") },
            ["Labels"] = new JsonObject
            {
                ["com.docker.compose.project"] = "amneziageo-server",
                ["com.docker.compose.service"] = "panel",
                ["org.opencontainers.image.version"] = "0.0.3.0",
            },
        },
        ["HostConfig"] = new JsonObject
        {
            ["NetworkMode"] = "host",
            ["Binds"] = new JsonArray(Data + ":" + Data, "/var/run/docker.sock:/var/run/docker.sock"),
            ["CapAdd"] = new JsonArray("NET_ADMIN"),
            ["RestartPolicy"] = new JsonObject { ["Name"] = "unless-stopped" },
        },
        ["Mounts"] = new JsonArray(
            new JsonObject
            {
                ["Type"] = "bind",
                ["Source"] = Data,
                ["Destination"] = Data,
                ["RW"] = true,
            },
            new JsonObject
            {
                ["Type"] = "volume",
                ["Name"] = "amneziageo-server_settings",
                ["Destination"] = "/etc/amneziageo-server",
                ["RW"] = true,
            }),
    };

    private static JsonNode Image() => new JsonObject
    {
        ["Config"] = new JsonObject
        {
            ["Env"] = new JsonArray("PATH=/usr/bin", "AMNEZIAGEO_DB=" + Data + "/server.db"),
            ["Entrypoint"] = new JsonArray("tini", "--", "amneziageo-server-entrypoint"),
            ["WorkingDir"] = "/opt/amneziageo-server",
            ["Healthcheck"] = new JsonObject { ["Test"] = new JsonArray("CMD-SHELL", "curl -fsSk $AMNEZIAGEO_HEALTH") },
            ["Labels"] = new JsonObject { ["org.opencontainers.image.version"] = "0.0.3.0" },
        },
    };

    private static string[] Words(JsonNode? list) =>
        [.. list!.AsArray().Select(one => one!.GetValue<string>())];
}
