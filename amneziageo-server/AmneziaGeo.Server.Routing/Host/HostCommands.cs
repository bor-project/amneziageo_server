using System.Diagnostics;
using System.Text;

namespace AmneziaGeo.Server.Routing.Host;

/// <summary>
/// What running a command left.
/// </summary>
/// <param name="Code">The number the command ended with.</param>
/// <param name="Output">What the command wrote out.</param>
/// <param name="Error">What the command wrote to the error stream.</param>
public sealed record CommandResult(int Code, string Output, string Error)
{
    /// <summary>
    /// Tells whether the command went through.
    /// </summary>
    public bool IsOk => Code == 0;

    /// <summary>
    /// Returns what the command said about the failure.
    /// </summary>
    public string Complaint => Error.Length > 0 ? Error.Trim() : Output.Trim();
}

/// <summary>
/// Runs the host tools the routing is put on the host with.
/// </summary>
public interface IHostCommands
{
    /// <summary>
    /// Runs a tool with the arguments and the standard input it takes.
    /// </summary>
    Task<CommandResult> RunAsync(string file, IReadOnlyList<string> arguments, string? input, CancellationToken ct);
}

/// <summary>
/// Runs the host tools as processes of the server.
/// </summary>
public sealed class HostCommands : IHostCommands
{
    /// <summary>
    /// How long a tool is given before it is taken down.
    /// </summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    private readonly bool _sudo;

    /// <summary>
    /// ctor
    /// </summary>
    public HostCommands(bool sudo = false)
    {
        _sudo = sudo;
    }

    /// <summary>
    /// Runs a tool with the arguments and the standard input it takes.
    /// </summary>
    public async Task<CommandResult> RunAsync(
        string file,
        IReadOnlyList<string> arguments,
        string? input,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentNullException.ThrowIfNull(arguments);

        var start = new ProcessStartInfo
        {
            FileName = _sudo ? "sudo" : file,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (_sudo)
        {
            start.ArgumentList.Add("-n");
            start.ArgumentList.Add(file);
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new HostNetworkException($"the host did not start '{file}'");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);

        var output = new StringBuilder();
        var error = new StringBuilder();
        var reading = Read(process.StandardOutput, output, limit.Token);
        var complaining = Read(process.StandardError, error, limit.Token);

        await process.StandardInput.WriteAsync(input ?? string.Empty).ConfigureAwait(false);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            await reading.ConfigureAwait(false);
            await complaining.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Stop(process);

            throw new HostNetworkException($"'{file}' did not answer in {Limit.TotalSeconds:0} seconds");
        }

        return new CommandResult(process.ExitCode, output.ToString(), error.ToString());
    }

    private static async Task Read(StreamReader stream, StringBuilder into, CancellationToken ct)
    {
        into.Append(await stream.ReadToEndAsync(ct).ConfigureAwait(false));
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
