using System.Runtime.InteropServices;

namespace DeskNote.App.Services;

/// <summary>
/// Asks Windows for a one-millisecond clock tick for as long as something needs it.
/// </summary>
/// <remarks>
/// <para>
/// A timer set to 33ms does not fire every 33ms on Windows' default clock, which ticks every
/// 15.6ms: it fires after 31 or 47, in turn. That is invisible at the pet's walking pace of thirty-
/// odd pixels a second and plain at the three hundred a second of a throw, where each step is a
/// jump of ten pixels or more.
/// </para>
/// <para>
/// The finer tick costs power for as long as it is held, so it is held only while it matters and
/// counted, so that two reasons to want it do not release it for each other.
/// </para>
/// </remarks>
internal static class HighResolutionTimer
{
    private const uint Period = 1;
    private static readonly object Gate = new();
    private static int _holders;

    public static void Acquire()
    {
        lock (Gate)
        {
            if (_holders++ == 0)
            {
                _ = TimeBeginPeriod(Period);
            }
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            if (_holders > 0 && --_holders == 0)
            {
                _ = TimeEndPeriod(Period);
            }
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);
}
