using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

public sealed class LastExitRecorderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rororo-lastexit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private LastExitRecorder New() => new(_dir);

    private string FilePath => Path.Combine(_dir, LastExitRecorder.FileName);

    [Fact]
    public void FirstRun_ReportsNone_WithNoTimestamp()
    {
        var r = New();
        r.Begin();

        Assert.Equal(LastExitRecorder.None, r.Previous.Reason);
        Assert.Equal(string.Empty, r.Previous.Detail);
        Assert.Null(r.Previous.At);
    }

    [Fact]
    public void AfterCleanExit_NextSessionReportsClean_WithTimestamp()
    {
        var first = New();
        first.Begin();
        first.RecordClean();

        var next = New();
        next.Begin();

        Assert.Equal(LastExitRecorder.Clean, next.Previous.Reason);
        Assert.NotNull(next.Previous.At);
    }

    [Fact]
    public void AfterSessionEnding_NextSessionReportsSessionEnding_WithReason()
    {
        var first = New();
        first.Begin();
        first.RecordSessionEnding("Logoff");

        var next = New();
        next.Begin();

        Assert.Equal(LastExitRecorder.SessionEnding, next.Previous.Reason);
        Assert.Equal("Logoff", next.Previous.Detail);
    }

    [Fact]
    public void AfterCrash_NextSessionReportsCrash_WithTypeHResultAndMessage()
    {
        var first = New();
        first.Begin();
        first.RecordCrash(new System.Runtime.InteropServices.COMException("composition flipped", unchecked((int)0x80263001)));

        var next = New();
        next.Begin();

        Assert.Equal(LastExitRecorder.Crash, next.Previous.Reason);
        Assert.Contains("System.Runtime.InteropServices.COMException", next.Previous.Detail);
        Assert.Contains("0x80263001", next.Previous.Detail);
        Assert.Contains("composition flipped", next.Previous.Detail);
    }

    [Fact]
    public void CrashDetail_CarriesNoInnerExceptionOrStack()
    {
        var inner = new InvalidOperationException("inner-secret-.ROBLOSECURITY=abc");
        Exception ex;
        try { throw new ApplicationException("outer message", inner); }
        catch (Exception caught) { ex = caught; }

        var first = New();
        first.Begin();
        first.RecordCrash(ex);

        var next = New();
        next.Begin();

        Assert.DoesNotContain("inner-secret", next.Previous.Detail);
        Assert.DoesNotContain("InvalidOperationException", next.Previous.Detail);
        Assert.DoesNotContain(" at ", next.Previous.Detail);
        Assert.DoesNotContain("inner-secret", File.ReadAllText(FilePath));
    }

    [Fact]
    public void StillRunning_NextSessionReportsUnknown()
    {
        var first = New();
        first.Begin();
        // killed: nothing recorded

        var next = New();
        next.Begin();

        Assert.Equal(LastExitRecorder.Unknown, next.Previous.Reason);
        Assert.NotNull(next.Previous.At);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("{\"state\":\"banana\",\"detail\":\"\",\"atUnixMs\":1}")]
    public void CorruptOrUnrecognisedFile_ReportsUnknown(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, content);

        var r = New();
        r.Begin();

        Assert.Equal(LastExitRecorder.Unknown, r.Previous.Reason);
    }

    [Fact]
    public void CleanAfterCrash_DoesNotOverwrite()
    {
        var first = New();
        first.Begin();
        first.RecordCrash(new InvalidOperationException("boom"));
        first.RecordClean();

        var next = New();
        next.Begin();

        Assert.Equal(LastExitRecorder.Crash, next.Previous.Reason);
    }

    [Fact]
    public void CleanAfterSessionEnding_DoesNotOverwrite()
    {
        var first = New();
        first.Begin();
        first.RecordSessionEnding("Shutdown");
        first.RecordClean();

        var next = New();
        next.Begin();

        Assert.Equal(LastExitRecorder.SessionEnding, next.Previous.Reason);
        Assert.Equal("Shutdown", next.Previous.Detail);
    }

    [Fact]
    public void CrashAfterSessionEnding_StillEndsAsCrash()
    {
        var first = New();
        first.Begin();
        first.RecordSessionEnding("Logoff");
        first.RecordCrash(new InvalidOperationException("boom"));

        var next = New();
        next.Begin();

        Assert.Equal(LastExitRecorder.Crash, next.Previous.Reason);
    }

    [Fact]
    public void SessionEndingAfterCrash_DoesNotOverwrite()
    {
        var first = New();
        first.Begin();
        first.RecordCrash(new InvalidOperationException("boom"));
        first.RecordSessionEnding("Logoff");

        var next = New();
        next.Begin();

        Assert.Equal(LastExitRecorder.Crash, next.Previous.Reason);
    }

    [Fact]
    public void RecordsBeforeBegin_AreNoOps()
    {
        var first = New();
        first.Begin();
        first.RecordClean();

        var secondInstance = New(); // never begun: exits early
        secondInstance.RecordCrash(new InvalidOperationException("boom"));
        secondInstance.RecordClean();

        var next = New();
        next.Begin();
        Assert.Equal(LastExitRecorder.Clean, next.Previous.Reason);
    }

    [Fact]
    public void Begin_RunsOncePerProcess()
    {
        var r = New();
        r.Begin();
        r.RecordClean();
        r.Begin(); // must not re-read its own clean record or reset to running

        Assert.Equal(LastExitRecorder.None, r.Previous.Reason);
        var next = New();
        next.Begin();
        Assert.Equal(LastExitRecorder.Clean, next.Previous.Reason);
    }

    [Fact]
    public void WriteFailure_NeverThrows()
    {
        Directory.CreateDirectory(_dir);
        // A directory squatting on the file name makes every write fail.
        Directory.CreateDirectory(FilePath);

        var r = New();
        r.Begin();
        r.RecordSessionEnding("Logoff");
        r.RecordCrash(new InvalidOperationException("boom"));
        r.RecordClean();
    }
}
