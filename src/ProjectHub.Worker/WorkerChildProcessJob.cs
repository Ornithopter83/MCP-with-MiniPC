using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ProjectHub.Worker;

internal sealed class WorkerChildProcessJob : IDisposable
{
    internal const uint KillOnJobCloseLimitFlag = 0x00002000;
    private const int JobObjectBasicProcessIdListClass = 3;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint Th32csSnapProcess = 0x00000002;

    private readonly object _gate = new();
    private readonly string _ownerLabel;
    private SafeJobHandle? _handle;
    private bool _disposed;

    public WorkerChildProcessJob(string ownerLabel)
    {
        _ownerLabel = string.IsNullOrWhiteSpace(ownerLabel) ? "Worker child process" : ownerLabel.Trim();
        if (!OperatingSystem.IsWindows())
            return;

        var rawHandle = CreateJobObjectW(IntPtr.Zero, null);
        if (rawHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{_ownerLabel} Job Object를 만들지 못했습니다.");

        var handle = new SafeJobHandle(rawHandle);
        try
        {
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = KillOnJobCloseLimitFlag
                }
            };
            var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(information, buffer, false);
                if (!SetInformationJobObject(
                        handle.DangerousGetHandle(),
                        JobObjectExtendedLimitInformationClass,
                        buffer,
                        (uint)size))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        $"{_ownerLabel} Job Object에 KILL_ON_JOB_CLOSE를 설정하지 못했습니다.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            _handle = handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    public bool Enabled
    {
        get
        {
            lock (_gate)
                return !_disposed && _handle is not null && !_handle.IsInvalid && !_handle.IsClosed;
        }
    }

    public void Assign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_handle is null)
                return;

            if (!AssignProcessToJobObject(_handle.DangerousGetHandle(), process.Handle))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"{_ownerLabel} 프로세스를 Worker Job Object에 연결하지 못했습니다.");
            }
        }
    }

    public IReadOnlyList<int> SnapshotProcessIds()
    {
        lock (_gate)
        {
            if (_disposed || _handle is null || _handle.IsInvalid || _handle.IsClosed)
                return Array.Empty<int>();

            const int capacity = 256;
            var headerSize = sizeof(uint) * 2;
            var size = headerSize + (IntPtr.Size * capacity);
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!QueryInformationJobObject(
                        _handle.DangerousGetHandle(),
                        JobObjectBasicProcessIdListClass,
                        buffer,
                        (uint)size,
                        out _))
                    return Array.Empty<int>();

                var count = Math.Min(Marshal.ReadInt32(buffer, sizeof(uint)), capacity);
                var result = new List<int>(count);
                for (var index = 0; index < count; index++)
                {
                    var processId = Marshal.ReadIntPtr(buffer, headerSize + (index * IntPtr.Size)).ToInt64();
                    if (processId is > 0 and <= int.MaxValue)
                        result.Add((int)processId);
                }
                return result;
            }
            catch
            {
                return Array.Empty<int>();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    internal static int TerminateDescendants(
        int rootProcessId,
        DateTimeOffset notBefore)
    {
        if (!OperatingSystem.IsWindows() || rootProcessId <= 0)
            return 0;

        var descendants = SnapshotDescendantProcessIds(rootProcessId);
        if (descendants.Count == 0)
            return 0;

        var minimumStartUtc = notBefore.UtcDateTime.AddSeconds(-2);
        var terminated = 0;

        // 깊은 후손부터 정리해 새 helper가 다시 파생될 가능성을 줄인다.
        foreach (var processId in descendants
                     .OrderByDescending(entry => entry.Depth)
                     .Select(entry => entry.ProcessId))
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                DateTime startedUtc;
                try
                {
                    startedUtc = process.StartTime.ToUniversalTime();
                }
                catch
                {
                    // PID 재사용 가능성을 배제할 수 없으면 건드리지 않는다.
                    continue;
                }

                if (startedUtc < minimumStartUtc || process.HasExited)
                    continue;

                process.Kill(entireProcessTree: true);
                terminated++;
            }
            catch (ArgumentException)
            {
                // 이미 종료된 PID.
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        return terminated;
    }

    private static IReadOnlyList<(int ProcessId, int Depth)> SnapshotDescendantProcessIds(
        int rootProcessId)
    {
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == new IntPtr(-1))
            return Array.Empty<(int, int)>();

        try
        {
            var childrenByParent = new Dictionary<int, List<int>>();
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>()
            };

            if (!Process32FirstW(snapshot, ref entry))
                return Array.Empty<(int, int)>();

            do
            {
                if (entry.ProcessId is > 0 and <= int.MaxValue &&
                    entry.ParentProcessId is > 0 and <= int.MaxValue)
                {
                    var parentId = (int)entry.ParentProcessId;
                    if (!childrenByParent.TryGetValue(parentId, out var children))
                    {
                        children = new List<int>();
                        childrenByParent[parentId] = children;
                    }
                    children.Add((int)entry.ProcessId);
                }

                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32NextW(snapshot, ref entry));

            var result = new List<(int ProcessId, int Depth)>();
            var pending = new Queue<(int ProcessId, int Depth)>();
            var seen = new HashSet<int> { rootProcessId };
            pending.Enqueue((rootProcessId, 0));

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (!childrenByParent.TryGetValue(current.ProcessId, out var children))
                    continue;

                foreach (var childId in children)
                {
                    if (!seen.Add(childId))
                        continue;

                    var depth = current.Depth + 1;
                    result.Add((childId, depth));
                    pending.Enqueue((childId, depth));
                }
            }

            return result;
        }
        catch
        {
            return Array.Empty<(int, int)>();
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    public void Dispose()
    {
        SafeJobHandle? handle;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            handle = _handle;
            _handle = null;
        }

        handle?.Dispose();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        IntPtr jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job,
        int jobObjectInformationClass,
        IntPtr jobObjectInformation,
        uint jobObjectInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(
        uint flags,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(
        IntPtr snapshot,
        ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(
        IntPtr snapshot,
        ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobHandle(IntPtr handle)
            : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
            => CloseHandle(handle);
    }
}
