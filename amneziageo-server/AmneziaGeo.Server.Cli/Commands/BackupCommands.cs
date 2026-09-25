using AmneziaGeo.Server.Dal;

namespace AmneziaGeo.Server.Cli.Commands;

/// <summary>
/// The commands that look a backup of the database over.
/// </summary>
public static class BackupCommands
{
    /// <summary>
    /// Runs one backup command and returns the exit code.
    /// </summary>
    public static int Run(Arguments args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.At(1) switch
        {
            "check" when args.At(2) is { Length: > 0 } file => Check(file),
            _ => Usage(),
        };
    }

    /// <summary>
    /// Prints ok for a sound database of the panel this release takes, or what is wrong with it.
    /// </summary>
    public static int Check(string file)
    {
        var said = DatabaseCheck.Inspect(file, DatabaseCheck.Newest());
        Terminal.Say(said);

        return said == DatabaseCheck.Sound ? 0 : 1;
    }

    private static int Usage()
    {
        Terminal.Fail("usage: amneziageo-server backup check <file>");

        return 2;
    }
}
