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
    "endpoint" => await EndpointCommands.RunAsync(context, arguments, ct).ConfigureAwait(false),
    "panel" => await PanelCommands.RunAsync(context, arguments, ct).ConfigureAwait(false),
    "update" => await UpdateCommands.RunAsync(context, arguments, ct).ConfigureAwait(false),
    "geo" => await GeoCommands.RunAsync(context, arguments, ct).ConfigureAwait(false),
    _ => Usage(),
};

static int Usage()
{
    Terminal.Fail("""
        usage:
          amneziageo-server                            open the menu
          amneziageo-server init [--user <login>]      make the first administrator
          amneziageo-server login [--user <login>]     sign in and print the tokens
          amneziageo-server refresh <token>            trade a refresh token for a pair
          amneziageo-server whoami                     what the panel makes of this host account
          amneziageo-server user list | add | passwd | role | enable | disable | remove
          amneziageo-server role list | add | set | remove
          amneziageo-server token list | add | revoke
          amneziageo-server import endpoint | peers | clients
          amneziageo-server endpoint list | open <name> | close <name>
          amneziageo-server panel show | get <name> | set <options> | reset
          amneziageo-server update status | check | apply [--beta]
          amneziageo-server geo update                 download the geo sources of the panel now
          amneziageo-server family [name]              resolve a netlink family
          amneziageo-server device list                name the amneziawg interfaces
          amneziageo-server device show [name]         read an interface with its peers
        """);

    return 2;
}
