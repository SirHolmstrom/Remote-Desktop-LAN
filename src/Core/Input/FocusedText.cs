using System.Runtime.Versioning;
using System.Windows.Automation;

namespace Core.Input;

/// <summary>
/// Reads the text of the currently focused control via Windows UI Automation, so the
/// phone keyboard's echo can reflect the *actual* field — including text typed on the
/// PC, autocompleted, or pasted. Returns null when nothing is focused or the control
/// doesn't expose its text (some custom-drawn apps don't).
/// </summary>
[SupportedOSPlatform("windows")]
public static class FocusedText
{
    public static string? TryRead()
    {
        string? result = null;

        // UI Automation client calls want an STA thread; running it off the input loop
        // (with a join timeout) means a hung app can never stall streaming.
        var worker = new Thread(() =>
        {
            try
            {
                var element = AutomationElement.FocusedElement;
                if (element is null) return;

                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
                    result = ((ValuePattern)valuePattern).Current.Value;
                else if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern))
                    result = ((TextPattern)textPattern).DocumentRange.GetText(20000);
            }
            catch { /* not readable — leave null, echo falls back to the tap-mirror */ }
        });

        worker.SetApartmentState(ApartmentState.STA);
        worker.IsBackground = true;
        worker.Start();
        return worker.Join(700) ? result : null;
    }
}
