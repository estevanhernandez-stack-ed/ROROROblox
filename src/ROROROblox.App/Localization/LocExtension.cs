using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace ROROROblox.App.Localization;

/// <summary>
/// XAML markup extension for live-toggle localization (Phase D): <c>Text="{loc:Loc SomeKey}"</c>
/// binds the target to <see cref="TranslationSource"/>'s indexer, so it re-renders when the culture
/// changes. Replaces <c>{x:Static loc:Strings.SomeKey}</c> (load-time static, can't react). The key
/// is a string, so a resx⇄key parity fence — not the compile-time accessor — guards key existence.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    /// <summary>The resx key to resolve.</summary>
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // A Binding is illegal in a Setter.Value (WPF throws at load). For those — conditional
        // style/trigger copy — hand back the resolved string instead: correct at load under the
        // current culture, just not live-toggled (a small, bounded set). Everything else gets the
        // live binding.
        if (serviceProvider?.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target
            && target.TargetObject is Setter)
        {
            return Loc.Get(Key);
        }

        var binding = new Binding($"[{Key}]")
        {
            Source = TranslationSource.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
