namespace AmneziaGeo.Server.Routing.Names;

/// <summary>
/// The fixed values of the reading of names out of the connections themselves.
/// </summary>
public static class NameDefaults
{
    /// <summary>
    /// The queue the firewall shows the first packets of the connections to the panel in.
    /// </summary>
    public const ushort Queue = 7318;

    /// <summary>
    /// The port the panel takes the TCP connections the firewall hands over on.
    /// </summary>
    public const int Port = 7319;

    /// <summary>
    /// The mark of a packet the panel read and sent back to the firewall.
    /// </summary>
    public const uint Read = 0x0008_0000;

    /// <summary>
    /// The mark of a packet whose TCP connection the firewall resets: a blocking rule took it.
    /// </summary>
    public const uint Reset = 0x0010_0000;

    /// <summary>
    /// The mark of a connection the firewall hands over to the panel.
    /// </summary>
    public const uint Taken = 0x0000_A500;

    /// <summary>
    /// The routing table that delivers the connections handed over to the panel itself.
    /// </summary>
    public const int Table = 42500;

    /// <summary>
    /// The place of the rule that sends the connections handed over into that table.
    /// </summary>
    public const int Priority = 9990;

    /// <summary>
    /// How long an address the panel looked at is left to the rules without another look, in minutes.
    /// </summary>
    public const int SeenMinutes = 5;

    /// <summary>
    /// How long a QUIC connection whose first packets were read stays out of the queue, in seconds.
    /// </summary>
    public const int ReadSeconds = 30;

    /// <summary>
    /// How long a TCP connection waits for what its client says first to be read, in seconds.
    /// </summary>
    public const int WaitSeconds = 10;

    /// <summary>
    /// The most connections a second the firewall hands over to the panel.
    /// </summary>
    public const int MostTaken = 300;

    /// <summary>
    /// The most connections the panel carries at once.
    /// </summary>
    public const int MostCarried = 4096;

    /// <summary>
    /// The bytes of a packet the kernel copies for the panel.
    /// </summary>
    public const int Snap = 0xFFFF;

    /// <summary>
    /// The most packets the queue holds.
    /// </summary>
    public const int Length = 4096;

    /// <summary>
    /// The bytes the socket of the queue holds for packets the panel has not read yet.
    /// </summary>
    public const int Buffer = 4 << 20;

    /// <summary>
    /// The most connections whose first packets are held at once.
    /// </summary>
    public const int MostHeld = 1024;

    /// <summary>
    /// The most packets of one connection held while what it says first is put together.
    /// </summary>
    public const int MostPackets = 8;

    /// <summary>
    /// The most bytes of what a client says first over TCP the panel looks through.
    /// </summary>
    public const int MostSaid = 16384;

    /// <summary>
    /// The most connections of a client to an address a blocking rule cuts while they are counted.
    /// </summary>
    public const int MostAgain = 8;

    /// <summary>
    /// The connections the firewall may hand over without one of them reaching the panel before it stops them.
    /// </summary>
    public const int Unreached = 3;

    /// <summary>
    /// How long one read of the queue waits before it looks at the packets held.
    /// </summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMilliseconds(20);

    /// <summary>
    /// How long the packets of a connection are held for the rest of what it says first.
    /// </summary>
    public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(60);

    /// <summary>
    /// How long the panel waits for the first word of a client before it reaches the far end without a name.
    /// </summary>
    public static readonly TimeSpan Said = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// How long the panel waits for the far end to take a connection.
    /// </summary>
    public static readonly TimeSpan Dial = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long the panel waits after it sent the addresses looked at to the host before it sends the next ones.
    /// </summary>
    public static readonly TimeSpan Noting = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How often the panel looks at its room while it is full or nearly so.
    /// </summary>
    public static readonly TimeSpan Crowding = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How often the panel checks that the connections handed over reach it.
    /// </summary>
    public static readonly TimeSpan Checking = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long the connections of a client to an address a blocking rule cut are counted.
    /// </summary>
    public static readonly TimeSpan Once = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the endpoints the clients arrive on are read from before they are read anew.
    /// </summary>
    public static readonly TimeSpan Renewing = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The ports of TCP the panel reads names on.
    /// </summary>
    public static readonly int[] Ports = [80, 443];

    /// <summary>
    /// The IPv4 ranges the panel takes no connection over to: its own, private, shared, link-local and group ones.
    /// </summary>
    public static readonly string[] Closed4 =
    [
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.168.0.0/16",
        "224.0.0.0/3",
    ];

    /// <summary>
    /// The IPv6 ranges the panel takes no connection over to: its own, private, link-local and group ones.
    /// </summary>
    public static readonly string[] Closed6 = ["::/127", "::ffff:0:0/96", "fc00::/7", "fe80::/10", "ff00::/8"];
}
