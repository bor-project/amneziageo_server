using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace AmneziaGeo.Server.Cli;

/// <summary>
/// Reads answers from the console and writes lines to it.
/// </summary>
public static class Terminal
{
    /// <summary>
    /// Asks for a line of text.
    /// </summary>
    public static string Ask(string prompt, string? fallback = null)
    {
        Console.Write(prompt);
        var answer = Console.ReadLine();

        return string.IsNullOrWhiteSpace(answer) ? fallback ?? string.Empty : answer.Trim();
    }

    /// <summary>
    /// Asks for a password without echoing it.
    /// </summary>
    public static string AskSecret(string prompt)
    {
        Console.Write(prompt);

        if (Console.IsInputRedirected)
        {
            var piped = Console.ReadLine() ?? string.Empty;

            return piped;
        }

        var typed = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();

                return typed.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }
    }

    /// <summary>
    /// Asks for a password twice and returns it when both answers match.
    /// </summary>
    public static string? AskNewSecret()
    {
        var first = AskSecret("password: ");
        var again = AskSecret("repeat: ");

        if (!string.Equals(first, again, StringComparison.Ordinal))
        {
            Fail("the two answers differ");

            return null;
        }

        return first;
    }

    /// <summary>
    /// Asks a yes or no question.
    /// </summary>
    public static bool Confirm(string prompt)
    {
        Console.Write(prompt + " [y/N]: ");
        var answer = Console.ReadLine();

        return answer is not null && answer.Trim().StartsWith('y');
    }

    /// <summary>
    /// Returns a random password.
    /// </summary>
    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(15));

    /// <summary>
    /// Writes a line to the output.
    /// </summary>
    public static void Say(string line) => Console.WriteLine(line);

    /// <summary>
    /// Writes a line to the error output.
    /// </summary>
    public static void Fail(string line) => Console.Error.WriteLine(line);
}
