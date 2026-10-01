using System.Collections.Concurrent;
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

    private static readonly ConcurrentDictionary<long, WorkerChildProcessJob> ActiveJobs = new();
    private static long _nextJobId;

    private readonly object _gate = new();
    private readonly string _ownerLabel;
    private readonly long _jobId;
    private SafeJobHandle? _handle;
    private bool _disposed;

    public WorkerChildProcessJob(string ownerLabel)
    {
        _ownerLabel = string.IsNullOrWhiteSpace(ownerLabel)
            ? "Worker child process"
            : ownerLabel.Trim();

        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Worker child Jobs require Windows.");

        var rawHandle = CreateJobObjectW(IntPtr.Zero, null);
        if (rawHandle == IntPtr.Zero)
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"{_ownerLabel} Job Object를 만들지 못했습니다.");

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
            _jobId = Interlocked.Increment(ref _nextJobId);
            ActiveJobs[_jobId] = this;
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
                return !_disposed &&
                       _handle is not null &&
                       !_handle.IsInvalid &&
                       !_handle.IsClosed;
        }
    }

    public SuspendedJobProcess Start(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        cancellationToken.ThrowIfCancellationRequested();

        SuspendedJobProcess launched;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_handle is null || _handle.IsInvalid || _handle.IsClosed)
                throw new InvalidOperationException(
                    $"{_ownerLabel} Job Object가 준비되지 않았습니다.");

            // 프로세스는 suspended 상태로 생성되고 Job 연결에 성공한 뒤에만 실행된다.
            launched = SuspendedJobProcessLauncher.Start(
                startInfo,
                _handle.DangerousGetHandle());
        }

        if (!cancellationToken.CanBeCanceled)
            return launched;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            launched.AttachCancellation(
                cancellationToken.Register(
                    static state => ((WorkerChildProcessJob)state!).Dispose(),
                    this));
            return launched;
        }
        catch
        {
            Dispose();
            launched.Dispose();
            throw;
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

                var count = Math.Min(
                    Marshal.ReadInt32(buffer, sizeof(uint)),
                    capacity);
                var result = new List<int>(count);
                for (var index = 0; index < count; index++)
                {
                    var processId = Marshal.ReadIntPtr(
                        buffer,
                        headerSize + (index * IntPtr.Size)).ToInt64();
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

        ActiveJobs.TryRemove(_jobId, out _);

        if (handle is not null && !handle.IsInvalid && !handle.IsClosed)
        {
            // 마지막 handle close 여부에만 의존하지 않는다.
            // 명시적으로 Job 전체를 종료한 뒤 handle을 닫는다.
            try { TerminateJobObject(handle.DangerousGetHandle(), 1); }
            catch { }
        }

        handle?.Dispose();
    }

    internal static void TerminateAllActiveJobs()
    {
        foreach (var job in ActiveJobs.Values.ToArray())
        {
            try { job.Dispose(); }
            catch { }
        }
    }

    internal static int ActiveJobCount => ActiveJobs.Count;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(
        IntPtr jobAttributes,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        IntPtr jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(
        IntPtr job,
        uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job,
        int jobObjectInformationClass,
        IntPtr jobObjectInformation,
        uint jobObjectInformationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

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
