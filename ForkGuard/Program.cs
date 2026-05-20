// ForkGuard — Minecraft server process guardian for Fork
//
// Usage: ForkGuard.exe <pipeName> <workingDir> <javaPath> <javaArgs...>
//
//   pipeName   — epoch-seconds string (e.g. "1716163200"); used as pipe name suffix
//   workingDir — server directory (absolute path)
//   javaPath   — path to java.exe
//   javaArgs   — everything else, joined and passed to Java
//
// Writes fork-guard.marker to workingDir when ready (deleted on exit).
// Exposes named pipe \\.\pipe\fork-<pipeName> for stdin commands (re-attach).
// Echoes Java stdout → own stdout (Fork reads this).
// Echoes Java stderr → own stderr + fork-guard.log (appended, timestamped).
// Uses a Job Object so Java is killed automatically if ForkGuard dies.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: ForkGuard <pipeName> <workingDir> <javaPath> <javaArgs...>");
    return 1;
}

string pipeName    = args[0];
string workingDir  = args[1];
string javaPath    = args[2];
string javaArgs    = string.Join(" ", args[3..]);
string markerFile  = Path.Combine(workingDir, "fork-guard.marker");
string guardLog    = Path.Combine(workingDir, "fork-guard.log");

// ── Start Java ───────────────────────────────────────────────────────────────
var java = new Process();
java.StartInfo = new ProcessStartInfo
{
    FileName               = javaPath,
    Arguments              = javaArgs,
    WorkingDirectory       = workingDir,
    UseShellExecute        = false,
    RedirectStandardInput  = true,
    RedirectStandardOutput = true,
    RedirectStandardError  = true,
    CreateNoWindow         = true,
    // NOTE: Do NOT set WindowStyle — ProcessWindowStyle enum validation uses
    // reflection internally (InvalidEnumArgumentException), which is disabled
    // in NativeAOT builds. CreateNoWindow = true is sufficient.
};

try
{
    java.Start();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[ForkGuard] Failed to start Java: {ex.Message}");
    return 2;
}

// ── Job Object: Java is killed automatically when ForkGuard exits ─────────────
nint jobHandle = JobObject.Create();
if (jobHandle != 0)
    JobObject.AddProcess(jobHandle, java.Id);

// ── Marker file ───────────────────────────────────────────────────────────────
// Fork reads this to learn the pipe name and Java PID for re-attach.
File.WriteAllText(markerFile, $"{pipeName}\n{java.Id}\n");

// ── Relay: Java stdout → ForkGuard stdout ────────────────────────────────────
// Fork reads ForkGuard's stdout exactly as it would have read Java's stdout.
var stdoutRelay = new Thread(() =>
{
    try
    {
        string? line;
        while ((line = java.StandardOutput.ReadLine()) != null)
            Console.WriteLine(line);
    }
    catch { /* process exited */ }
}) { IsBackground = true, Name = "stdout-relay" };
stdoutRelay.Start();

// ── Relay: Java stderr → ForkGuard stderr + guard log ───────────────────────
var stderrRelay = new Thread(() =>
{
    try
    {
        using var log = new StreamWriter(guardLog, append: true) { AutoFlush = true };
        string? line;
        while ((line = java.StandardError.ReadLine()) != null)
        {
            Console.Error.WriteLine(line);
            log.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
        }
    }
    catch { }
}) { IsBackground = true, Name = "stderr-relay" };
stderrRelay.Start();

// ── Relay: ForkGuard stdin → Java stdin ──────────────────────────────────────
// When Fork holds the ForkGuard process, it writes to ForkGuard's stdin.
// We forward those writes to Java's stdin unchanged.
var stdinRelay = new Thread(() =>
{
    try
    {
        string? line;
        while ((line = Console.ReadLine()) != null)
            java.StandardInput.WriteLine(line);
    }
    catch { }
}) { IsBackground = true, Name = "stdin-relay" };
stdinRelay.Start();

// ── Named pipe: accept commands from a re-attached Fork instance ──────────────
// After Fork restarts, it opens a NamedPipeClientStream to send commands.
// We loop: wait for connection → relay lines → accept next connection.
var pipeThread = new Thread(() =>
{
    while (!java.HasExited)
    {
        NamedPipeServerStream? pipe = null;
        try
        {
            pipe = new NamedPipeServerStream(
                $"fork-{pipeName}",
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.None);

            pipe.WaitForConnection();

            using var reader = new StreamReader(pipe, leaveOpen: false);
            string? cmd;
            while (pipe.IsConnected && (cmd = reader.ReadLine()) != null)
            {
                try { java.StandardInput.WriteLine(cmd); }
                catch { break; }
            }
        }
        catch (IOException) { /* pipe broken — accept next */ }
        catch (Exception) when (!java.HasExited)
        {
            Thread.Sleep(200);
        }
        finally
        {
            try { pipe?.Dispose(); } catch { }
        }
    }
}) { IsBackground = true, Name = "pipe-listener" };
pipeThread.Start();

// ── Wait for Java ─────────────────────────────────────────────────────────────
java.WaitForExit();
int exitCode = java.ExitCode;

// Cleanup
try { File.Delete(markerFile); } catch { }
if (jobHandle != 0) JobObject.Close(jobHandle);

return exitCode;

// ─────────────────────────────────────────────────────────────────────────────
// Win32 Job Object helpers (P/Invoke, NativeAOT-compatible)
// ─────────────────────────────────────────────────────────────────────────────
static class JobObject
{
    private const int  JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateJobObject(nint lpJobAttributes, nint lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        nint hJob, int JobObjectInformationClass,
        ref ExtendedLimitInfo lpJobObjectInformation, uint cbJobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(nint hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInfo
    {
        public long  PerProcessUserTimeLimit;
        public long  PerJobUserTimeLimit;
        public uint  LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint  ActiveProcessLimit;
        public nuint Affinity;
        public uint  PriorityClass;
        public uint  SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount,  WriteTransferCount,  OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInfo
    {
        public BasicLimitInfo BasicLimitInformation;
        public IoCounters      IoInfo;
        public nuint           ProcessMemoryLimit;
        public nuint           JobMemoryLimit;
        public nuint           PeakProcessMemoryUsed;
        public nuint           PeakJobMemoryUsed;
    }

    public static nint Create()
    {
        nint h = CreateJobObject(0, 0);
        if (h == 0) return 0;

        var info = new ExtendedLimitInfo();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        SetInformationJobObject(h, JobObjectExtendedLimitInformation,
            ref info, (uint)Marshal.SizeOf<ExtendedLimitInfo>());
        return h;
    }

    public static void AddProcess(nint job, int pid)
    {
        nint ph = OpenProcess(PROCESS_ALL_ACCESS, false, pid);
        if (ph != 0)
        {
            AssignProcessToJobObject(job, ph);
            CloseHandle(ph);
        }
    }

    public static void Close(nint h) => CloseHandle(h);
}
