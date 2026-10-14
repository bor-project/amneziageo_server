using System.Globalization;
using System.Text.Json;
using AmneziaGeo.Server.Api.Web;

namespace AmneziaGeo.Server.Tests;

public class RuntimeSettingsTests
{
    [Fact]
    public void TheThreadsOfThePoolOfThePanelWaitForWorkWithoutSpinning()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "AmneziaGeo.Server.Api.runtimeconfig.json");
        using var told = JsonDocument.Parse(File.ReadAllText(file));
        var settings = told.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        Assert.True(settings.TryGetProperty("System.Threading.ThreadPool.UnfairSemaphoreSpinLimit", out var spins));
        Assert.Equal("0", spins.ToString());
    }

    [Fact]
    public void ThePoolOfThePanelHoldsRoomForTheThreadsTheRequestsMayHold()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "AmneziaGeo.Server.Api.runtimeconfig.json");
        using var told = JsonDocument.Parse(File.ReadAllText(file));
        var settings = told.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");

        Assert.True(settings.TryGetProperty("System.Threading.ThreadPool.MinThreads", out var least));
        Assert.Equal("64", least.ToString());
        Assert.True(AnswerSlots.Default * 2 <= int.Parse(least.ToString(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ThePoolOfTheTestsHoldsRoomForTheThreadsTheTestsMayHold()
    {
        ThreadPool.GetMinThreads(out var workers, out _);

        Assert.True(workers >= 64, $"the pool of the tests starts {workers} threads without waiting");
    }
}
