using Fork.ViewModel;

namespace Fork.Logic.CustomConsole;

/// <summary>
/// Abstraction over stdin channels: direct process stdin (normal start)
/// or a named pipe (re-attached via ForkGuard).
/// </summary>
public interface IConsoleReader
{
    void Read(string line, EntityViewModel source);
}
