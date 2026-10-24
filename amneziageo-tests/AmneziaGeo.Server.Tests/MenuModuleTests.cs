namespace AmneziaGeo.Server.Tests;

public class MenuModuleTests
{
    // The calls of apt-get, printed with the lines about what failed.
    private const string Apt = "apt-get() { echo \"apt-get $*\" >&2; }; ";

    [Theory]
    [InlineData("6.8.0-142-generic", "linux-image-generic linux-image-6.8.0-142-generic linux-image-unsigned-6.8.0-142-generic",
        "linux-headers-generic linux-headers-6.8.0-142-generic", "linux-headers-generic\n")]
    [InlineData("6.8.0-142-generic", "linux-image-virtual linux-image-6.8.0-142-generic",
        "linux-headers-virtual linux-headers-generic", "linux-headers-virtual\n")]
    [InlineData("6.8.0-45-generic", "linux-image-generic-hwe-22.04 linux-image-6.8.0-45-generic",
        "linux-headers-generic-hwe-22.04 linux-headers-generic", "linux-headers-generic-hwe-22.04\n")]
    [InlineData("6.12.113+deb13-amd64", "linux-image-amd64 linux-image-6.12.113+deb13-amd64",
        "linux-headers-amd64 linux-headers-6.12.113+deb13-amd64", "linux-headers-amd64\n")]
    [InlineData("6.12.43+deb13-cloud-amd64", "linux-image-cloud-amd64 linux-image-6.12.43+deb13-cloud-amd64",
        "linux-headers-cloud-amd64 linux-headers-amd64", "linux-headers-cloud-amd64\n")]
    [InlineData("6.1.0-25-arm64", "linux-image-arm64 linux-image-6.1.0-25-arm64", "linux-headers-arm64", "linux-headers-arm64\n")]
    public async Task TheHeadersToFollow_AreTheOnesOfTheKernelMetapackagesOnTheHost(string kernel, string on, string apt, string metas)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync(Host(kernel, on, apt) + "headers_metas", string.Empty);

