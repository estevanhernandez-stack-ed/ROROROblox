using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using ROROROblox.App.Localization;

namespace ROROROblox.Tests;

/// <summary>
/// The live-toggle runtime (localization Phase D): <see cref="TranslationSource"/> resolves strings
/// for the current culture and notifies on change, so a picker toggle re-renders bound UI without a
/// restart. These prove the mechanism end to end against the shipped satellites — the thing v1.26's
/// <c>x:Static</c> could not do.
/// </summary>
[Collection("MutatesUiCulture")]
public class LocalizationRuntimeTests
{
    // A key whose Spanish translation differs from English (used to prove per-culture resolution).
    private const string SampleKey = "AboutPage_KeyboardShortcuts";

    [Fact]
    public void TranslationSource_ResolvesForTheCurrentCulture()
    {
        var original = TranslationSource.Instance.CurrentCulture;
        try
        {
            TranslationSource.Instance.CurrentCulture = CultureInfo.InvariantCulture;
            var en = TranslationSource.Instance[SampleKey];

            TranslationSource.Instance.CurrentCulture = CultureInfo.GetCultureInfo("es");
            var es = TranslationSource.Instance[SampleKey];

            Assert.Equal("Keyboard shortcuts", en);
            Assert.False(string.IsNullOrWhiteSpace(es));
            Assert.NotEqual(en, es); // genuinely switched, not an English fallback
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = original;
        }
    }

    [Fact]
    public void SettingCulture_RaisesIndexerChangeAndCultureChanged()
    {
        var original = TranslationSource.Instance.CurrentCulture;
        try
        {
            TranslationSource.Instance.CurrentCulture = CultureInfo.InvariantCulture;

            var indexerRaised = false;
            var cultureChangedRaised = false;
            PropertyChangedEventHandler pc = (_, e) =>
            {
                if (e.PropertyName == Binding.IndexerName || string.IsNullOrEmpty(e.PropertyName))
                    indexerRaised = true;
            };
            EventHandler cc = (_, _) => cultureChangedRaised = true;
            TranslationSource.Instance.PropertyChanged += pc;
            TranslationSource.Instance.CultureChanged += cc;
            try
            {
                TranslationSource.Instance.CurrentCulture = CultureInfo.GetCultureInfo("de");
            }
            finally
            {
                TranslationSource.Instance.PropertyChanged -= pc;
                TranslationSource.Instance.CultureChanged -= cc;
            }

            Assert.True(indexerRaised, "setting the culture must raise the indexer PropertyChanged so bound UI refreshes");
            Assert.True(cultureChangedRaised, "setting the culture must fire CultureChanged so ViewModels re-raise composed strings");
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = original;
        }
    }

    [Fact]
    public void SettingSameCulture_DoesNotNotify()
    {
        var original = TranslationSource.Instance.CurrentCulture;
        try
        {
            TranslationSource.Instance.CurrentCulture = CultureInfo.GetCultureInfo("fr");
            var notified = false;
            PropertyChangedEventHandler pc = (_, _) => notified = true;
            TranslationSource.Instance.PropertyChanged += pc;
            try
            {
                TranslationSource.Instance.CurrentCulture = CultureInfo.GetCultureInfo("fr"); // same
            }
            finally
            {
                TranslationSource.Instance.PropertyChanged -= pc;
            }
            Assert.False(notified, "re-setting the same culture must not churn bindings");
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = original;
        }
    }

    [Fact]
    public void LocExtension_BindsToTheTranslationSourceIndexer()
    {
        var result = new LocExtension(SampleKey).ProvideValue(new NullServiceProvider());

        // With no IProvideValueTarget in the service provider, Binding.ProvideValue returns the
        // Binding itself — enough to prove the extension wires path + source correctly.
        var binding = Assert.IsType<Binding>(result);
        Assert.Equal($"[{SampleKey}]", binding.Path.Path);
        Assert.Same(TranslationSource.Instance, binding.Source);
        Assert.Equal(BindingMode.OneWay, binding.Mode);
    }

    [Fact]
    public void Loc_Get_MatchesTheTranslationSource()
    {
        var original = TranslationSource.Instance.CurrentCulture;
        try
        {
            TranslationSource.Instance.CurrentCulture = CultureInfo.GetCultureInfo("es");
            Assert.Equal(TranslationSource.Instance[SampleKey], Loc.Get(SampleKey));
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = original;
        }
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
