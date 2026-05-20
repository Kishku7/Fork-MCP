using System;
using System.IO;
using System.IO.Pipes;
using Fork.ViewModel;

namespace Fork.Logic.CustomConsole;

/// <summary>
/// IConsoleReader that routes commands to a running ForkGuard instance
/// via a named pipe. Used when Fork re-attaches to a server it didn't start.
/// </summary>
public sealed class GuardedConsoleReader : IConsoleReader
{
    private readonly string pipeName;
    private NamedPipeClientStream? pipe;
    private StreamWriter? writer;
    private readonly object writeLock = new();

    public GuardedConsoleReader(string pipeName)
    {
        this.pipeName = pipeName;
        TryConnect();
    }

    private void TryConnect()
    {
        try
        {
            var p = new NamedPipeClientStream(".", $"fork-{pipeName}", PipeDirection.Out, PipeOptions.None);
            p.Connect(2000);
            writer = new StreamWriter(p) { AutoFlush = true };
            pipe   = p;
        }
        catch
        {
            // Guard not ready yet — next Read() will retry.
            writer = null;
            pipe   = null;
        }
    }

    public void Read(string line, EntityViewModel source)
    {
        lock (writeLock)
        {
            try
            {
                if (writer == null) TryConnect();
                writer?.WriteLine(line);
            }
            catch
            {
                // Pipe broken — reconnect and retry once.
                writer?.Dispose();
                pipe?.Dispose();
                writer = null;
                pipe   = null;

                TryConnect();
                try { writer?.WriteLine(line); }
                catch { /* give up for this command */ }
            }

            // Always echo to the Fork console so the user can see what was sent.
            ConsoleWriter.Write(line, source);
        }
    }
}
