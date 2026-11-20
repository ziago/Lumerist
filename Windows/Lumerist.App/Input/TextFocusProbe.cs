using System.Windows.Automation;
using static Lumerist.App.Interop.Native;

namespace Lumerist.App.Input;

/// <summary>
/// Whether the user is typing in a Lightroom text field (keywords, search,
/// names), so a Dial Key types its letter instead of starting a gesture.
/// </summary>
/// <remarks>
/// Asked from inside the keyboard hook, so it must be quick. First the cheap
/// signs (a caret, a standard edit control), then UI Automation on a pool
/// thread with a 30 ms budget, like the Mac's Accessibility timeout. Which
/// signs Lightroom's own fields give is to be checked on Windows.
/// </remarks>
internal static class TextFocusProbe
{
    public static readonly TimeSpan AutomationBudget = TimeSpan.FromMilliseconds(30);

    private static readonly HashSet<string> EditClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Edit", "RichEdit20A", "RichEdit20W", "RICHEDIT50W", "RichEditD2DPT",
    };

    private static int automationBusy;

    /// <summary>The last answer UI Automation gave, used when the next one is late.</summary>
    private sealed record Answer(nint Focus, bool IsText, long At);

    private static Answer? lastAnswer;

    /// <summary>How long a late question may lean on the previous answer for the same window.</summary>
    private const long AnswerLifetimeMilliseconds = 5_000;

    /// <summary>
    /// Runs the whole UI Automation path once in the background (assemblies,
    /// JIT, the COM proxy), so the first real question fits the budget.
    /// </summary>
    public static void WarmUp() => _ = Task.Run(() =>
    {
        try
        {
            _ = IsTextElement(AutomationElement.FocusedElement);
        }
        catch (Exception)
        {
            // Without UI Automation only the cheap signs are used.
        }
    });

    public static unsafe bool IsTextInputFocused(uint threadId)
    {
        if (threadId == 0) return false;
        var info = new GUITHREADINFO { cbSize = (uint)sizeof(GUITHREADINFO) };
        if (!GetGUIThreadInfo(threadId, ref info)) return false;
        if (info.hwndCaret != 0 || (info.flags & GUI_CARETBLINKING) != 0) return true;
        if (info.hwndFocus != 0 && EditClasses.Contains(ClassName(info.hwndFocus))) return true;
        return AskAutomation(info.hwndFocus);
    }

    /// <summary>
    /// The hook thread waits without pumping messages (it is not STA), so the
    /// hook is never re-entered while UI Automation talks to Lightroom. When
    /// the answer is late (or a late question is still running), the previous
    /// answer for the same window stands in; a late answer still updates it.
    /// </summary>
    private static bool AskAutomation(nint focus)
    {
        if (Interlocked.Exchange(ref automationBusy, 1) == 1) return Previous(focus);
        var question = Task.Run(() =>
        {
            try
            {
                var isText = IsTextElement(AutomationElement.FocusedElement);
                Volatile.Write(ref lastAnswer, new Answer(focus, isText, Environment.TickCount64));
                return isText;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                Volatile.Write(ref automationBusy, 0);
            }
        });
        return question.Wait(AutomationBudget) ? question.Result : Previous(focus);
    }

    private static bool Previous(nint focus) =>
        Volatile.Read(ref lastAnswer) is { } answer
        && answer.Focus == focus
        && Environment.TickCount64 - answer.At < AnswerLifetimeMilliseconds
        && answer.IsText;

    private static bool IsTextElement(AutomationElement? element)
    {
        if (element is null) return false;
        var type = element.Current.ControlType;
        if (type != ControlType.Edit && type != ControlType.Document && type != ControlType.ComboBox) return false;
        // A read-only document or a closed list is not a place to type.
        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern value)
            return !value.Current.IsReadOnly;
        return type != ControlType.ComboBox;
    }
}
