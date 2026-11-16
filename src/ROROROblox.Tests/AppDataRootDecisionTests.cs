using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The pure eligibility decision behind <c>RORORO_DATA_DIR</c>: three inputs in (packaged,
/// Velopack-installed layout, the raw variable), a folder or a reason out. No I/O and no
/// environment reads inside the function, so every row is a plain call.
/// </summary>
public class AppDataRootDecisionTests
{
    private static readonly string Absolute = Path.Combine(Path.GetTempPath(), "rororo-decision-test", "data");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \r\n")]
    public void UnsetEmptyOrWhitespace_IsUnset(string? raw)
    {
        var d = AppDataRoot.Decide(isPackaged: false, isVelopackInstalled: false, raw);

        Assert.Equal(DataRootDecisionReason.Unset, d.Reason);
        Assert.Null(d.Folder);
    }

    [Fact]
    public void DevBuild_AbsolutePath_IsUsed_AndNormalised()
    {
        var messy = Path.Combine(Path.GetTempPath(), "rororo-decision-test", "a", "..", "data") + Path.DirectorySeparatorChar;

        var d = AppDataRoot.Decide(false, false, messy);

        Assert.Equal(DataRootDecisionReason.Used, d.Reason);
        Assert.Equal(Absolute, d.Folder);
    }

    [Fact]
    public void DevBuild_PlainAbsolutePath_IsUsedAsIs()
    {
        var d = AppDataRoot.Decide(false, false, Absolute);

        Assert.Equal(DataRootDecisionReason.Used, d.Reason);
        Assert.Equal(Absolute, d.Folder);
    }

    [Fact]
    public void DevBuild_PathWithSurroundingWhitespace_IsTrimmedAndUsed()
    {
        var d = AppDataRoot.Decide(false, false, "  " + Absolute + "\t ");

        Assert.Equal(DataRootDecisionReason.Used, d.Reason);
        Assert.Equal(Absolute, d.Folder);
    }

    [Fact]
    public void Packaged_IgnoresTheVariable_WhateverItHolds()
    {
        var d = AppDataRoot.Decide(isPackaged: true, isVelopackInstalled: false, Absolute);

        Assert.Equal(DataRootDecisionReason.NotADevBuild, d.Reason);
        Assert.Null(d.Folder);
    }

    [Fact]
    public void VelopackInstalled_IgnoresTheVariable_WhateverItHolds()
    {
        var d = AppDataRoot.Decide(isPackaged: false, isVelopackInstalled: true, Absolute);

        Assert.Equal(DataRootDecisionReason.NotADevBuild, d.Reason);
        Assert.Null(d.Folder);
    }

    [Fact]
    public void PackagedAndInstalled_WithARelativeValue_IsStillNotADevBuild()
    {
        // The build check outranks the value check: a shipped build says "not a dev build",
        // never "your path was bad", so nobody chases the wrong cause.
        var d = AppDataRoot.Decide(true, true, @"relative\dir");

        Assert.Equal(DataRootDecisionReason.NotADevBuild, d.Reason);
    }

    [Fact]
    public void PackagedWithNothingSet_IsUnset()
    {
        // Unset wins over the build check: with no variable there is nothing to ignore, so a
        // shipped build logs nothing about it.
        var d = AppDataRoot.Decide(true, false, null);

        Assert.Equal(DataRootDecisionReason.Unset, d.Reason);
    }

    [Theory]
    [InlineData(@"relative\dir")]
    [InlineData(@"..\up")]
    [InlineData(@".\here")]
    [InlineData("justaname")]
    [InlineData(@"\rooted-without-a-drive")]
    [InlineData("C:drive-relative")]
    public void DevBuild_RelativePath_IsNotAnAbsolutePath(string raw)
    {
        var d = AppDataRoot.Decide(false, false, raw);

        Assert.Equal(DataRootDecisionReason.NotAnAbsolutePath, d.Reason);
        Assert.Null(d.Folder);
    }

    [Fact]
    public void DevBuild_ValueWithANulCharacter_IsNotAnAbsolutePath_AndDoesNotThrow()
    {
        var d = AppDataRoot.Decide(false, false, Absolute + "\0x");

        Assert.Equal(DataRootDecisionReason.NotAnAbsolutePath, d.Reason);
    }
}
