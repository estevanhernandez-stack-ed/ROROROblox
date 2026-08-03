using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class JoinSecretCodecTests
{
    [Fact]
    public void Encode_GameJob_RoundTripsToTheSameServer()
    {
        var target = new LaunchTarget.GameJob(140403681187145, "fcbe3a36-d655-41da-ba8a-8280f5709568");

        var secret = JoinSecretCodec.Encode(target);
        Assert.NotNull(secret);
        Assert.True(JoinSecretCodec.TryDecode(secret, out var decoded));

        var job = Assert.IsType<LaunchTarget.GameJob>(decoded);
        Assert.Equal(140403681187145, job.PlaceId);
        Assert.Equal("fcbe3a36-d655-41da-ba8a-8280f5709568", job.JobId);
    }

    [Fact]
    public void Encode_PrivateServer_PreservesTheCodeKind()
    {
        // linkCode and accessCode are NOT interchangeable — sending one in the other's slot is
        // permission-denied even on a server you own.
        var target = new LaunchTarget.PrivateServer(8737899170, "SHARE_TOKEN", PrivateServerCodeKind.LinkCode);

        Assert.True(JoinSecretCodec.TryDecode(JoinSecretCodec.Encode(target)!, out var decoded));

        var ps = Assert.IsType<LaunchTarget.PrivateServer>(decoded);
        Assert.Equal(PrivateServerCodeKind.LinkCode, ps.Kind);
        Assert.Equal("SHARE_TOKEN", ps.Code);
    }

    [Fact]
    public void Encode_StaysUnderLacheesSecretCap()
    {
        // Lachee silently rejects SetPresence when a secret exceeds 128 chars — the May branch
        // lost a session to this. A realistic worst case is a long private-server link code.
        var target = new LaunchTarget.PrivateServer(
            long.MaxValue, new string('A', 64), PrivateServerCodeKind.AccessCode);

        var secret = JoinSecretCodec.Encode(target);

        Assert.NotNull(secret);
        Assert.True(secret.Length <= JoinSecretCodec.MaxLength, $"secret was {secret.Length} chars");
    }

    [Fact]
    public void Encode_TargetsWithNoJoinableServer_ReturnNull()
    {
        Assert.Null(JoinSecretCodec.Encode(new LaunchTarget.Home()));
        Assert.Null(JoinSecretCodec.Encode(new LaunchTarget.DefaultGame()));
        Assert.Null(JoinSecretCodec.Encode(new LaunchTarget.Place(8737899170)));  // "any server" is not a server
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("g|notanumber|job")]
    [InlineData("p|123")]              // truncated
    public void TryDecode_Rubbish_ReturnsFalseAndDoesNotThrow(string input)
    {
        Assert.False(JoinSecretCodec.TryDecode(input, out _));
    }
}
