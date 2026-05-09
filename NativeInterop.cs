using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace OptiMemory;

internal static partial class NativeInterop
{
    [LibraryImport("ntdll.dll")]
    private static partial uint RtlAdjustPrivilege(
        SePrivilege privilege,
        [MarshalAs(UnmanagedType.U1)] bool enable,
        [MarshalAs(UnmanagedType.U1)] bool currentThread,
        [MarshalAs(UnmanagedType.U1)] out bool enabled);

    [LibraryImport("ntdll.dll")]
    private static partial ulong RtlNtStatusToDosError(uint status);

    [LibraryImport("ntdll.dll")]
    private static partial uint NtSetSystemInformation(
        SystemInformationClass systemInformationClass,
        IntPtr systemInformation,
        uint systemInformationLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetConsoleWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);

    public enum SePrivilege : uint
    {
        SeIncreaseQuotaPrivilege = 5,
        SeProfileSingleProcessPrivilege = 13
    }

    public enum SystemInformationClass
    {
        SystemMemoryListInformation = 80,
        SystemFileCacheInformationEx = 81,
        SystemCombinePhysicalMemoryInformation = 130,
        SystemRegistryReconciliationInformation = 155
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public static bool SetPrivilege(SePrivilege privilege, bool state)
    {
        var result = RtlAdjustPrivilege(privilege, state, false, out _);
        return result == 0;
    }

    public static void SetSystemInformation(SystemInformationClass infoClass, IntPtr info, uint infoLength)
    {
        var result = NtSetSystemInformation(infoClass, info, infoLength);
        if (result != 0)
            throw new Win32Exception((int)RtlNtStatusToDosError(result));
    }

    public static (ulong Total, ulong Available) GetPhysicalMemoryBytes()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (status.ullTotalPhys, status.ullAvailPhys);
    }

    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static void AttachParentConsole() => AttachConsole(ATTACH_PARENT_PROCESS);

    /// <summary>隐藏当前进程的控制台窗口（GUI / 提权子进程模式使用）。</summary>
    public static void HideConsoleWindow()
    {
        var hwnd = GetConsoleWindow();
        if (hwnd != IntPtr.Zero) ShowWindow(hwnd, 0 /* SW_HIDE */);
    }
}
