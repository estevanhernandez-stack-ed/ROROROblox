using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Transport;

namespace ROROROblox.App.Transport;

/// <summary>
/// Export accounts to a passphrase-encrypted <c>.rororo-accounts</c> bundle (v1.6.0 — spec §1
/// "Export flow"). An account checklist (all exportable accounts checked by default), a passphrase +
/// confirm field gated by <see cref="PassphraseStrength"/> (≥12 chars + a live meter), then a
/// save-file dialog and the write.
///
/// SECURITY-SENSITIVE. The decrypted export records and the passphrase are used strictly inside
/// <see cref="OnExportClick"/>'s method scope and never stored on any field/property — they go out of
/// scope the moment the write returns. No passphrase or cookie is ever logged.
/// </summary>
internal partial class ExportAccountsWindow : Window
{
    private readonly IAccountStore _accountStore;
    private readonly IAccountTransport _transport;
    private readonly ObservableCollection<ExportRow> _rows = [];

    /// <summary>
    /// One checklist entry. <see cref="CanExport"/> is false when the account has no Roblox userId
    /// yet — the merge key requires one, so it can't travel. Those rows render disabled + greyed with
    /// the inline note (mirrors <see cref="AccountExportResult.SkippedNoUserId"/>).
    /// </summary>
    private sealed class ExportRow : INotifyPropertyChanged
    {
        private bool _isChecked;

        public ExportRow(Guid id, string renderName, bool canExport)
        {
            Id = id;
            RenderName = renderName;
            CanExport = canExport;
            _isChecked = canExport; // default-checked, but only the exportable ones
        }

        public Guid Id { get; }
        public string RenderName { get; }
        public bool CanExport { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public Visibility NoUserIdNoteVisibility =>
            CanExport ? Visibility.Collapsed : Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public ExportAccountsWindow(
        IAccountStore accountStore,
        IAccountTransport transport,
        IReadOnlyList<AccountSummary> accounts)
    {
        _accountStore = accountStore;
        _transport = transport;
        InitializeComponent();

        // Pre-filter exportability by the known per-account userId — same gate ExportAccountsAsync
        // applies (SkippedNoUserId). Accounts without a resolved userId are shown disabled/greyed.
        foreach (var a in accounts)
        {
            bool canExport = a.RobloxUserId is > 0;
            var row = new ExportRow(a.Id, a.RenderName, canExport);
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ExportRow.IsChecked))
                {
                    RecomputeGate();
                }
            };
            _rows.Add(row);
        }

        AccountsList.ItemsSource = _rows;
        Loaded += (_, _) =>
        {
            PassphraseBox.Focus();
            RecomputeGate();
        };
    }

    private void OnPassphraseChanged(object sender, RoutedEventArgs e)
    {
        // PasswordBox doesn't expose Password through binding (by design — it never lands in a
        // bindable property). Read it here, drive the meter, then let it go out of scope.
        var (score, label) = PassphraseStrength.Evaluate(PassphraseBox.Password);
        StrengthBar.Value = score;
        StrengthLabel.Text = label.ToUpperInvariant();
        RecomputeGate();
    }

    /// <summary>
    /// The Export button's enable gate: at least one exportable account checked, the passphrase
    /// clears the ≥12 floor, and the two fields match. Validation text explains what's missing.
    /// </summary>
    private void RecomputeGate()
    {
        int checkedExportable = _rows.Count(r => r.CanExport && r.IsChecked);
        string pass = PassphraseBox.Password;
        string confirm = ConfirmBox.Password;

        bool hasAccounts = checkedExportable > 0;
        bool floorOk = PassphraseStrength.IsAcceptable(pass);
        bool match = !string.IsNullOrEmpty(pass) && pass == confirm;

        string? message = null;
        if (!hasAccounts)
        {
            message = "Pick at least one account to export.";
        }
        else if (!floorOk)
        {
            message = $"Passphrase must be at least {PassphraseStrength.MinimumLength} characters.";
        }
        else if (!match)
        {
            message = "The two passphrases don't match.";
        }

        ValidationText.Text = message ?? string.Empty;
        ExportButton.IsEnabled = hasAccounts && floorOk && match;
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        // Re-check the gate at click time (defense in depth — the button shouldn't be clickable
        // otherwise, but never trust the UI for a security-relevant action).
        var checkedIds = _rows.Where(r => r.CanExport && r.IsChecked).Select(r => r.Id).ToList();
        if (checkedIds.Count == 0 ||
            !PassphraseStrength.IsAcceptable(PassphraseBox.Password) ||
            PassphraseBox.Password != ConfirmBox.Password)
        {
            RecomputeGate();
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save account bundle",
            Filter = "RoRoRo account bundle (*.rororo-accounts)|*.rororo-accounts",
            DefaultExt = ".rororo-accounts",
            AddExtension = true,
            FileName = $"rororo-accounts-{DateTime.Now:yyyyMMdd}.rororo-accounts",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ExportButton.IsEnabled = false;
        try
        {
            // SECURITY: everything below stays in method scope. The AccountExportResult, its Records
            // (plaintext cookies), and the passphrase are never assigned to a field/property. After
            // the write returns they're out of scope; no logging of the passphrase or any cookie.
            var result = await _accountStore.ExportAccountsAsync(checkedIds);
            if (result.Records.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "None of the selected accounts could be exported. Launch them once so they get a Roblox ID, then try again.",
                    "Nothing to export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            byte[] bundle = _transport.Export(result.Records, PassphraseBox.Password);
            await File.WriteAllBytesAsync(dialog.FileName, bundle);

            MessageBox.Show(
                this,
                "Saved. This file is your account logins — anyone with the file AND the passphrase can sign in as you. Keep the passphrase safe and don't post the file publicly.",
                "Accounts exported",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            // Generic surface — never echo the passphrase or cookie. ex.Message here is an I/O or
            // crypto-input error, not an oracle (the transport's own errors are import-side).
            MessageBox.Show(
                this,
                $"Couldn't save the bundle: {ex.Message}",
                "Export failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ExportButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
