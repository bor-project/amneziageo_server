// Written by tools/generate-uapi.py from the kernel module header. Do not edit.
// Source: amneziawg-linux-kernel-module 4569c4c, src/uapi/wireguard.h

namespace AmneziaGeo.Server.Awg.Uapi;

/// <summary>
/// Names and sizes the kernel module publishes.
/// </summary>
public static class WgUapi
{
    public const string FamilyName = "amneziawg";

    public const byte FamilyVersion = 3;

    public const int KeyLength = 32;
}

/// <summary>
/// The wg_cmd values of the module header.
/// </summary>
public enum WgCmd : byte
{
    GetDevice = 0,
    SetDevice = 1,
    UnknownPeer = 2,
}

/// <summary>
/// The wgdevice_flag values of the module header.
/// </summary>
[Flags]
public enum WgDeviceFlag : uint
{
    ReplacePeers = 1 << 0,
}

/// <summary>
/// The wgdevice_attribute values of the module header.
/// </summary>
public enum WgDeviceAttribute : ushort
{
    Unspec = 0,
    Ifindex = 1,
    Ifname = 2,
    PrivateKey = 3,
    PublicKey = 4,
    Flags = 5,
    ListenPort = 6,
    Fwmark = 7,
    Peers = 8,
    Jc = 9,
    Jmin = 10,
    Jmax = 11,
    S1 = 12,
    S2 = 13,
    H1 = 14,
    H2 = 15,
    H3 = 16,
    H4 = 17,
    Peer = 18,
    S3 = 19,
    S4 = 20,
    I1 = 21,
    I2 = 22,
    I3 = 23,
    I4 = 24,
    I5 = 25,
    HeaderProtectionKey = 26,
    ContentPaddingAddition = 27,
    RekeyAfterTime = 28,
    RekeyTimeout = 29,
    RejectAfterTime = 30,
    KeepaliveTimeout = 31,
    MaxHandshakeAttempts = 32,
    RandomTrailers = 33,
    DisableCookies = 34,
}

/// <summary>
/// The wgpeer_flag values of the module header.
/// </summary>
[Flags]
public enum WgPeerFlag : uint
{
    RemoveMe = 1 << 0,
    ReplaceAllowedips = 1 << 1,
    UpdateOnly = 1 << 2,
    HasAdvancedSecurity = 1 << 3,
}

/// <summary>
/// The wgpeer_attribute values of the module header.
/// </summary>
public enum WgPeerAttribute : ushort
{
    Unspec = 0,
    PublicKey = 1,
    PresharedKey = 2,
    Flags = 3,
    Endpoint = 4,
    PersistentKeepaliveInterval = 5,
    LastHandshakeTime = 6,
    RxBytes = 7,
    TxBytes = 8,
    Allowedips = 9,
    ProtocolVersion = 10,
    AdvancedSecurity = 11,
}

/// <summary>
/// The wgallowedip_flag values of the module header.
/// </summary>
[Flags]
public enum WgAllowedIpFlag : uint
{
    RemoveMe = 1 << 0,
}

/// <summary>
/// The wgallowedip_attribute values of the module header.
/// </summary>
public enum WgAllowedIpAttribute : ushort
{
    Unspec = 0,
    Family = 1,
    IpAddr = 2,
    CidrMask = 3,
    Flags = 4,
}
