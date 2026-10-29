using System.Runtime.InteropServices;

namespace HWSuite.Gui.Services;

/// <summary>
/// The Windows "open" dialog (IFileOpenDialog) for picking a folder or a file. Used instead of WinUI's pickers,
/// which do not work in an unpackaged app running as administrator — the way HWSuite usually runs.
/// </summary>
internal static class ShellDialogs
{
    private const uint FosPickFolders = 0x20;
    private const uint FosForceFileSystem = 0x40;
    private const uint FosPathMustExist = 0x800;
    private const uint FosFileMustExist = 0x1000;
    private const uint SigdnFileSysPath = 0x80058000;
    private const int Cancelled = unchecked((int)0x800704C7);

    /// <summary>A folder, or null when cancelled.</summary>
    public static string? PickFolder(string title, string? start) =>
        Show(title, start, FosPickFolders | FosForceFileSystem | FosPathMustExist, null);

    /// <summary>A file matching one of <paramref name="filters"/> ("Images", "*.png;*.jpg"), or null when cancelled.</summary>
    public static string? PickFile(string title, string? start, params (string Name, string Pattern)[] filters) =>
        Show(title, start, FosForceFileSystem | FosPathMustExist | FosFileMustExist, filters);

    private static string? Show(string title, string? start, uint options, (string Name, string Pattern)[]? filters)
    {
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        try
        {
            dialog.GetOptions(out var current);
            dialog.SetOptions(current | options);
            dialog.SetTitle(title);
            if (filters is { Length: > 0 })
            {
                var specs = filters.Select(f => new FilterSpec { Name = f.Name, Spec = f.Pattern }).ToArray();
                dialog.SetFileTypes((uint)specs.Length, specs);
            }

            var folder = start is null ? null : File.Exists(start) ? Path.GetDirectoryName(start) : start;
            if (folder is not null && Directory.Exists(folder)
                && SHCreateItemFromParsingName(folder, IntPtr.Zero, typeof(IShellItem).GUID, out var item) == 0)
            {
                dialog.SetFolder(item);
                Marshal.ReleaseComObject(item);
            }

            var owner = App.MainWindow is { } window ? WinRT.Interop.WindowNative.GetWindowHandle(window) : IntPtr.Zero;
            var hr = dialog.Show(owner);
            if (hr == Cancelled)
            {
                return null;
            }

            Marshal.ThrowExceptionForHR(hr);
            dialog.GetResult(out var result);
            try
            {
                result.GetDisplayName(SigdnFileSysPath, out var name);
                try
                {
                    return Marshal.PtrToStringUni(name);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(name);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(result);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(dialog);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FilterSpec
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Name;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Spec;
    }

    [ComImport]
    [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialog;

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);

        void GetParent(out IShellItem parent);

        void GetDisplayName(uint form, out IntPtr name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IShellItem other, uint hint, out int order);
    }

    // IFileOpenDialog = IModalWindow + IFileDialog + its own two methods, in vtable order.
    [ComImport]
    [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr owner);

        void SetFileTypes(uint count, [MarshalAs(UnmanagedType.LPArray)] FilterSpec[] filters);

        void SetFileTypeIndex(uint index);

        void GetFileTypeIndex(out uint index);

        void Advise(IntPtr events, out uint cookie);

        void Unadvise(uint cookie);

        void SetOptions(uint options);

        void GetOptions(out uint options);

        void SetDefaultFolder(IShellItem folder);

        void SetFolder(IShellItem folder);

        void GetFolder(out IShellItem folder);

        void GetCurrentSelection(out IShellItem item);

        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);

        void GetResult(out IShellItem item);

        void AddPlace(IShellItem item, int place);

        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

        void Close(int hr);

        void SetClientGuid(ref Guid guid);

        void ClearClientData();

        void SetFilter(IntPtr filter);

        void GetResults(out IntPtr items);

        void GetSelectedItems(out IntPtr items);
    }
}
