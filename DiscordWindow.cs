using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MicPrank;

/// <summary>
/// Поиск и переключение окна Discord. Нужно только для режима --focus-discord,
/// когда глобальная горячая клавиша Discord не зарегистрирована.
/// </summary>
public static class DiscordWindow
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    public static IntPtr FindMainWindow()
    {
        var pids = DiscordHelper.GetProcessIds();
        if (pids.Count == 0)
        {
            return IntPtr.Zero;
        }

        // Обычный путь: у главного процесса Discord есть MainWindowHandle.
        foreach (var name in DiscordHelper.ProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    var handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero && IsWindow(handle))
                    {
                        return handle;
                    }
                }
                catch
                {
                    // Игнорируем процессы, к которым нет доступа.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        // Discord может быть свёрнут в трей. Ищем самое большое окно процесса.
        var best = IntPtr.Zero;
        var bestArea = 0L;
        var bestVisible = false;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var processId);
            if (!pids.Contains((int)processId) || !IsWindow(hWnd))
            {
                return true;
            }

            var exStyle = GetWindowLongPtr(hWnd, GwlExStyle).ToInt64();
            if ((exStyle & WsExToolWindow) != 0)
            {
                return true;
            }

            if (!GetWindowRect(hWnd, out var rect))
            {
                return true;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0)
            {
                return true;
            }

            var visible = IsWindowVisible(hWnd);
            if (best != IntPtr.Zero && bestVisible && !visible)
            {
                return true;
            }

            var area = (long)width * height;
            if (!bestVisible && visible)
            {
                best = hWnd;
                bestArea = area;
                bestVisible = true;
                return true;
            }

            if (area > bestArea)
            {
                best = hWnd;
                bestArea = area;
                bestVisible = visible;
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    public static bool TryFocusDiscord(out string? error)
    {
        var handle = FindMainWindow();
        if (handle == IntPtr.Zero)
        {
            error = "Окно Discord не найдено. Возможно, Discord свёрнут в трей или ещё запускается.";
            return false;
        }

        if (!TryFocus(handle))
        {
            error = "Не удалось переключить фокус на Discord. Попробуйте режим --mode system.";
            return false;
        }

        error = null;
        return true;
    }

    public static IntPtr GetForegroundWindow()
    {
        return NativeGetForegroundWindow();
    }

    public static bool TryFocus(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle))
        {
            return false;
        }

        if (IsIconic(handle))
        {
            ShowWindowAsync(handle, SwRestore);
        }
        else
        {
            ShowWindowAsync(handle, SwShow);
        }

        var foreground = NativeGetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();
        var attached = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = AttachThreadInput(currentThread, foregroundThread, true);
            }

            SetForegroundWindow(handle);
            BringWindowToTop(handle);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (NativeGetForegroundWindow() == handle)
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern IntPtr GetForegroundWindowNative();

    private static IntPtr NativeGetForegroundWindow() => GetForegroundWindowNative();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
