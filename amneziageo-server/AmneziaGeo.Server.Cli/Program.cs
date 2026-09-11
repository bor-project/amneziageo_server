using AmneziaGeo.Server.Cli;
using AmneziaGeo.Server.Cli.Commands;

var arguments = new Arguments(args);
var ct = CancellationToken.None;

if (arguments.At(0) == "family")
{
    return FamilyCommand.Run(arguments);
}

if (arguments.At(0) == "device")
{
    return DeviceCommands.Run(arguments);
}

if (arguments.At(0) == "relay")
{
    return await RelayCommands.RunAsync(arguments, ct).ConfigureAwait(false);
}

using var context = Context.Open(arguments.Value("db"));

return arguments.At(0) switch
{
    null => await Menu.RunAsync(context, ct).ConfigureAwait(false),
    "init" => await LoginCommands.InitAsync(context, arguments, ct).ConfigureAwait(false),
    "login" => await LoginCommands.LoginAsync(context, arguments, ct).ConfigureAwait(false),
    "refresh" => await LoginCommands.RefreshAsync(context, arguments, ct).ConfigureAwait(false),
    "whoami" => await LoginCommands.WhoAsync(context, ct).ConfigureAwait(false),
    "user" => await UserCommands.RunAsync(context, arguments, ct).ConfigureAwait(false),
    "role" => await RoleCommands.RunAsync(context, arguments, ct).ConfigureAwait(false),
    "token" => await TokenCommands.RunAsync(context, arguments, ct).ConfigureAwait(false),
    "import" => await ImportCommands.RunAsync(context, arguments, ct).ConfigureAwait(false),
    _ => Usage(),
};

static int Usage()
{
    Terminal.Fail("""
        usage:
          amneziageo-server-cli                            open the menu
          amneziageo-server-cli init [--user <login>]      make the first administrator
          amneziageo-server-cli login [--user <login>]     sign in and print the tokens
          amneziageo-server-cli refresh <token>            trade a refresh token for a pair
          amneziageo-server-cli whoami                     what the panel makes of this host account
          amneziageo-server-cli user list | add | passwd | role | enable | disable | remove
          amneziageo-server-cli role list | add | set | remove
          amneziageo-server-cli token list | add | revoke
          amneziageo-server-cli import endpoint | peers | clients
          amneziageo-server-cli family [name]              resolve a netlink family
          amneziageo-server-cli device list                 name the amneziawg interfaces
          amneziageo-server-cli device show [name]          read an interface with its peers
          amneziageo-server-cli relay --listen <address:port> --target <host:port>
        """);

    return 2;
}
