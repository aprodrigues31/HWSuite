using LibreHardwareMonitor.Hardware;

namespace HWSuite.Providers.Lhm;

/// <summary>
/// LibreHardwareMonitor keeps process-wide state (PawnIO handles, hardware mutexes): Computer.Close()
/// releases it for everyone, so closing one Computer while another is still reading breaks the
/// other (NullReferenceException in the sensors). Every Computer in HWSuite opens and closes
/// through here; the shared state is only released when the last one closes.
/// </summary>
internal static class LhmRuntime
{
    private static readonly Lock Gate = new();
    private static int _open;

    public static bool AnyOpen
    {
        get
        {
            lock (Gate)
            {
                return _open > 0;
            }
        }
    }

    public static void Open(Computer computer)
    {
        lock (Gate)
        {
            computer.Open();
            _open++;
        }
    }

    public static void Close(Computer computer)
    {
        lock (Gate)
        {
            _open--;
            if (_open == 0)
            {
                computer.Close();
            }

            // Otherwise leave it: its hardware objects are simply dropped, and the shared state
            // stays alive for the Computer that is still in use.
        }
    }
}
