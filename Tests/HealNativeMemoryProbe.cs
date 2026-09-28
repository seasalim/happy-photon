using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HappyPhoton.Tests;

internal static class HealNativeMemoryProbe
{
    internal sealed record HeapSample(string Handle, bool DynamicCrt, long BusyBytes,
        long FreeBytes, long RegionCommittedBytes, int WalkError);

    internal static object Capture()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        // GC statistics exclude cached free regions; VirtualQuery measures their OS commitment.
        var gc = GC.GetGCMemoryInfo();
        var heaps = GetHeaps().Select(ReadHeap).ToArray();
        return new
        {
            privateBytes = process.PrivateMemorySize64,
            managedAllocatedBytes = GC.GetTotalMemory(false),
            managedCommittedBytes = gc.TotalCommittedBytes,
            managedHeapBytes = gc.HeapSizeBytes,
            managedFragmentedBytes = gc.FragmentedBytes,
            collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) },
            allocations = Allocations(),
            heaps
        };
    }

    private static object Allocations()
    {
        var small = GCHandle.Alloc(new byte[1], GCHandleType.Pinned);
        var large = GCHandle.Alloc(new byte[100000], GCHandleType.Pinned);
        try
        {
            VirtualQuery(small.AddrOfPinnedObject(), out var smallRegion, 48);
            VirtualQuery(large.AddrOfPinnedObject(), out var largeRegion, 48);
            var groups = new Dictionary<nint, long>();
            nint address = 0;
            while (VirtualQuery(address, out var region, 48) != 0)
            {
                if (region.State == 0x1000 && region.Type == 0x20000)
                    groups[region.AllocationBase] = groups.GetValueOrDefault(region.AllocationBase) + (long)region.RegionSize;
                var next = region.BaseAddress + (nint)region.RegionSize;
                if (next <= address) break;
                address = next;
            }
            return new
            {
                smallManagedRoot = smallRegion.AllocationBase.ToString("X"),
                largeManagedRoot = largeRegion.AllocationBase.ToString("X"),
                committedPrivateRegions = groups.Where(pair => pair.Value >= 1000000)
                    .OrderByDescending(pair => pair.Value)
                    .Select(pair => new { root = pair.Key.ToString("X"), bytes = pair.Value }).ToArray()
            };
        }
        finally
        {
            small.Free();
            large.Free();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll")]
    private static extern nuint VirtualQuery(nint address, out MemoryInformation information, nuint length);

    internal static object OptimizeHeaps()
    {
        return GetHeaps().Select(heap =>
        {
            var info = new OptimizeInformation { Version = 1 };
            var success = HeapSetInformation(heap, 3, ref info, 8);
            return new { handle = heap.ToString("X"), success, error = success ? 0 : Marshal.GetLastWin32Error() };
        }).ToArray();
    }

    private static nint[] GetHeaps()
    {
        var heaps = new nint[GetProcessHeaps(0, null) + 16];
        var count = GetProcessHeaps((uint)heaps.Length, heaps);
        if (count > heaps.Length) throw new InvalidOperationException("Process heap list changed during capture.");
        return heaps.Take((int)count).ToArray();
    }

    private static HeapSample ReadHeap(nint heap)
    {
        long busy = 0;
        long free = 0;
        long committed = 0;
        // RegionCommittedBytes excludes large VirtualAlloc blocks; BusyBytes includes them.
        var entry = new HeapEntry();
        if (!HeapLock(heap))
            return new(heap.ToString("X"), heap == GetCrtHeap(), 0, 0, 0, Marshal.GetLastWin32Error());

        int error;
        try
        {
            while (HeapWalk(heap, ref entry))
            {
                if ((entry.Flags & 1) != 0) committed += entry.Committed;
                else if ((entry.Flags & 4) != 0) busy += entry.Size;
                else if ((entry.Flags & 2) == 0) free += entry.Size;
            }
            error = Marshal.GetLastWin32Error();
        }
        finally
        {
            HeapUnlock(heap);
        }

        return new(heap.ToString("X"), heap == GetCrtHeap(), busy, free, committed, error);
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct HeapEntry
    {
        [FieldOffset(0)] public nint Data;
        [FieldOffset(8)] public uint Size;
        [FieldOffset(14)] public ushort Flags;
        [FieldOffset(16)] public uint Committed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct OptimizeInformation
    {
        public uint Version;
        public uint Flags;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessHeaps(uint count, [Out] nint[]? heaps);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool HeapLock(nint heap);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool HeapUnlock(nint heap);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool HeapWalk(nint heap, ref HeapEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool HeapSetInformation(nint heap, int informationClass,
        ref OptimizeInformation information, nuint length);

    [DllImport("ucrtbase.dll", EntryPoint = "_get_heap_handle", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint GetCrtHeap();
}
