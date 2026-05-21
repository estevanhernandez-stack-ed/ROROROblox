using System.IO;
using System.Windows;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Transport;

namespace ROROROblox.App.Transport;

/// <summary>
/// Import accounts from a passphrase-encrypted <c>.rororo-accounts</c> bundle (v1.6.0 — spec §1
/// "Import flow"). Pick the file, enter the passphrase, decrypt → merge-by-userId → refresh the
/// account list. Merge is non-destructive: accounts already on this PC are kept untouched.
///
/// SECURITY-SENSITIVE. The decrypted records and the passphrase live only inside
/// <see cref="OnImportClick"/>'s scope — never assigned to a field/property, never logged. On a
/// failed decrypt we catch ONLY <see cref="AccountTransportException"/> and show its message verbatim
/// (the deliberately-ambiguous "wrong passphrase or damaged file" string) so we never reveal which
/// failure mode hit.
/// </summary>
internal partial class ImportAccountsWindow : Window
{
    private readonly IAccountStore _accountStore;
    private readonly IAccountTransport _transport;
    private readonly MainViewModel _mainViewModel;

    public ImportAccountsWindow(
        IAccountStore accountStore,
        IAccountTransport transport,
        MainViewModel mainViewModel)
    {
        _accountStore = accountStore;
        _transport = transport;
        _mainViewModel = mainViewModel;
        InitializeComponent();
        Loaded += (_, _) => RecomputeGate();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open account bundle",
            Filter = "RoRoRo account bundle (*.rororo-accounts)|*.rororo-accounts|All files (*.*)|*.*",
            DefaultExt = ".rororo-accounts",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            FilePathBox.Text = dialog.FileName;
            StatusText.Text = string.Empty;
            RecomputeGate();
        }
    }

    private void OnInputChanged(object sender, RoutedEventArgs e) => RecomputeGate();

    private void RecomputeGate()
    {
        bool hasFile = !string.IsNullOrWhiteSpace(FilePathBox.Text);
        bool hasPass = PassphraseBox.Password.Length > 0;
        ImportButton.IsEnabled = hasFile && hasPass;
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var path = FilePathBox.Text;
        if (string.IsNullOrWhiteSpace(path) || PassphraseBox.Password.Length == 0)
        {
            RecomputeGate();
            return;
        }

        ImportButton.IsEnabled = false;
        StatusText.Text = "Opening bundle…";

        try
        {
            // Snapshot the existing local main BEFORE the merge, so if an imported account also had
            // IsMain we can re-assert the local one afterwards (an import must not silently steal main).
            var before = await _accountStore.ListAsync();
            Guid? existingMainId = before.FirstOrDefault(a => a.IsMain)?.Id;

            byte[] bytes = await File.ReadAllBytesAsync(path);

            // SECURITY: records + passphrase stay in this scope. Not stored on a field, not logged.
            IReadOnlyList<AccountExportRecord> records = _transport.Import(bytes, PassphraseBox.Password);
            ImportMergeResult merge = await _accountStore.ImportMergeAsync(records);

            // Re-establish the single-main invariant. The merge is non-destructive, so an imported
            // account carrying IsMain can land alongside the existing local main. If we now have more
            // than one main, keep the EXISTING local main (don't let an import steal it). SetMainAsync
            // clears every other main when it sets one.
            var after = await _accountStore.ListAsync();
            var mains = after.Where(a => a.IsMain).Select(a => a.Id).ToList();
            if (mains.Count > 1)
            {
                // Prefer the pre-existing local main; if there wasn't one (all mains came from the
                // import), keep the first deterministically so we still collapse to a single main.
                Guid keep = existingMainId is Guid g && mains.Contains(g) ? g : mains[0];
                await _accountStore.SetMainAsync(keep);
            }

            // Refresh the account list so the new rows show without an app restart. LoadAsync rebuilds
            // Accounts from the store on the UI thread (this handler runs there).
            await _mainViewModel.LoadAsync();

            // records goes out of scope here — nothing retained.
            MessageBox.Show(
                this,
                $"Imported {merge.Imported} account(s). Skipped {merge.Skipped} already on this PC.",
                "Accounts imported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (AccountTransportException ex)
        {
            // The ONE expected failure: wrong passphrase OR damaged file. Surface .Message verbatim
            // (the generic, ambiguous string) — never distinguish the two modes.
            StatusText.Text = ex.Message;
            ImportButton.IsEnabled = true;
        }
        catch (IOException ex)
        {
            // File couldn't be read (deleted, locked). Distinct from a crypto failure, plainly stated.
            StatusText.Text = $"Couldn't read that file: {ex.Message}";
            ImportButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
