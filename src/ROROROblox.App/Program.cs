using System;
using Velopack;

namespace ROROROblox.App;

// Velopack requires VelopackApp.Build().Run() as the FIRST line of Main so install /
// uninstall / restart-after-update hooks fire before WPF spins up. The auto-generated
// Main from App.xaml is replaced via <StartupObject> in the csproj.
public static class Program
{
    /// <summary>
    /// Captured at process entry so App.OnStartup can route a Discord-Join URI dispatch.
    /// Discord's URI-scheme dispatch (registered via Lachee.RegisterUriScheme) launches our
    /// exe with an arg shaped like <c>discord-{appId}://join?secret={joinSecret}</c>. Without
    /// parsing this, the join click is silently dropped (single-instance guard kills the
    /// second exe before anything reads the args).
    /// </summary>
    public static string? DiscordJoinUriArg { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().SetArgs(args).Run();

        DiscordJoinUriArg = ExtractDiscordJoinUri(args);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static string? ExtractDiscordJoinUri(string[] args)
    {
        foreach (var arg in args)
        {
            if (string.IsNullOrEmpty(arg)) continue;
            if (arg.StartsWith("discord-", StringComparison.OrdinalIgnoreCase) &&
                arg.Contains("://", StringComparison.Ordinal))
            {
                return arg;
            }
        }
        return null;
    }
}
