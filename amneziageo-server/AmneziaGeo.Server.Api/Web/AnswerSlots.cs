namespace AmneziaGeo.Server.Api.Web;

/// <summary>
/// Bounds how many requests the web servers of the panel answer at once.
/// </summary>
public sealed class AnswerSlots : IDisposable
{
    /// <summary>
    /// How many requests are answered at once unless the settings say otherwise.
    /// </summary>
    public const int Default = 32;

    private readonly SemaphoreSlim? _free;

    /// <summary>
    /// ctor
    /// </summary>
    public AnswerSlots(int count)
    {
        Count = Math.Max(0, count);
        _free = Count > 0 ? new SemaphoreSlim(Count, Count) : null;
    }

    /// <summary>
    /// How many requests are answered at once, zero for every one.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// How many requests are being answered now.
    /// </summary>
    public int Taken => _free is null ? 0 : Count - _free.CurrentCount;

    /// <summary>
    /// Answers a request once a slot is free.
    /// </summary>
    public async Task AnswerAsync(Func<Task> answer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (_free is null)
        {
            await answer().ConfigureAwait(false);

            return;
        }

        await _free.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await answer().ConfigureAwait(false);
        }
        finally
        {
            _free.Release();
        }
    }

    /// <summary>
    /// Lets the slots go.
    /// </summary>
    public void Dispose() => _free?.Dispose();
}

/// <summary>
/// Puts the requests the panel answers after the services of its port under the slots.
/// </summary>
public static class AnswerGate
{
    /// <summary>
    /// Has every request that reaches it wait for a free slot, the host asking whether the panel runs aside.
    /// </summary>
    public static WebApplication UseAnswerSlots(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var slots = app.Services.GetRequiredService<AnswerSlots>();
        app.Use((context, next) => PanelServices.AsksHealth(context.Request.Path, context.Connection.RemoteIpAddress)
            ? next(context)
            : slots.AnswerAsync(() => next(context), context.RequestAborted));

        return app;
    }
}
