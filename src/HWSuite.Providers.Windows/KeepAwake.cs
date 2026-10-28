using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HWSuite.Providers.Windows;

/// <summary>
/// Keeps Windows from sleeping while a long test runs (SetThreadExecutionState). The request belongs to
/// the calling thread: call <see cref="Start"/> and <see cref="Stop"/> from the same thread.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class KeepAwake
{
    private const uint EsContinuous = 0x80000000;
    private const uint EsSystemRequired = 0x00000001;

    public static void Start() => _ = SetThreadExecutionState(EsContinuous | EsSystemRequired);

    public static void Stop() => _ = SetThreadExecutionState(EsContinuous);

    [LibraryImport("kernel32.dll")]
    private static partial uint SetThreadExecutionState(uint flags);
}
