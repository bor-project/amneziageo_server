using System.Diagnostics;
using System.Text;

namespace AmneziaGeo.Server.Tests;

public class MenuReadingTests
{
    private static readonly string Menu = Path.Combine(AppContext.BaseDirectory, "menu", "amneziageo-server");

    [Fact]
    public async Task AReadingWithoutATerminal_PrintsWhatTheCommandWroteAndKeepsItsCode()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, said) = await MenuQuestionsTests.RunAsync("reading sh -c 'echo state; echo trouble >&2; exit 3'; echo \"code $?\"", string.Empty);

        Assert.Equal(0, exit);
        Assert.Equal("state\ncode 3\n", told);
        Assert.Equal("trouble\n", said);
    }

    [Fact]
    public async Task OnATerminal_KeysTypedWhileTheStateIsRead_NeitherShowNorReachTheQuestion()
    {
        Skip.IfNot(OperatingSystem.IsLinux() && File.Exists("/usr/bin/script"));

        await using var screen = Screen.Open(
            "before=$(stty -g); slow() { until read -r -t 0; do sleep 0.05; done; echo state; }; reading slow; " +
            "choice=$(ask 'Choose:'); printf 'chose [%s]\\n' \"$choice\"; " +
            "[ \"$(stty -g)\" = \"$before\" ] && echo 'the terminal is as before'");

        await screen.WaitAsync("reading...");
        await screen.TypeAsync("zz9\r");
        await screen.WaitAsync("Choose:");
        await screen.TypeAsync("7\r");
        var shown = await screen.EndAsync();

        Assert.Contains("state", shown, StringComparison.Ordinal);
        Assert.Contains("chose [7]", shown, StringComparison.Ordinal);
        Assert.Contains("the terminal is as before", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("zz9", shown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnATerminal_CtrlCWhileTheStateIsRead_StopsTheSpinnerAndGivesTheTerminalBack()
    {
        Skip.IfNot(OperatingSystem.IsLinux() && File.Exists("/usr/bin/script"));

        await using var screen = Screen.Open(
            "before=$(stty -g); trap 'echo stopped' INT; ( slow() { sleep 30; }; reading slow ); sleep 0.5; " +
            "[ \"$(stty -g)\" = \"$before\" ] && echo 'the terminal is as before'");

        await screen.WaitAsync("reading...");
        await screen.TypeAsync("\u0003");
        await screen.WaitAsync("stopped");
        var shown = await screen.EndAsync();
        var after = shown[(shown.IndexOf("stopped", StringComparison.Ordinal) + "stopped".Length)..];

        Assert.Contains("the terminal is as before", after, StringComparison.Ordinal);
        Assert.DoesNotContain("reading...", after, StringComparison.Ordinal);
    }

    // Words of the menu on a terminal of their own, given by script, with what the terminal shows kept.
    private sealed class Screen : IAsyncDisposable
    {
        private readonly Process _script;
        private readonly StringBuilder _shown = new();
        private readonly Task _reader;
        private int _seen;

        /// <summary>
        /// ctor
        /// </summary>
        private Screen(Process script)
        {
            _script = script;
            _reader = ReadAsync();
        }

        public static Screen Open(string words)
        {
            var start = new ProcessStartInfo("script")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
            };
            start.ArgumentList.Add("-qefc");
            start.ArgumentList.Add("bash -c \"$AGTEST_WORDS\" \"$AGTEST_MENU\"");
            start.ArgumentList.Add("/dev/null");
            start.Environment["AGTEST_WORDS"] = "source <(head -n -1 \"$0\"); " + words;
            start.Environment["AGTEST_MENU"] = Menu;
            start.Environment["TERM"] = "dumb";

            return new Screen(Process.Start(start)!);
        }

        public async Task WaitAsync(string text)
        {
            var limit = DateTime.UtcNow.AddSeconds(20);
            while (true)
            {
                lock (_shown)
                {
                    var at = _shown.ToString().IndexOf(text, _seen, StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        _seen = at + text.Length;
                        return;
                    }
                }

                if (DateTime.UtcNow > limit || _script.HasExited)
                {
                    throw new TimeoutException($"the terminal did not show {text}: {Shown()}");
                }

                await Task.Delay(20).ConfigureAwait(false);
            }
        }

        public async Task TypeAsync(string keys)
        {
            await _script.StandardInput.WriteAsync(keys).ConfigureAwait(false);
            await _script.StandardInput.FlushAsync().ConfigureAwait(false);
        }

        public async Task<string> EndAsync()
        {
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _script.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            await _reader.ConfigureAwait(false);

            return Shown();
        }

        public ValueTask DisposeAsync()
        {
            if (!_script.HasExited)
            {
                _script.Kill(true);
            }

            _script.Dispose();

            return ValueTask.CompletedTask;
        }

        private string Shown()
        {
            lock (_shown)
            {
                return _shown.ToString();
            }
        }

        private async Task ReadAsync()
        {
            var buffer = new char[4096];
            var count = await _script.StandardOutput.ReadAsync(buffer).ConfigureAwait(false);
            while (count > 0)
            {
                lock (_shown)
                {
                    _shown.Append(buffer, 0, count);
                }

                count = await _script.StandardOutput.ReadAsync(buffer).ConfigureAwait(false);
            }
        }
    }
}
