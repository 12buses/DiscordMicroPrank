using System.Reflection;
using System.Text;

namespace MicPrank;

internal static class Program
{
    private static PrankRunner? _runner;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // В некоторых окружениях кодировку консоли изменить нельзя.
        }

        Console.Title = "MicPrank";

        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            Log.Info("Подсказка: MicPrank --help");
            return 1;
        }

        if (options.Help)
        {
            Console.WriteLine(Options.HelpText);
            return 0;
        }

        if (options.Version)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            Console.WriteLine($"MicPrank {version}");
            return 0;
        }

        if (options.Diagnose)
        {
            return Diagnostics.Run(options);
        }

        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\MicPrank",
            createdNew: out var createdNew);

        if (!createdNew)
        {
            Log.Error("MicPrank уже запущен. Закройте предыдущую копию.");
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        var cancelCount = 0;

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            if (Interlocked.Increment(ref cancelCount) == 1)
            {
                eventArgs.Cancel = true;
                Log.Warn(
                    "Завершение... Микрофон будет включён обратно. " +
                    "Нажмите Ctrl+C ещё раз для принудительного выхода.");
                cancellation.Cancel();
            }
            else
            {
                eventArgs.Cancel = false;
            }
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                _runner?.RestoreNow();
            }
            catch
            {
                // При выходе уже ничего не поделать.
            }
        };

        try
        {
            try
            {
                _runner = new PrankRunner(options);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                Log.Info("Подсказка: MicPrank --help");
                return 1;
            }

            if (options.TestFocus)
            {
                return await _runner.RunTestFocusAsync(cancellation.Token);
            }

            if (options.TestHotkey)
            {
                return await _runner.RunTestHotkeyAsync(cancellation.Token);
            }

            return await _runner.RunAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            if (options.Verbose)
            {
                Log.Debug(ex.ToString());
            }

            return 1;
        }
        finally
        {
            _runner?.RestoreNow();
        }
    }
}
