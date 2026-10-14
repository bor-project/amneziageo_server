using System.Diagnostics;

namespace AmneziaGeo.Server.Tests;

public class MenuQuestionsTests
{
    private static readonly string Menu = Path.Combine(AppContext.BaseDirectory, "menu", "amneziageo-server");

    [Theory]
    [InlineData("confirm", "y\n", 0, 0)]
    [InlineData("confirm", "YES\n", 0, 0)]
    [InlineData("confirm", "\n", 1, 0)]
    [InlineData("confirm", "n\n", 1, 0)]
    [InlineData("confirm", "д\n1\nyes\n", 0, 2)]
    [InlineData("confirm", "yx\nn\n", 1, 1)]
    [InlineData("confirm", "", 2, 0)]
    [InlineData("agree", "\n", 0, 0)]
    [InlineData("agree", "No\n", 1, 0)]
    [InlineData("agree", "nope\ny\n", 0, 1)]
    [InlineData("agree", "x\n", 2, 1)]
    public async Task AYesOrNoQuestionTakesYesOrNoAndAsksAgainForAnythingElse(string question, string input, int code, int again)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, _, said) = await RunAsync($"{question} 'Go on?'", input);

        Assert.Equal(code, exit);
        Assert.Equal(again, Count(said, "answer y or n"));
    }

    [Theory]
    [InlineData("yes", "д\nn\n", "no", 1)]
    [InlineData("yes", "\n", "yes", 0)]
    [InlineData("no", "\n", "no", 0)]
    [InlineData("no", "", "no", 0)]
    [InlineData("yes", "", "yes", 0)]
    [InlineData("no", "Y\n", "yes", 0)]
    public async Task AQuestionOfTheInstallationTakesYesOrNoAndItsDefaultForAnEmptyAnswer(string fallback, string input, string taken, int again)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, said) = await RunAsync($"answer_yes opened 'Hold the port open?' {fallback}; printf 'taken %s\\n' \"${{given[opened]}}\"", input);

        Assert.Contains($"taken {taken}\n", told, StringComparison.Ordinal);
        Assert.Equal(again, Count(said, "answer y or n"));
    }

    // Runs words of the menu with its functions read in and the lines given on its input.
    private static async Task<(int Exit, string Told, string Said)> RunAsync(string words, string input)
    {
        var start = new ProcessStartInfo("bash")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("source <(head -n -1 \"$0\"); " + words);
        start.ArgumentList.Add(Menu);
        using var bash = Process.Start(start)!;
        var told = bash.StandardOutput.ReadToEndAsync();
        var said = bash.StandardError.ReadToEndAsync();
        await bash.StandardInput.WriteAsync(input);
        bash.StandardInput.Close();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await bash.WaitForExitAsync(limit.Token);

        return (bash.ExitCode, await told, await said);
    }

    private static int Count(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
