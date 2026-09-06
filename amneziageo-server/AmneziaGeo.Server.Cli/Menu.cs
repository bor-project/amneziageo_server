using AmneziaGeo.Server.Cli.Commands;

namespace AmneziaGeo.Server.Cli;

/// <summary>
/// The menu the utility opens when it is started without a command.
/// </summary>
public static class Menu
{
    /// <summary>
    /// Runs the menu until it is left.
    /// </summary>
    public static async Task<int> RunAsync(Context context, CancellationToken ct)
    {
        while (true)
        {
            Terminal.Say(string.Empty);
            Terminal.Say("AmneziaGeo server");
            Terminal.Say("  1  users");
            Terminal.Say("  2  roles");
            Terminal.Say("  3  sign in");
            Terminal.Say("  4  who am i");
            Terminal.Say("  5  kernel");
            Terminal.Say("  0  quit");

            switch (Terminal.Ask("> "))
            {
                case "1":
                    await UsersAsync(context, ct).ConfigureAwait(false);
                    break;

                case "2":
                    await RolesAsync(context, ct).ConfigureAwait(false);
                    break;

                case "3":
                    await LoginCommands.LoginAsync(context, new Arguments(["login"]), ct).ConfigureAwait(false);
                    break;

                case "4":
                    await LoginCommands.WhoAsync(context, ct).ConfigureAwait(false);
                    break;

                case "5":
                    FamilyCommand.Run(new Arguments(["family"]));
                    break;

                case "0":
                case "":
                    return 0;

                default:
                    Terminal.Fail("there is no such item");
                    break;
            }
        }
    }

    private static async Task RolesAsync(Context context, CancellationToken ct)
    {
        while (true)
        {
            Terminal.Say(string.Empty);
            Terminal.Say("roles");
            Terminal.Say("  1  list");
            Terminal.Say("  2  add");
            Terminal.Say("  3  set");
            Terminal.Say("  4  remove");
            Terminal.Say("  0  back");

            switch (Terminal.Ask("> "))
            {
                case "1":
                    await RoleCommands.ListAsync(context, ct).ConfigureAwait(false);
                    break;

                case "2":
                    await RoleCommands.AddAsync(context, new Arguments(["role", "add"]), ct).ConfigureAwait(false);
                    break;

                case "3":
                    await RoleCommands.SetAsync(context, new Arguments(["role", "set"]), ct).ConfigureAwait(false);
                    break;

                case "4":
                    await RoleCommands.RemoveAsync(context, new Arguments(["role", "remove"]), ct).ConfigureAwait(false);
                    break;

                case "0":
                case "":
                    return;

                default:
                    Terminal.Fail("there is no such item");
                    break;
            }
        }
    }

    private static async Task UsersAsync(Context context, CancellationToken ct)
    {
        while (true)
        {
            Terminal.Say(string.Empty);
            Terminal.Say("users");
            Terminal.Say("  1  list");
            Terminal.Say("  2  add");
            Terminal.Say("  3  password");
            Terminal.Say("  4  role");
            Terminal.Say("  5  enable");
            Terminal.Say("  6  disable");
            Terminal.Say("  7  remove");
            Terminal.Say("  0  back");

            switch (Terminal.Ask("> "))
            {
                case "1":
                    await UserCommands.ListAsync(context, ct).ConfigureAwait(false);
                    break;

                case "2":
                    await UserCommands.AddAsync(context, new Arguments(["user", "add"]), ct).ConfigureAwait(false);
                    break;

                case "3":
                    await UserCommands.PasswordAsync(context, new Arguments(["user", "passwd"]), ct).ConfigureAwait(false);
                    break;

                case "4":
                    await UserCommands.RoleAsync(context, new Arguments(["user", "role"]), ct).ConfigureAwait(false);
                    break;

                case "5":
                    await UserCommands.EnabledAsync(context, new Arguments(["user", "enable"]), enabled: true, ct).ConfigureAwait(false);
                    break;

                case "6":
                    await UserCommands.EnabledAsync(context, new Arguments(["user", "disable"]), enabled: false, ct).ConfigureAwait(false);
                    break;

                case "7":
                    await UserCommands.RemoveAsync(context, new Arguments(["user", "remove"]), ct).ConfigureAwait(false);
                    break;

                case "0":
                case "":
                    return;

                default:
                    Terminal.Fail("there is no such item");
                    break;
            }
        }
    }
}
