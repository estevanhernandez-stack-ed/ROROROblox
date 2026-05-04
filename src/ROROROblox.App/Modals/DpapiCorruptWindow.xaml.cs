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

    private static void TryRenameCorruptFile()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ROROROblox", "accounts.dat");
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
