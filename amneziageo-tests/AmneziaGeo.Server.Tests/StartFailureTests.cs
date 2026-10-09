using AmneziaGeo.Server.Api.Web;
using Microsoft.AspNetCore.Connections;

namespace AmneziaGeo.Server.Tests;

public class StartFailureTests
{
    [Fact]
    public void AnAddressAnotherProgramHoldsIsToldFromOtherFailures()
    {
        var taken = new IOException(
            "Failed to bind to address http://127.0.0.1:8443: address already in use.",
            new AddressInUseException("Address already in use"));

        Assert.True(StartFailure.IsPortTaken(taken));
        Assert.False(StartFailure.IsPortTaken(new IOException("the disk is full")));
        Assert.False(StartFailure.IsPortTaken(new InvalidOperationException("no service", new AddressInUseException("x"))));
    }

    [Fact]
    public void TheLineNamesTheAddressAndTheCommandThatMovesThePanel()
    {
        var taken = new IOException(
            "Failed to bind to address http://127.0.0.1:8443: address already in use.",
            new AddressInUseException("Address already in use"));

        var line = StartFailure.Line(taken);

        Assert.Equal(
            "the panel did not start: Failed to bind to address http://127.0.0.1:8443: address already in use; "
            + "free the port or move the panel with 'amneziageo-server panel set --port <port>'",
            line);
        Assert.DoesNotContain('\n', line);
        Assert.Equal(1, StartFailure.PortTaken);
    }
}
