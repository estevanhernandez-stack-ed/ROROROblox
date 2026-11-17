using System.IO;
using System.Windows;

namespace ROROROblox.App.Modals;

internal partial class DpapiCorruptWindow : Window
{
    public DpapiCorruptWindow()
    {
        InitializeComponent();
    }

    private void OnStartFreshClick(object sender, RoutedEventArgs e)
    {
        TryRenameCorruptFile();
        DialogResult = true;
        Close();
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>The accounts file the window sets aside when the user starts fresh.</summary>
    internal static string CorruptAccountsPath => ROROROblox.Core.AppDataRoot.Combine("accounts.dat");

    private static void TryRenameCorruptFile()
    {
        try
        {
            var path = CorruptAccountsPath;
            if (!File.Exists(path))
            {
                return;
            }
            var renamed = path + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
            File.Move(path, renamed, overwrite: false);
        }
        catch
        {
            // If rename fails the next AddAsync will overwrite via atomic-write — same outcome.
        }
    }
}
