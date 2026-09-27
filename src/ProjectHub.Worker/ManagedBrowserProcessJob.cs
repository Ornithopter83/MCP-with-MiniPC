using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ProjectHub.Worker;

internal sealed class ManagedBrowserProcessJob : IDisposable
{
    internal const uint KillOnJobCloseLimitFlag = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;
    private readonly SafeJobHandle? _handle;

    public ManagedBrowserProcessJob()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var rawHandle = CreateJobObjectW(IntPtr.Zero, null);
        if (rawHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "관리형 Chromium Job Object를 만들지 못했습니다.");

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
                        "관리형 Chromium Job Object에 KILL_ON_JOB_CLOSE를 설정하지 못했습니다.");
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
        => _handle is not null && !_handle.IsInvalid && !_handle.IsClosed;

    public void Assign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (_handle is null)
            return;

        if (!AssignProcessToJobObject(_handle.DangerousGetHandle(), process.Handle))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "관리형 Chromium 프로세스를 Worker Job Object에 연결하지 못했습니다.");
        }
    }

    public void Dispose()
        => _handle?.Dispose();

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
