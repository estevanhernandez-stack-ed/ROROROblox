using ROROROblox.App.Logging;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>F-001 — the whole point of the seam is that this is assertable.</summary>
public class ShellOpenerTests
{
    private sealed class RecordingShellOpener : IShellOpener
    {
        public List<string> Opened { get; } = new();
        public void Open(string path) => Opened.Add(path);
    }

    [Fact]
    public void OpenLogFolderCommand_OpensTheLogDirectory()
    {
        var opener = new RecordingShellOpener();
        var (vm, _, _, path) = MainViewModelTests.Build(shellOpener: opener);
        try
        {
            vm.OpenLogFolderCommand.Execute(null);

            Assert.Equal(new[] { AppLogging.LogDirectory }, opener.Opened);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShowWelcomeTourCommand_ShowsTheTour()
    {
        var shown = 0;
        var (vm, _, _, path) = MainViewModelTests.Build();
        try
        {
            vm.ShowWelcomeTour = () => shown++;

            vm.ShowWelcomeTourCommand.Execute(null);

            Assert.Equal(1, shown);
        }
        finally { File.Delete(path); }
    }
}
