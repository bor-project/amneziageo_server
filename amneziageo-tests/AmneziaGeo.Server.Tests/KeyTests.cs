using AmneziaGeo.Server.Core.Crypto;

namespace AmneziaGeo.Server.Tests;

public sealed class KeyTests
{
    private const string AlicePrivate = "77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a";

    private const string AlicePublic = "8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a";

    private const string BobPrivate = "5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb";

    private const string BobPublic = "de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f";

    private const string Shared = "4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742";

    [Fact]
    public void PublicKeyFollowsThePrivateOne()
    {
        Assert.Equal(AlicePublic, Hex(Curve25519.PublicOf(Convert.FromHexString(AlicePrivate))));
        Assert.Equal(BobPublic, Hex(Curve25519.PublicOf(Convert.FromHexString(BobPrivate))));
    }

    [Fact]
    public void BothSidesReachTheSameSecret()
    {
        var mine = Curve25519.Product(Convert.FromHexString(AlicePrivate), Convert.FromHexString(BobPublic));
        var theirs = Curve25519.Product(Convert.FromHexString(BobPrivate), Convert.FromHexString(AlicePublic));

        Assert.Equal(Shared, Hex(mine));
        Assert.Equal(Shared, Hex(theirs));
    }

    [Fact]
    public void ScalarMultiplicationMatchesTheStandard()
    {
        var scalar = Convert.FromHexString("a546e36bf0527c9d3b16154b82465edd62144c0ac1fc5a18506a2244ba449ac4");
        var point = Convert.FromHexString("e6db6867583030db3594c1a424b15f7c726624ec26b3353b10a903a6d0ab1c4c");

        Assert.Equal("c3da55379de9c6908e94ea4df28d084f32eccf03491c71f754b4075577a28552", Hex(Curve25519.Product(scalar, point)));
    }

    [Fact]
    public void MadePairHoldsTogether()
    {
        var pair = Curve25519.Create();

        Assert.True(Curve25519.IsKey(pair.PrivateKey));
        Assert.True(Curve25519.IsKey(pair.PublicKey));
        Assert.Equal(pair.PublicKey, Curve25519.PublicOf(pair.PrivateKey));
        Assert.NotEqual(pair.PrivateKey, Curve25519.Create().PrivateKey);
    }

    [Fact]
    public void TextThatIsNotAKeyIsRefused()
    {
        Assert.False(Curve25519.IsKey(null));
        Assert.False(Curve25519.IsKey(string.Empty));
        Assert.False(Curve25519.IsKey("not a key"));
        Assert.False(Curve25519.IsKey(Convert.ToBase64String(new byte[16])));
        Assert.Throws<FormatException>(() => Curve25519.Bytes("not a key"));
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