        Assert.Equal(metas, told);
    }

    [Theory]
    [InlineData("6.12.113+deb13-amd64", "linux-headers-amd64", "linux-headers-amd64\n")]
    [InlineData("6.12.43+deb13-cloud-amd64", "linux-headers-cloud-amd64 linux-headers-amd64", "linux-headers-cloud-amd64\n")]
    [InlineData("6.8.0-142-generic", "linux-headers-generic", "linux-headers-generic\n")]
    [InlineData("6.1.0-25-amd64", "linux-headers-amd64", "linux-headers-amd64\n")]
    [InlineData("6.8.0-142-generic", "", "")]
    [InlineData("6.6.0", "linux-headers-6.6.0", "")]
    public async Task WithoutAKernelMetapackage_TheHeadersToFollow_AreTheOnesOfTheFlavourOfTheKernel(string kernel, string apt, string metas)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, _) = await MenuQuestionsTests.RunAsync(Host(kernel, $"linux-image-{kernel}", apt) + "headers_metas", string.Empty);

        Assert.Equal(metas, told);
    }

    [Theory]
    [InlineData("linux-headers-amd64", "linux-headers-amd64 did not install")]
    [InlineData("", "")]
    public async Task TheInstallationOfTheModule_PutsTheHeadersToFollowOn_AndSaysWhenTheyDoNotGoOn(string broken, string complaint)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, _, said) = await MenuQuestionsTests.RunAsync(
            Host("6.12.113+deb13-amd64", "linux-image-amd64", "linux-headers-amd64") +
            $"apt-get() {{ echo \"apt-get $*\" >&2; [ \"${{@: -1}}\" != '{broken}' ]; }}; install() {{ :; }}; curl() {{ return 1; }}; install_module",
            string.Empty);

        Assert.StartsWith(
            "apt-get install -y --no-install-recommends dkms gcc make linux-headers-6.12.113+deb13-amd64\n" +
            "apt-get install -y --no-install-recommends linux-headers-amd64\n",
            said,
            StringComparison.Ordinal);
        if (complaint.Length == 0)
        {
            Assert.DoesNotContain("did not install", said, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains(complaint, said, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AKernelWithoutAMetapackageOfHeaders_IsToldSo()
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, _, said) = await MenuQuestionsTests.RunAsync(
            Host("6.6.0", "linux-image-6.6.0", "linux-headers-6.6.0") + Apt + "install() { :; }; curl() { return 1; }; install_module",
            string.Empty);

        Assert.StartsWith(
            "apt-get install -y --no-install-recommends dkms gcc make linux-headers-6.6.0\napt has no metapackage of the kernel headers",
            said,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModuleBuiltForAnOlderKernelOnly_IsNamedWithTheFix_AndBuiltForTheRunningOneWhenAsked()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("6.12.107+deb13-amd64/updates/dkms/amneziawg.ko.xz", string.Empty);
        folder.Write("6.12.113+deb13-amd64/kernel/net/ipv4/tcp_bbr.ko.xz", string.Empty);

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            Host("6.12.113+deb13-amd64", "linux-image-amd64", "linux-headers-amd64") + Apt +
            $"modules_dir={folder.Path}; dkms() {{ echo \"dkms $*\"; }}; modprobe() {{ echo \"modprobe $*\"; }}; rebuild_module",
            "y\n");

        Assert.Equal(
            "DKMS built the module for 6.12.107+deb13-amd64 and not for the running kernel 6.12.113+deb13-amd64: " +
            "apt-get install linux-headers-6.12.113+deb13-amd64 linux-headers-amd64 && dkms autoinstall -k 6.12.113+deb13-amd64 builds it\n" +
            "apt-get install -y --no-install-recommends linux-headers-6.12.113+deb13-amd64 linux-headers-amd64\n",
            said);
        Assert.Equal("dkms autoinstall -k 6.12.113+deb13-amd64\nmodprobe amneziawg\namneziawg module loaded\n", told);
    }

    [Fact]
    public async Task AModuleBuiltForNoOtherKernel_IsLeftToTheLinesBefore()
    {
        Skip.IfNot(OperatingSystem.IsLinux());
        using var folder = new MenuDockerTests.Folder();
        folder.Write("6.12.113+deb13-amd64/updates/dkms/amneziawg.ko.xz", string.Empty);

        var (_, told, said) = await MenuQuestionsTests.RunAsync(
            Host("6.12.113+deb13-amd64", "linux-image-amd64", "linux-headers-amd64") + Apt + $"modules_dir={folder.Path}; rebuild_module",
            "y\n");

        Assert.Empty(told);
        Assert.Empty(said);
    }

    [Theory]
    [InlineData("6.12.113+deb13-amd64", "linux-image-amd64", "linux-headers-amd64", "linux-headers-amd64")]
    [InlineData("6.8.0-45-generic", "linux-image-generic-hwe-22.04 linux-headers-generic", "linux-headers-generic-hwe-22.04 linux-headers-generic",
        "linux-headers-generic-hwe-22.04")]
    public async Task AHostWithoutTheHeadersToFollow_IsToldSo_AndGetsThemWhenAsked(string kernel, string on, string apt, string lacks)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, said) = await MenuQuestionsTests.RunAsync(Host(kernel, on, apt) + Apt + "dkms() { :; }; follow_headers", "y\n");

        Assert.Equal(
            $"{lacks} not on: after a kernel upgrade DKMS has no headers to build the module with\napt-get install -y --no-install-recommends {lacks}\n",
            said);
        Assert.Equal($"{lacks} on\n", told);
    }

    [Theory]
    [InlineData("6.8.0-142-generic", "linux-image-virtual linux-image-generic linux-headers-virtual linux-headers-generic",
        "linux-headers-virtual linux-headers-generic")]
    [InlineData("6.12.113+deb13-amd64", "linux-image-amd64 linux-headers-amd64", "linux-headers-amd64")]
    public async Task AHostWithTheHeadersToFollow_HearsNothing(string kernel, string on, string apt)
    {
        Skip.IfNot(OperatingSystem.IsLinux());

        var (_, told, said) = await MenuQuestionsTests.RunAsync(Host(kernel, on, apt) + Apt + "dkms() { :; }; follow_headers", "y\n");

        Assert.Empty(told);
        Assert.Empty(said);
    }

    // The host stood in for: the running kernel, the packages dpkg has on and the ones apt carries.
    private static string Host(string kernel, string on, string apt)
        => $"uname() {{ printf '%s\\n' '{kernel}'; }}; on='{on}'; apt='{apt}'; " +
            "dpkg-query() { local p; for p in $on; do case $p in ${@: -1}) printf 'ii  %s\\n' \"$p\" ;; esac; done; }; " +
            "apt_has() { case \" $apt \" in *\" $1 \"*) return 0 ;; esac; return 1; }; ";
}
