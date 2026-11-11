using System.Runtime.InteropServices;

namespace MicPrank;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008
}

public enum HotkeyProbeResult
{
    Free,
    Taken,
    Error
}

/// <summary>
/// Разобранное сочетание клавиш. Умеет отправлять его через SendInput.
/// </summary>
public sealed class HotkeyDefinition
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventExtendedKey = 0x0001;

    private const ushort VkLeftShift = 0xA0;
    private const ushort VkLeftControl = 0xA2;
    private const ushort VkLeftAlt = 0xA4;
    private const ushort VkLeftWin = 0x5B;

    private static readonly Dictionary<string, (ushort Vk, string Name)> NamedKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SPACE"] = (0x20, "Space"),
            ["TAB"] = (0x09, "Tab"),
            ["ENTER"] = (0x0D, "Enter"),
            ["RETURN"] = (0x0D, "Enter"),
            ["ESC"] = (0x1B, "Esc"),
            ["ESCAPE"] = (0x1B, "Esc"),
            ["BACKSPACE"] = (0x08, "Backspace"),
            ["DELETE"] = (0x2E, "Delete"),
            ["DEL"] = (0x2E, "Delete"),
            ["INSERT"] = (0x2D, "Insert"),
            ["INS"] = (0x2D, "Insert"),
            ["HOME"] = (0x24, "Home"),
            ["END"] = (0x23, "End"),
            ["PAGEUP"] = (0x21, "PageUp"),
            ["PGUP"] = (0x21, "PageUp"),
            ["PAGEDOWN"] = (0x22, "PageDown"),
            ["PGDN"] = (0x22, "PageDown"),
            ["UP"] = (0x26, "Up"),
            ["DOWN"] = (0x28, "Down"),
            ["LEFT"] = (0x25, "Left"),
            ["RIGHT"] = (0x27, "Right"),
            ["PAUSE"] = (0x13, "Pause"),
            ["PRINTSCREEN"] = (0x2C, "PrintScreen"),
            ["PRTSC"] = (0x2C, "PrintScreen"),
            ["NUMLOCK"] = (0x90, "NumLock"),
            ["SCROLLLOCK"] = (0x91, "ScrollLock"),
            ["CAPSLOCK"] = (0x14, "CapsLock"),
            ["MINUS"] = (0xBD, "-"),
            ["DASH"] = (0xBD, "-"),
            ["PLUS"] = (0xBB, "+"),
            ["EQUALS"] = (0xBB, "="),
            ["COMMA"] = (0xBC, ","),
            ["PERIOD"] = (0xBE, "."),
            ["DOT"] = (0xBE, "."),
            ["SLASH"] = (0xBF, "/"),
            ["SEMICOLON"] = (0xBA, ";"),
            ["QUOTE"] = (0xDE, "'"),
            ["LEFTBRACKET"] = (0xDB, "["),
            ["RIGHTBRACKET"] = (0xDD, "]"),
            ["BACKSLASH"] = (0xDC, "\\"),
            ["TILDE"] = (0xC0, "`"),
            ["GRAVE"] = (0xC0, "`")
        };

    private static readonly Dictionary<char, (ushort Vk, string Name)> SymbolKeys = new()
    {
        ['-'] = (0xBD, "-"),
        ['='] = (0xBB, "="),
        [','] = (0xBC, ","),
        ['.'] = (0xBE, "."),
        ['/'] = (0xBF, "/"),
        [';'] = (0xBA, ";"),
        ['\''] = (0xDE, "'"),
        ['['] = (0xDB, "["),
        [']'] = (0xDD, "]"),
        ['\\'] = (0xDC, "\\"),
        ['`'] = (0xC0, "`")
    };

    private HotkeyDefinition(HotkeyModifiers modifiers, ushort virtualKey, string keyName)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        KeyName = keyName;
        DisplayName = BuildDisplayName(modifiers, keyName);
    }

    public HotkeyModifiers Modifiers { get; }
    public ushort VirtualKey { get; }
    public string KeyName { get; }
    public string DisplayName { get; }

    public static HotkeyDefinition Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Пустое сочетание клавиш.");
        }

        var parts = text
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            throw new ArgumentException(
                $"«{text}»: нужно указать модификатор и клавишу, например Ctrl+Shift+M.");
        }

        var modifiers = HotkeyModifiers.None;
        ushort virtualKey = 0;
        string? keyName = null;

        foreach (var part in parts)
        {
            if (TryParseModifier(part, out var modifier))
            {
                modifiers |= modifier;
                continue;
            }

            if (virtualKey != 0)
            {
                throw new ArgumentException($"«{text}»: указано больше одной клавиши.");
            }

            (virtualKey, keyName) = ParseKey(part);
        }

        if (modifiers == HotkeyModifiers.None)
        {
            throw new ArgumentException(
                $"«{text}»: нужен хотя бы один модификатор (Ctrl, Shift, Alt или Win).");
        }

        if (virtualKey == 0 || keyName is null)
        {
            throw new ArgumentException($"«{text}»: не найдена клавиша.");
        }

        return new HotkeyDefinition(modifiers, virtualKey, keyName);
    }

    /// <summary>
    /// Отправляет сочетание в систему. Возвращает false, если SendInput заблокирован.
    /// </summary>
    public bool Send(out string? error)
    {
        error = null;

        var modifierKeys = GetModifierKeys(Modifiers);
        var downInputs = new List<Input>(modifierKeys.Length + 1);
        var upInputs = new List<Input>(modifierKeys.Length + 1);

        foreach (var modifier in modifierKeys)
        {
            downInputs.Add(CreateKeyInput(modifier, keyUp: false));
        }

        downInputs.Add(CreateKeyInput(VirtualKey, keyUp: false));
        upInputs.Add(CreateKeyInput(VirtualKey, keyUp: true));

        for (var i = modifierKeys.Length - 1; i >= 0; i--)
        {
            upInputs.Add(CreateKeyInput(modifierKeys[i], keyUp: true));
        }

        if (!SendInputs(downInputs, out error))
        {
            return false;
        }

        // Небольшая пауза, чтобы приложение успело увидеть нажатие.
        Thread.Sleep(30);

        // Если отпускание не удалось, пробуем ещё раз, чтобы не оставить
        // модификаторы зажатыми.
        if (!SendInputs(upInputs, out error) && !SendInputs(upInputs, out error))
        {
            return false;
        }

        return true;
    }

    private static bool SendInputs(List<Input> inputs, out string? error)
    {
        error = null;
        var array = inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<Input>());
        if (sent == array.Length)
        {
            return true;
        }

        var code = Marshal.GetLastWin32Error();
        error = code == 0
            ? "Windows отклонил ввод (возможно, окно защищено или не в фокусе)."
            : $"SendInput вернул ошибку {code}.";
        return false;
    }

    private static ushort[] GetModifierKeys(HotkeyModifiers modifiers)
    {
        var keys = new List<ushort>(4);

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            keys.Add(VkLeftControl);
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            keys.Add(VkLeftAlt);
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            keys.Add(VkLeftShift);
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            keys.Add(VkLeftWin);
        }

        return keys.ToArray();
    }

    private static bool TryParseModifier(string text, out HotkeyModifiers modifier)
    {
        switch (text.ToLowerInvariant())
        {
            case "ctrl":
            case "control":
                modifier = HotkeyModifiers.Control;
                return true;

            case "shift":
                modifier = HotkeyModifiers.Shift;
                return true;

            case "alt":
                modifier = HotkeyModifiers.Alt;
                return true;

            case "win":
            case "windows":
            case "meta":
                modifier = HotkeyModifiers.Win;
                return true;

            default:
                modifier = HotkeyModifiers.None;
                return false;
        }
    }

    private static (ushort Vk, string Name) ParseKey(string text)
    {
        if (text.Length == 1)
        {
            var ch = char.ToUpperInvariant(text[0]);

            if (ch is >= 'A' and <= 'Z')
            {
                return ((ushort)ch, ch.ToString());
            }

            if (ch is >= '0' and <= '9')
            {
                return ((ushort)ch, ch.ToString());
            }

            if (SymbolKeys.TryGetValue(text[0], out var symbol))
            {
                return symbol;
            }
        }

        if (text.Length is >= 2 and <= 3 &&
            (text[0] == 'F' || text[0] == 'f') &&
            int.TryParse(text.AsSpan(1), out var functionNumber) &&
            functionNumber is >= 1 and <= 24)
        {
            return ((ushort)(0x70 + functionNumber - 1), $"F{functionNumber}");
        }

        if (NamedKeys.TryGetValue(text, out var named))
        {
            return named;
        }

        throw new ArgumentException(
            $"Неизвестная клавиша «{text}». Поддерживаются A–Z, 0–9, F1–F24 и основные клавиши.");
    }

    private static string BuildDisplayName(HotkeyModifiers modifiers, string keyName)
    {
        var parts = new List<string>(5);

        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private static Input CreateKeyInput(ushort virtualKey, bool keyUp)
    {
        var flags = keyUp ? KeyEventKeyUp : 0u;
        if (IsExtendedKey(virtualKey))
        {
            flags |= KeyEventExtendedKey;
        }

        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static bool IsExtendedKey(ushort virtualKey)
    {
        return virtualKey is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
            or 0x2D or 0x2E or 0x2C or 0x90 or 0xA3 or 0xA5;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}

/// <summary>
/// Проверяет, свободна ли горячая клавиша. Если её уже кто-то держит
/// (например, Discord с включёнными глобальными клавишами), RegisterHotKey
/// завершится с ERROR_HOTKEY_ALREADY_REGISTERED.
/// </summary>
public static class HotkeyProbe
{
    private const int ProbeId = 0xBEEF;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    public static HotkeyProbeResult Probe(HotkeyDefinition hotkey, out int errorCode)
    {
        errorCode = 0;

        if (RegisterHotKey(IntPtr.Zero, ProbeId, (uint)hotkey.Modifiers, hotkey.VirtualKey))
        {
            UnregisterHotKey(IntPtr.Zero, ProbeId);
            return HotkeyProbeResult.Free;
        }

        errorCode = Marshal.GetLastWin32Error();
        return errorCode == ErrorHotkeyAlreadyRegistered
            ? HotkeyProbeResult.Taken
            : HotkeyProbeResult.Error;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
