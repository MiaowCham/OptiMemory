using System.Runtime.InteropServices;

namespace OptiMemory;

[Flags]
public enum MemoryScope
{
    None = 0,
    EmptyWorkingSets = 1 << 0,
    FlushFileCache = 1 << 1,
    FlushModifiedList = 1 << 2,
    PurgeStandbyList = 1 << 3,
    PurgeLowPriorityStandbyList = 1 << 4,
    RegistryReconciliation = 1 << 5,
    CombinePhysicalMemory = 1 << 6,
    All = 0b1111111
}

public sealed record OptimizeResult(
    ulong TotalMemory,
    ulong BeforeAvailable,
    ulong AfterAvailable,
    List<string> Errors)
{
    public ulong Freed => AfterAvailable > BeforeAvailable ? AfterAvailable - BeforeAvailable : 0;

    public string FreedText => FormatBytes(Freed);
    public string AfterText => FormatBytes(AfterAvailable);
    public string TotalText => FormatBytes(TotalMemory);
    public double UsagePercent => TotalMemory > 0 ? (double)(TotalMemory - AfterAvailable) / TotalMemory * 100.0 : 0;

    public static string FormatBytes(ulong bytes)
    {
        if (bytes >= 1024UL * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0 / 1024.0:F1} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0:F1} KB";
    }
}

public static class MemoryOptimizer
{
    private static bool _privilegesAcquired;

    public static bool IsAdmin => NativeInterop.IsRunningAsAdministrator();

    private static void ExecuteMemoryListOperation(int infoValue)
    {
        var handle = GCHandle.Alloc(infoValue, GCHandleType.Pinned);
        try
        {
            NativeInterop.SetSystemInformation(
                NativeInterop.SystemInformationClass.SystemMemoryListInformation,
                handle.AddrOfPinnedObject(),
                sizeof(int));
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
    }

    private static void ExecuteStructureOperation<T>(T structure, NativeInterop.SystemInformationClass infoClass) where T : struct
    {
        var handle = GCHandle.Alloc(structure, GCHandleType.Pinned);
        try
        {
            NativeInterop.SetSystemInformation(
                infoClass,
                handle.AddrOfPinnedObject(),
                (uint)Marshal.SizeOf(structure));
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
        }
    }

    private static void AcquirePrivileges()
    {
        if (_privilegesAcquired) return;
        NativeInterop.SetPrivilege(NativeInterop.SePrivilege.SeProfileSingleProcessPrivilege, true);
        NativeInterop.SetPrivilege(NativeInterop.SePrivilege.SeIncreaseQuotaPrivilege, true);
        _privilegesAcquired = true;
    }

    public static OptimizeResult Optimize(MemoryScope scope = MemoryScope.All)
    {
        var (total, before) = NativeInterop.GetPhysicalMemoryBytes();

        // Always perform managed GC cleanup
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);

        var errors = new List<string>();

        if (IsAdmin)
        {
            AcquirePrivileges();
            if (scope.HasFlag(MemoryScope.EmptyWorkingSets))
                TryRun(() => ExecuteMemoryListOperation(2), "EmptyWorkingSets", errors);
            if (scope.HasFlag(MemoryScope.FlushFileCache))
                TryRun(FlushFileCache, "FlushFileCache", errors);
            if (scope.HasFlag(MemoryScope.FlushModifiedList))
                TryRun(() => ExecuteMemoryListOperation(3), "FlushModifiedList", errors);
            if (scope.HasFlag(MemoryScope.PurgeStandbyList))
                TryRun(() => ExecuteMemoryListOperation(4), "PurgeStandbyList", errors);
            if (scope.HasFlag(MemoryScope.PurgeLowPriorityStandbyList))
                TryRun(() => ExecuteMemoryListOperation(5), "PurgeLowPriorityStandbyList", errors);
            if (scope.HasFlag(MemoryScope.RegistryReconciliation))
                TryRun(RegistryReconciliation, "RegistryReconciliation", errors);
            if (scope.HasFlag(MemoryScope.CombinePhysicalMemory))
                TryRun(CombinePhysicalMemory, "CombinePhysicalMemory", errors);
        }
        else
        {
            // Without admin: attempt EmptyWorkingSets (may partially succeed)
            if (scope.HasFlag(MemoryScope.EmptyWorkingSets))
                TryRun(() => ExecuteMemoryListOperation(2), "EmptyWorkingSets", errors);
        }

        var (_, after) = NativeInterop.GetPhysicalMemoryBytes();
        return new OptimizeResult(total, before, after, errors);
    }

    public static (ulong Total, ulong Available) GetMemoryStatus()
        => NativeInterop.GetPhysicalMemoryBytes();

    private static void TryRun(Action action, string name, List<string> errors)
    {
        try { action(); }
        catch (Exception ex) { errors.Add($"{name}: {ex.Message}"); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_FILECACHE_INFORMATION
    {
        public UIntPtr CurrentSize, PeakSize, PageFaultCount;
        public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public UIntPtr CurrentSizeIncludingTransitionInPages, PeakSizeIncludingTransitionInPages;
        public UIntPtr TransitionRePurposeCount, Flags;
    }

    private static void FlushFileCache()
    {
        var info = new SYSTEM_FILECACHE_INFORMATION
        {
            MaximumWorkingSet = UIntPtr.MaxValue,
            MinimumWorkingSet = UIntPtr.MaxValue
        };
        ExecuteStructureOperation(info, NativeInterop.SystemInformationClass.SystemFileCacheInformationEx);
    }

    private static void RegistryReconciliation() =>
        NativeInterop.SetSystemInformation(
            NativeInterop.SystemInformationClass.SystemRegistryReconciliationInformation,
            IntPtr.Zero, 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_COMBINE_INFORMATION_EX
    {
        public IntPtr Handle;
        public UIntPtr PagesCombined;
        public uint Flags;
    }

    private static void CombinePhysicalMemory()
    {
        var info = new MEMORY_COMBINE_INFORMATION_EX();
        ExecuteStructureOperation(info, NativeInterop.SystemInformationClass.SystemCombinePhysicalMemoryInformation);
    }
}
