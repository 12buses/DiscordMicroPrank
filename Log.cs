namespace MicPrank;

internal static class Log
{
    private static readonly object Sync = new();

    public static void Info(string message) => Write(message, ConsoleColor.Gray);

    public static void Action(string message) => Write(message, ConsoleColor.Yellow);

    public static void Warn(string message) => Write("Внимание: " + message, ConsoleColor.DarkYellow);

    public static void Error(string message) => Write("Ошибка: " + message, ConsoleColor.Red);

    public static void Debug(string message) => Write(message, ConsoleColor.DarkGray);

    private static void Write(string message, ConsoleColor color)
    {
        lock (Sync)
        {
            try
            {
                var previous = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
                Console.WriteLine(message);
                Console.ForegroundColor = previous;
            }
            catch (IOException)
            {
                // Консоль могли закрыть во время выхода.
            }
            catch (ObjectDisposedException)
            {
                // Консоль уже недоступна.
            }
        }
    }
}
