using System.Net;
using AmneziaGeo.Server.Api.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AmneziaGeo.Server.Tests;

public class AnswerSlotsTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ARequestOverTheBoundWaitsUntilASlotIsFree()
    {
        using var slots = new AnswerSlots(2);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = slots.AnswerAsync(() => held.Task, CancellationToken.None);
        var second = slots.AnswerAsync(() => held.Task, CancellationToken.None);
        var answered = false;

        var third = slots.AnswerAsync(
            () =>
            {
                answered = true;

                return Task.CompletedTask;
            },
            CancellationToken.None);
        var early = answered;
        var taken = slots.Taken;
        held.SetResult();
        await Task.WhenAll(first, second, third).WaitAsync(Patience);

        Assert.False(early);
        Assert.Equal(2, taken);
        Assert.True(answered);
        Assert.Equal(0, slots.Taken);
    }

    [Fact]
    public async Task ZeroAnswersEveryRequestAtOnce()
    {
        using var slots = new AnswerSlots(0);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = Enumerable.Range(0, 100).Select(_ => slots.AnswerAsync(() => held.Task, CancellationToken.None)).ToList();
        var answered = false;

        await slots.AnswerAsync(
                () =>
                {
                    answered = true;

                    return Task.CompletedTask;
                },
                CancellationToken.None)
            .WaitAsync(Patience);
        held.SetResult();
        await Task.WhenAll(waiting).WaitAsync(Patience);

        Assert.True(answered);
    }

    [Fact]
    public async Task ARequestThatGivesUpWhileWaitingTakesNoSlot()
    {
        using var slots = new AnswerSlots(1);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = slots.AnswerAsync(() => held.Task, CancellationToken.None);
        using var gone = new CancellationTokenSource();

        var waiting = slots.AnswerAsync(() => Task.CompletedTask, gone.Token);
        await gone.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        held.SetResult();
        await first.WaitAsync(Patience);
        Assert.Equal(0, slots.Taken);
    }

    [Fact]
    public async Task AnAnswerThatFailsGivesItsSlotBack()
    {
        using var slots = new AnswerSlots(1);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => slots.AnswerAsync(() => throw new InvalidOperationException("the answer broke"), CancellationToken.None));

        Assert.Equal(0, slots.Taken);
        await slots.AnswerAsync(() => Task.CompletedTask, CancellationToken.None).WaitAsync(Patience);
    }

    [Fact]
    public async Task ThePanelAnswersTheHostAndTheServicesWhileEverySlotIsTaken()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(_ => new AnswerSlots(1));
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/service")
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;

                return;
            }

            await next(context);
        });
        app.UseAnswerSlots();
        app.Run(async context =>
        {
            if (context.Request.Path == "/held")
            {
                await held.Task;
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        var slots = app.Services.GetRequiredService<AnswerSlots>();
        using var http = new HttpClient { BaseAddress = new Uri(address) };

        var holding = http.GetAsync("/held");
        await UntilAsync(() => slots.Taken == 1);
        var waiting = http.GetAsync("/panel");
        using var service = await http.GetAsync("/service").WaitAsync(Patience);
        using var health = await http.GetAsync(PanelServices.HealthPath).WaitAsync(Patience);
        var early = waiting.IsCompleted;
        held.SetResult();
        using var late = await waiting.WaitAsync(Patience);
        using var first = await holding.WaitAsync(Patience);
        await app.StopAsync();

        Assert.Equal(HttpStatusCode.Accepted, service.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, health.StatusCode);
        Assert.False(early);
        Assert.Equal(HttpStatusCode.NoContent, late.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
    }

    // Waits until a condition comes true, failing once the patience runs out.
    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "the condition did not come true in time");
            await Task.Delay(10);
        }
    }
}
