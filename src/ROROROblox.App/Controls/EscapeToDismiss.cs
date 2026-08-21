using System;
using System.Windows;
using System.Windows.Input;

namespace ROROROblox.App.Controls;

/// <summary>
/// Makes Esc dismiss a window that has no cancel button to hang <c>IsCancel</c> on (F-054).
/// <para>
/// WPF gives Esc no meaning by itself: it closes a dialog only by "clicking" a button marked
/// <c>IsCancel="True"</c>. Twenty-two of RoRoRo's windows have such a button and dismiss correctly.
/// Four did not, and the most important of them is <c>CookieCaptureWindow</c> — the window every
/// first-run user is guaranteed to meet, which has no buttons at all because it is a browser frame.
/// </para>
/// <para>
/// Its own code already expected this: <c>"Closed before completion (X / ESC). A clean close is a
/// deliberate Cancelled"</c>. The verdict was written for a gesture nobody had wired.
/// </para>
/// <para>
/// Deliberately NOT used where a cancel button exists. There <c>IsCancel="True"</c> is the
/// idiomatic answer and already the house convention, and a second mechanism doing the same job
/// would be one more thing to keep in step.
/// </para>
/// </summary>
internal static class EscapeToDismiss
{
    /// <param name="window">The window to wire.</param>
    /// <param name="onEscape">
    /// What Esc should mean. Defaults to <see cref="Window.Close"/>. Pass an explicit action when
    /// closing alone would not say the right thing — a dialog whose result carries a decision needs
    /// Esc to land on the SAFE decision, never the destructive one.
    /// </param>
    public static void Wire(Window window, Action? onEscape = null)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            // Handled, so the key does not continue on to a focused child that might treat Escape
            // as its own — the capture window hosts a WebView2, which is exactly such a child.
            e.Handled = true;
            (onEscape ?? window.Close)();
        };
    }
}
