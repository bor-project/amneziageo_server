namespace AmneziaGeo.Server.Tests;

public class MenuResolverTests
{
    // A ufw that lets the resolver in on awg0 only, and that tells on its error stream what it was given.
    private const string Ufw = """
        ufw() {
          if [ "$1" = status ]; then
            cat << 'EOF'
        Status: active
        Logging: on (low)
        Default: deny (incoming), allow (outgoing), allow (routed)
        New profiles: skip

        To                         Action      From
        --                         ------      ----
        22/tcp                     ALLOW IN    Anywhere
        53 on awg0                 ALLOW IN    Anywhere                   # amneziageo resolver awg0
        53 on awg2                 ALLOW IN    10.0.2.0/24
        Anywhere on awg3           ALLOW IN    Anywhere
        53/tcp on awg4             ALLOW IN    Anywhere
        50:60/udp on awg5          ALLOW IN    Anywhere
        53 (v6) on awg0            ALLOW IN    Anywhere (v6)
        EOF
          else
            echo "ufw: $*" >&2
          fi
        }

        """;

    // A panel on a package with the resolver on port 53 and the endpoints awg0 and awg1.
    private const string Panel = """
        kind() { printf package; }
        cli() {
          case "$*" in
            'dns get enabled') echo "${resolver:-on}" ;;
            'dns get port') echo 53 ;;
            'endpoint list') printf 'awg0: port 51820, left to the host\nawg1: port 443, left to the host\n' ;;
            'dns set --enabled on') echo "utility: $*" ;;
          esac
        }

        """;

    [Theory]
    [InlineData("awg0", 0)]
    [InlineData("awg1", 1)]
    [InlineData("awg2", 1)]
    [InlineData("awg3", 0)]
    [InlineData("awg4", 1)]
    [InlineData("awg5", 0)]
    public async Task UfwLetsTheResolverInOnAnInterfaceByARuleForItOrForEveryOne(string name, int code)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, _, _) = await MenuQuestionsTests.RunAsync(Ufw + $"ufw_lets_in {name} 53", string.Empty);

        Assert.Equal(code, exit);
    }

    [Theory]
    [InlineData("on", "package", "awg1\n")]
    [InlineData("off", "package", "")]
    [InlineData("on", "container", "")]
    public async Task TheMenuNamesTheEndpointsUfwKeepsFromTheResolver(string resolver, string kind, string kept)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync(
            Ufw + Panel + $"resolver={resolver}; kind() {{ printf {kind}; }}; ufw_active() {{ :; }}; resolver_kept",
            string.Empty);

        Assert.Equal(kept, told);
    }

    [Fact]
    public async Task ItemThirteenLetsTheClientsUfwKeepsOutReachTheResolver()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, said) = await MenuQuestionsTests.RunAsync(Ufw + Panel + "resolver_open", string.Empty);

        Assert.Equal(0, exit);
        Assert.Contains("ufw: allow in on awg1 to any port 53 comment amneziageo resolver awg1\n", said, StringComparison.Ordinal);
        Assert.DoesNotContain("on awg0", said, StringComparison.Ordinal);
        Assert.Contains("ufw lets the clients of awg1 reach the resolver on port 53", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InTheContainerTheItemNamesWhatToRunOnTheHost()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (exit, told, said) = await MenuQuestionsTests.RunAsync(Ufw + Panel + "kind() { printf container; }; resolver_open", string.Empty);

        Assert.Equal(1, exit);
        Assert.Contains("run this on the host", said, StringComparison.Ordinal);
        Assert.Contains("ufw allow in on awg0 to any port 53; ufw allow in on awg1 to any port 53", told, StringComparison.Ordinal);
        Assert.DoesNotContain("ufw: allow", said, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("y\n", true)]
    [InlineData("n\n", false)]
    public async Task TheResolverItemOffersToLetTheClientsReachTheResolver(string answer, bool opened)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, said) = await MenuQuestionsTests.RunAsync(Ufw + Panel + "resolver_item on", answer);

        Assert.Contains("utility: dns set --enabled on", told, StringComparison.Ordinal);
        Assert.Equal(opened, said.Contains("ufw: allow in on awg1 to any port 53", StringComparison.Ordinal));
        Assert.Equal(opened, told.Contains("ufw lets the clients of awg1 reach the resolver on port 53", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("yes", true)]
    [InlineData("no", false)]
    public async Task TheInstallationOffersToLetTheClientsReachTheResolver(string answer, bool opened)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, said) = await MenuQuestionsTests.RunAsync(Ufw + Panel + $"given[resolver_ufw]={answer}; install_doors", string.Empty);

        Assert.Contains("ufw keeps the clients of awg1 from the resolver of the panel: let them reach it?", told, StringComparison.Ordinal);
        Assert.Equal(opened, said.Contains("ufw: allow in on awg1 to any port 53", StringComparison.Ordinal));
    }
}
