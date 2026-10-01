using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ProjectHub.Worker;

internal sealed class SuspendedJobProcess : IDisposable
{
    public SuspendedJobProcess(
        Process process,
        StreamWriter? standardInput,
        StreamReader? standardOutput,
        StreamReader? standardError)
    {
        Process = process;
        StandardInput = standardInput;
        StandardOutput = standardOutput;
        StandardError = standardError;
    }

    public Process Process { get; }
    public StreamWriter? StandardInput { get; }
    public StreamReader? StandardOutput { get; }
    public StreamReader? StandardError { get; }

    public void Dispose()
    {
        try { StandardInput?.Dispose(); } catch { }
        try { StandardOutput?.Dispose(); } catch { }
        try { StandardError?.Dispose(); } catch { }
        Process.Dispose();
    }
}

internal static class SuspendedJobProcessLauncher
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint CreateNoWindow = 0x08000000;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    internal static SuspendedJobProcess Start(
        ProcessStartInfo startInfo,
        IntPtr jobHandle)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Suspended Job process launch is supported only on Windows.");
        if (startInfo.UseShellExecute)
            throw new InvalidOperationException("Managed child processes require UseShellExecute=false.");
        if (jobHandle == IntPtr.Zero || jobHandle == new IntPtr(-1))
            throw new InvalidOperationException("A valid Job Object handle is required.");

        SafeFileHandle? childStdIn = null;
        SafeFileHandle? childStdOut = null;
        SafeFileHandle? childStdErr = null;
        SafeFileHandle? parentStdIn = null;
        SafeFileHandle? parentStdOut = null;
        SafeFileHandle? parentStdErr = null;
        IntPtr environmentBlock = IntPtr.Zero;
        Process? managedProcess = null;
        StreamWriter? standardInput = null;
        StreamReader? standardOutput = null;
        StreamReader? standardError = null;
        ProcessInformation processInfo = default;

        try
        {
            var inheritHandles =
                startInfo.RedirectStandardInput ||
                startInfo.RedirectStandardOutput ||
                startInfo.RedirectStandardError;

            var startupInfo = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfo>()
            };

            if (inheritHandles)
            {
                startupInfo.Flags |= StartfUseStdHandles;

                if (startInfo.RedirectStandardInput)
                {
                    CreatePipePair(
                        childReads: true,
                        out childStdIn,
                        out parentStdIn);
                    startupInfo.StandardInput = childStdIn.DangerousGetHandle();
                }
                else
                {
                    childStdIn = OpenInheritedNullInput();
                    startupInfo.StandardInput = childStdIn.DangerousGetHandle();
                }

                if (startInfo.RedirectStandardOutput)
                {
                    CreatePipePair(
                        childReads: false,
                        out childStdOut,
                        out parentStdOut);
                    startupInfo.StandardOutput = childStdOut.DangerousGetHandle();
                }
                else
                {
                    childStdOut = OpenInheritedNullOutput();
                    startupInfo.StandardOutput = childStdOut.DangerousGetHandle();
                }

                if (startInfo.RedirectStandardError)
                {
                    CreatePipePair(
                        childReads: false,
                        out childStdErr,
                        out parentStdErr);
                    startupInfo.StandardError = childStdErr.DangerousGetHandle();
                }
                else
                {
                    childStdErr = OpenInheritedNullOutput();
                    startupInfo.StandardError = childStdErr.DangerousGetHandle();
                }
            }

            var commandLine = new StringBuilder(BuildCommandLine(startInfo));
            environmentBlock = BuildEnvironmentBlock(startInfo);
            var creationFlags = CreateSuspended | CreateUnicodeEnvironment;
            if (startInfo.CreateNoWindow)
                creationFlags |= CreateNoWindow;

            if (!CreateProcessW(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    inheritHandles,
                    creationFlags,
                    environmentBlock,
                    string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                        ? null
                        : startInfo.WorkingDirectory,
                    ref startupInfo,
                    out processInfo))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Managed child process start failed: {startInfo.FileName}");
            }

            if (!AssignProcessToJobObject(jobHandle, processInfo.Process))
            {
                var error = Marshal.GetLastWin32Error();
                TerminateProcess(processInfo.Process, 1);
                throw new Win32Exception(
                    error,
                    $"Managed child process could not be assigned to its Job Object: {startInfo.FileName}");
            }

            managedProcess = Process.GetProcessById(unchecked((int)processInfo.ProcessId));

            childStdIn?.Dispose();
            childStdIn = null;
            childStdOut?.Dispose();
            childStdOut = null;
            childStdErr?.Dispose();
            childStdErr = null;

            if (parentStdIn is not null)
            {
                var stream = new FileStream(parentStdIn, FileAccess.Write, 4096, isAsync: true);
                parentStdIn = null;
                standardInput = new StreamWriter(
                    stream,
                    NormalizeWriterEncoding(startInfo.StandardInputEncoding),
                    4096,
                    leaveOpen: false);
            }

            if (parentStdOut is not null)
            {
                var stream = new FileStream(parentStdOut, FileAccess.Read, 4096, isAsync: true);
                parentStdOut = null;
                standardOutput = new StreamReader(
                    stream,
                    startInfo.StandardOutputEncoding ?? Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 4096,
                    leaveOpen: false);
            }

            if (parentStdErr is not null)
            {
                var stream = new FileStream(parentStdErr, FileAccess.Read, 4096, isAsync: true);
                parentStdErr = null;
                standardError = new StreamReader(
                    stream,
                    startInfo.StandardErrorEncoding ?? Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 4096,
                    leaveOpen: false);
            }

            if (ResumeThread(processInfo.Thread) == uint.MaxValue)
            {
                var error = Marshal.GetLastWin32Error();
                TerminateProcess(processInfo.Process, 1);
                throw new Win32Exception(
                    error,
                    $"Managed child process resume failed: {startInfo.FileName}");
            }

            return new SuspendedJobProcess(
                managedProcess,
                standardInput,
                standardOutput,
                standardError);
        }
        catch
        {
            if (processInfo.Process != IntPtr.Zero)
            {
                try { TerminateProcess(processInfo.Process, 1); }
                catch { }
            }
            try { standardInput?.Dispose(); } catch { }
            try { standardOutput?.Dispose(); } catch { }
            try { standardError?.Dispose(); } catch { }
            try { managedProcess?.Dispose(); } catch { }
            throw;
        }
        finally
        {
            if (environmentBlock != IntPtr.Zero)
                Marshal.FreeHGlobal(environmentBlock);

            childStdIn?.Dispose();
            childStdOut?.Dispose();
            childStdErr?.Dispose();
            parentStdIn?.Dispose();
            parentStdOut?.Dispose();
            parentStdErr?.Dispose();

            if (processInfo.Thread != IntPtr.Zero)
                CloseHandle(processInfo.Thread);
            if (processInfo.Process != IntPtr.Zero)
                CloseHandle(processInfo.Process);
        }
    }

    private static void CreatePipePair(
        bool childReads,
        out SafeFileHandle childHandle,
        out SafeFileHandle parentHandle)
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true
        };

        if (!CreatePipe(out var read, out var write, ref attributes, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Managed child pipe creation failed.");

        SafeFileHandle? readHandle = new(read, ownsHandle: true);
        SafeFileHandle? writeHandle = new(write, ownsHandle: true);
        try
        {
            var parent = childReads ? writeHandle : readHandle;
            if (!SetHandleInformation(
                    parent.DangerousGetHandle(),
                    HandleFlagInherit,
                    0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "Managed child pipe inheritance setup failed.");
            }

            if (childReads)
            {
                childHandle = readHandle;
                parentHandle = writeHandle;
            }
            else
            {
                childHandle = writeHandle;
                parentHandle = readHandle;
            }

            readHandle = null;
            writeHandle = null;
        }
        finally
        {
            readHandle?.Dispose();
            writeHandle?.Dispose();
        }
    }

    private static SafeFileHandle OpenInheritedNullInput()
        => OpenInheritedNull(GenericRead);

    private static SafeFileHandle OpenInheritedNullOutput()
        => OpenInheritedNull(0x40000000);

    private static SafeFileHandle OpenInheritedNull(uint access)
    {
        var attributes = new SecurityAttributes
        {
            Length = Marshal.SizeOf<SecurityAttributes>(),
            InheritHandle = true
        };
        var handle = CreateFileW(
            "NUL",
            access,
            FileShareRead | FileShareWrite,
            ref attributes,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        if (handle == new IntPtr(-1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "NUL handle creation failed.");

        return new SafeFileHandle(handle, ownsHandle: true);
    }

    private static IntPtr BuildEnvironmentBlock(ProcessStartInfo startInfo)
    {
        var entries = startInfo.Environment
            .Where(pair => pair.Value is not null)
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key + "=" + pair.Value)
            .ToArray();

        var block = string.Join("\0", entries) + "\0\0";
        return Marshal.StringToHGlobalUni(block);
    }

    private static string BuildCommandLine(ProcessStartInfo startInfo)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteArgument(startInfo.FileName));

        if (startInfo.ArgumentList.Count > 0)
        {
            foreach (var argument in startInfo.ArgumentList)
                builder.Append(' ').Append(QuoteArgument(argument));
        }
        else if (!string.IsNullOrWhiteSpace(startInfo.Arguments))
        {
            builder.Append(' ').Append(startInfo.Arguments);
        }

        return builder.ToString();
    }

    private static string QuoteArgument(string value)
    {
        value ??= string.Empty;
        if (value.Length > 0 &&
            value.All(ch => ch is not ' ' and not '\t' and not '\n' and not '\v' and not '"'))
            return value;

        var builder = new StringBuilder();
        builder.Append('"');
        var backslashes = 0;

        foreach (var ch in value)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
                builder.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                builder.Append('\\', backslashes);
                backslashes = 0;
            }

            builder.Append(ch);
        }

        if (backslashes > 0)
            builder.Append('\\', backslashes * 2);

        builder.Append('"');
        return builder.ToString();
    }

    private static Encoding NormalizeWriterEncoding(Encoding? encoding)
    {
        var selected = encoding ?? Encoding.UTF8;
        if (selected.CodePage == Encoding.UTF8.CodePage)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return selected;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        IntPtr job,
        IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out IntPtr readPipe,
        out IntPtr writePipe,
        ref SecurityAttributes pipeAttributes,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(
        IntPtr handle,
        uint mask,
        uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        ref SecurityAttributes securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }
}
