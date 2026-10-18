namespace AmneziaGeo.Server.Tests;

public class MenuShortcutTests
{
    [Theory]
    [InlineData("ag-s", "", "")]
    [InlineData("ag s", "", "'ag s' cannot name a command")]
    [InlineData("-s", "", "'-s' cannot name a command")]
    [InlineData("amneziageo-server", "", "amneziageo-server is taken: ")]
    [InlineData("ag-s", "file", "ag-s is taken: ")]
    [InlineData("ag-s", "link", "")]
    [InlineData("ag-s", "other link", "ag-s is taken: ")]
    [InlineData("cd", "", "cd is taken: a command of the shell")]
    [InlineData("ag-s", "program", "ag-s is taken: ")]
    public async Task TheNameOfAShortCommand_IsOneNoCommandHoldsOrALinkToTheMenuAlready(string name, string held, string refused)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var self = folder.Write("bin/amneziageo-server", "#!/usr/bin/env bash\n");
        var link = System.IO.Path.Combine(folder.Path, "bin", name);
        var path = System.IO.Path.Combine(folder.Path, "path");
        Directory.CreateDirectory(path);
        switch (held)
        {
            case "file":
                File.WriteAllText(link, "#!/bin/sh\n");
                break;
            case "link":
                File.CreateSymbolicLink(link, self);
                break;
            case "other link":
                File.CreateSymbolicLink(link, "/bin/true");
                break;
            case "program":
                File.WriteAllText(System.IO.Path.Combine(path, name), "#!/bin/sh\n");
                break;
        }

        var (exit, _, said) = await MenuQuestionsTests.RunAsync($"self={self}; chmod 755 {path}/* 2> /dev/null; PATH={path}:$PATH; shortcut_ok '{name}'", string.Empty);

        if (refused.Length == 0)
        {
            Assert.Equal(0, exit);
            Assert.Empty(said);
        }
        else
        {
            Assert.Equal(1, exit);
            Assert.Contains(refused, said, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("", "ag-s")]
    [InlineData("ag-s", "ag-s1")]
    [InlineData("ag-s ag-s1", "ag-s2")]
    public async Task TheShortCommandOffered_IsTheFirstFreeOfAgSAndItWithANumber(string held, string offered)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var self = folder.Write("bin/amneziageo-server", "#!/usr/bin/env bash\n");
        foreach (var name in held.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            folder.Write($"bin/{name}", "#!/bin/sh\n");
        }

        var (_, told, said) = await MenuQuestionsTests.RunAsync($"self={self}; free_shortcut ag-s", string.Empty);

        Assert.Equal(offered, told);
        Assert.Empty(said);
    }

