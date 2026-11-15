namespace AmneziaGeo.Server.Routing.Template;

/// <summary>
/// Describes a bundled routing list.
/// </summary>
public sealed record RoutingBundleList(string Key, string Name, IReadOnlyList<string> Targets, IReadOnlyList<string> Applications);

/// <summary>
/// Supplies the bundled routing lists.
/// </summary>
public static class RoutingBundle
{
    public static IReadOnlyList<RoutingBundleList> Lists { get; } =
    [
        new("youtube", "YouTube",
            ["domain:youtube.com", "domain:youtu.be", "domain:googlevideo.com", "domain:ytimg.com", "domain:youtube-nocookie.com", "domain:ggpht.com"],
            ["app:pkg=com.google.android.youtube", "app:pkg=com.google.android.youtube.tv"]),
        new("discord", "Discord",
            ["domain:discord.com", "domain:discord.gg", "domain:discordapp.com", "domain:discordapp.net", "domain:discord.media", "domain:discordcdn.com"],
            ["app:pkg=com.discord", "app:name=Discord.exe"]),
        new("instagram", "Instagram",
            ["domain:instagram.com", "domain:cdninstagram.com"],
            ["app:pkg=com.instagram.android"]),
        new("facebook", "Facebook",
            ["domain:facebook.com", "domain:facebook.net", "domain:fbcdn.net", "domain:fb.com", "domain:messenger.com"],
            ["app:pkg=com.facebook.katana", "app:pkg=com.facebook.orca"]),
        new("whatsapp", "WhatsApp",
            ["domain:whatsapp.com", "domain:whatsapp.net", "domain:wa.me"],
            ["app:pkg=com.whatsapp", "app:pkg=com.whatsapp.w4b"]),
        new("telegram", "Telegram",
            ["domain:telegram.org", "domain:t.me", "domain:telegram.me", "domain:telegra.ph", "domain:telesco.pe"],
            ["app:pkg=org.telegram.messenger", "app:name=Telegram.exe"]),
        new("twitter", "X (Twitter)",
            ["domain:x.com", "domain:twitter.com", "domain:twimg.com", "domain:t.co"],
            ["app:pkg=com.twitter.android"]),
    ];
}
