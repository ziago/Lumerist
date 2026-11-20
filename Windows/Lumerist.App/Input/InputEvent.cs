namespace Lumerist.App.Input;

/// <summary>
/// A keyboard or mouse event as the hook saw it. The gesture engine keeps key
/// downs it holds back and hands them back to be replayed.
/// </summary>
/// <param name="ScanCode">Chord scan code (Set 1, plus 0xE000 for extended keys).</param>
/// <param name="ModifierMask">
/// The modifier keys held when the event happened (<see cref="Lumerist.Core.Input.ModifierPlan"/>):
/// a replay restores them, so a key held back before Shift went down is not sent with Shift.
/// </param>
internal readonly record struct InputEvent(bool IsMouse, int VirtualKey, int ScanCode, bool IsUp, int ModifierMask, uint MouseMessage, uint MouseData)
{
    public static InputEvent Key(int virtualKey, int scanCode, bool isUp, int modifierMask) =>
        new(false, virtualKey, scanCode, isUp, modifierMask, 0, 0);

    public static InputEvent Mouse(uint message, uint mouseData) => new(true, 0, 0, false, 0, message, mouseData);
}