    [Theory]
    [InlineData("\n\n", "ag-s", "Name of the short command: ag-s\n", "")]
    [InlineData("y\nmy-s\n", "my-s", "Name of the short command: my-s\n", "")]
    [InlineData("n\n", "", "", "")]
    [InlineData("\nag-s\nmy-s\n", "my-s", "Name of the short command: ag-s\nName of the short command: my-s\n", "ag-s is taken: ")]
    public async Task AShortCommand_IsAskedOnATerminalWithAgSOfferedAndAnotherNameAskedForWhereItIsTaken(
        string input, string taken, string asked, string refused)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var self = folder.Write("bin/amneziageo-server", "#!/usr/bin/env bash\n");
        if (refused.Length > 0)
        {
            folder.Write("bin/ag-s", "#!/bin/sh\n");
        }

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            $"self={self}; on_terminal() {{ :; }}; ask_shortcut; printf 'taken=%s\\n' \"${{given[shortcut]}}\"", input);

        Assert.Equal($"{asked}taken={taken}\n", told);
        if (refused.Length == 0)
        {
            Assert.Empty(said);
        }
        else
        {
            Assert.Contains(refused, said, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("", "", "", "")]
    [InlineData("given[shortcut]=ag-s; ", "ag-s", "Name of the short command: ag-s\n", "")]
    [InlineData("given[shortcut]=ag-s; ", "", "Name of the short command: ag-s\n", "ag-s is taken: ")]
    [InlineData("given[shortcut]=; ", "", "", "")]
    public async Task WithoutATerminal_NoShortCommandIsAskedForAndOnlyTheOneOfTheAnswersGoesOn(
        string answers, string taken, string told, string refused)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var self = folder.Write("bin/amneziageo-server", "#!/usr/bin/env bash\n");
        if (refused.Length > 0)
        {
            folder.Write("bin/ag-s", "#!/bin/sh\n");
        }

        var (exit, said, warned) = await MenuQuestionsTests.RunAsync(
            $"self={self}; {answers}ask_shortcut; printf 'taken=%s\\n' \"${{given[shortcut]}}\"; read -r line; echo \"left $line\"",
            "an answer of the next question\n");

        Assert.Equal(0, exit);
        Assert.Equal($"{told}taken={taken}\nleft an answer of the next question\n", said);
        if (refused.Length == 0)
        {
            Assert.Empty(warned);
        }
        else
        {
            Assert.Contains(refused, warned, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("1\n\n\n", "ag-s")]
    [InlineData("2\n\ny\n\n\n", "ag-s")]
    [InlineData("2\n\nn\n", "")]
    public async Task TheShortCommand_IsAskedForAfterThePackageOrTheCommandOnTheHostOfAContainer(string input, string taken)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var self = System.IO.Path.Combine(folder.Path, "bin", "amneziageo-server");
        Directory.CreateDirectory(System.IO.Path.Combine(folder.Path, "bin"));
        var directory = System.IO.Path.Combine(folder.Path, "compose");

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            $"self={self}; docker_dir={directory}; seen[docker]=29; docker_lacks() {{ :; }}; on_terminal() {{ :; }}; " +
            "eval \"$(declare -f answer_yes | sed 1s/answer_yes/kept_answer_yes/)\"; " +
            "answer_yes() { if [ \"$1\" = upgrade ]; then printf 'taken=%s\\n' \"${given[shortcut]-unset}\"; exit 0; fi; kept_answer_yes \"$@\"; }; " +
            "ask_installation",
            input);

        Assert.EndsWith($"taken={taken}\n", told, StringComparison.Ordinal);
        Assert.Equal(taken.Length > 0, told.Contains("Name of the short command: ag-s\n", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "made")]
    [InlineData("link", "made")]
    [InlineData("file", "kept")]
    [InlineData("no menu", "none")]
    public async Task TheShortCommand_GoesOnAsALinkToTheMenuAndLeavesWhatHoldsItsNameAlone(string held, string left)
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var self = System.IO.Path.Combine(folder.Path, "bin", "amneziageo-server");
        var link = System.IO.Path.Combine(folder.Path, "bin", "ag-s");
        Directory.CreateDirectory(System.IO.Path.Combine(folder.Path, "bin"));
        if (held != "no menu")
        {
            File.WriteAllText(self, "#!/usr/bin/env bash\necho the menu\n");
        }

        if (held == "link")
        {
            File.CreateSymbolicLink(link, self);
        }
        else if (held == "file")
        {
            File.WriteAllText(link, "#!/bin/sh\necho a tool of its own\n");
        }

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            $"self={self}; given[shortcut]=ag-s; put_shortcut; printf 'made=%s\\n' \"$made_shortcut\"", string.Empty);

        switch (left)
        {
            case "made":
                Assert.Equal(self, new FileInfo(link).LinkTarget);
                Assert.Equal("the short command ag-s runs amneziageo-server\nmade=ag-s\n", told);
                Assert.Empty(said);
                break;
            case "kept":
                Assert.Equal("#!/bin/sh\necho a tool of its own\n", File.ReadAllText(link));
                Assert.Equal("made=\n", told);
                Assert.Contains($"{link} is taken", said, StringComparison.Ordinal);
                break;
            default:
                Assert.False(File.Exists(link));
                Assert.Equal("made=\n", told);
                Assert.Empty(said);
                break;
        }
    }

    [Fact]
    public async Task TakingTheMenuOff_TakesTheLinksToItAlongAndLeavesTheOthers()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        var self = folder.Write("bin/amneziageo-server", "#!/usr/bin/env bash\n# Manages the AmneziaGeo server on this host: the menu.\n");
        var bin = System.IO.Path.Combine(folder.Path, "bin");
        File.CreateSymbolicLink(System.IO.Path.Combine(bin, "ag-s"), self);
        File.CreateSymbolicLink(System.IO.Path.Combine(bin, "my-s"), self);
        File.CreateSymbolicLink(System.IO.Path.Combine(bin, "other"), "/bin/true");
        folder.Write("bin/tool", "#!/bin/sh\n");

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            $"self={self}; given[host_menu]=no; kind() {{ printf docker; }}; settle_self", string.Empty);

        Assert.Empty(said);
        Assert.Equal("removed the short command ag-s\nremoved the short command my-s\n", told);
        var left = Directory.GetFileSystemEntries(bin).Select(entry => new FileInfo(entry).Name).Order().ToArray();
        Assert.Equal(new[] { "other", "tool" }, left);
    }

    [Theory]
    [InlineData("ag-s", "the menu: amneziageo-server, or ag-s\n")]
    [InlineData("", "the menu: amneziageo-server\n")]
    public async Task TheEndOfTheInstallation_NamesTheShortCommand(string made, string line)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            "check_status() { :; }; setting() { :; }; show_address() { :; }; held_ports() { :; }; ipv6_line() { :; }; " +
            $"given[kind]=package; made_shortcut={made}; final_check",
            string.Empty);

        Assert.EndsWith(line, told, StringComparison.Ordinal);
    }
}
