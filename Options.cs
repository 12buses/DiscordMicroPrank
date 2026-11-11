using System.Globalization;

namespace MicPrank;

public enum PrankMode
{
    Hotkey,
    System
}

public sealed class Options
{
    public PrankMode Mode { get; private set; } = PrankMode.Hotkey;
    public string Hotkey { get; private set; } = "Ctrl+Shift+M";
    public bool FocusDiscord { get; private set; }
    public bool OnlyInCall { get; private set; }
    public bool Once { get; private set; }
    public bool DryRun { get; private set; }
    public bool Diagnose { get; private set; }
    public bool TestHotkey { get; private set; }
    public bool TestFocus { get; private set; }
    public bool Help { get; private set; }
    public bool Version { get; private set; }
    public bool Verbose { get; private set; }
    public string? Device { get; private set; }

    public double MinDelaySeconds { get; private set; } = 45;
    public double MaxDelaySeconds { get; private set; } = 150;
    public double MinDurationSeconds { get; private set; } = 2;
    public double MaxDurationSeconds { get; private set; } = 5;

    public static Options Parse(string[] args)
    {
        var options = new Options();

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var (name, inlineValue) = SplitOption(raw);

            string TakeValue()
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }

                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Для параметра {name} нужно значение.");
                }

                return args[++i];
            }

            switch (name.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                    options.Help = true;
                    break;

                case "--version":
                    options.Version = true;
                    break;

                case "--mode":
                {
                    var value = TakeValue().ToLowerInvariant();
                    options.Mode = value switch
                    {
                        "hotkey" or "hot" or "discord" => PrankMode.Hotkey,
                        "system" or "sys" or "device" => PrankMode.System,
                        _ => throw new ArgumentException(
                            $"Неизвестный режим «{value}». Используйте hotkey или system.")
                    };
                    break;
                }

                case "--system":
                    options.Mode = PrankMode.System;
                    break;

                case "--hotkey":
                    options.Hotkey = TakeValue();
                    break;

                case "--focus-discord":
                case "--focus":
                    options.FocusDiscord = true;
                    break;

                case "--only-in-call":
                case "--only-call":
                    options.OnlyInCall = true;
                    break;

                case "--once":
                    options.Once = true;
                    break;

                case "--dry-run":
                case "--dry":
                    options.DryRun = true;
                    break;

                case "--diagnose":
                case "--diag":
                    options.Diagnose = true;
                    break;

                case "--test-hotkey":
                case "--test":
                    options.TestHotkey = true;
                    options.Once = true;
                    break;

                case "--test-focus":
                    options.TestFocus = true;
                    options.Once = true;
                    break;

                case "--verbose":
                case "-v":
                    options.Verbose = true;
                    break;

                case "--device":
                    options.Device = TakeValue();
                    break;

                case "--min-delay":
                    options.MinDelaySeconds = ParseSeconds(name, TakeValue());
                    break;

                case "--max-delay":
                    options.MaxDelaySeconds = ParseSeconds(name, TakeValue());
                    break;

                case "--duration":
                {
                    var duration = ParseSeconds(name, TakeValue());
                    options.MinDurationSeconds = duration;
                    options.MaxDurationSeconds = duration;
                    break;
                }

                case "--min-duration":
                    options.MinDurationSeconds = ParseSeconds(name, TakeValue());
                    break;

                case "--max-duration":
                    options.MaxDurationSeconds = ParseSeconds(name, TakeValue());
                    break;

                default:
                    throw new ArgumentException(
                        $"Неизвестный параметр «{raw}». Подсказка: MicPrank --help");
            }
        }

        options.Validate();
        return options;
    }

    private void Validate()
    {
        if (MinDelaySeconds < 0 || MaxDelaySeconds < 0)
        {
            throw new ArgumentException("Пауза между приколами не может быть отрицательной.");
        }

        if (MaxDelaySeconds < MinDelaySeconds)
        {
            throw new ArgumentException("--max-delay не может быть меньше --min-delay.");
        }

        if (MinDurationSeconds <= 0 || MaxDurationSeconds <= 0)
        {
            throw new ArgumentException("Длительность прикола должна быть больше нуля.");
        }

        if (MaxDurationSeconds < MinDurationSeconds)
        {
            throw new ArgumentException("--max-duration не может быть меньше --min-duration.");
        }

        if (MinDurationSeconds > 120 || MaxDurationSeconds > 120)
        {
            throw new ArgumentException("Слишком большая длительность. Максимум — 120 секунд.");
        }

        if (TestHotkey && Mode == PrankMode.System)
        {
            throw new ArgumentException("--test-hotkey работает только в режиме hotkey.");
        }

        if (TestFocus && TestHotkey)
        {
            throw new ArgumentException("--test-focus и --test-hotkey нельзя использовать вместе.");
        }
    }

    private static (string Name, string? Value) SplitOption(string raw)
    {
        if (!raw.StartsWith("--", StringComparison.Ordinal))
        {
            return (raw, null);
        }

        var equals = raw.IndexOf('=');
        if (equals < 0)
        {
            return (raw, null);
        }

        return (raw[..equals], raw[(equals + 1)..]);
    }

    private static double ParseSeconds(string name, string value)
    {
        var normalized = value.Trim().Replace(',', '.');
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            throw new ArgumentException($"Не удалось разобрать число «{value}» для {name}.");
        }

        return seconds;
    }

    public static string HelpText =>
        """
        MicPrank — случайно выключает микрофон в Discord на несколько секунд.

        ИСПОЛЬЗОВАНИЕ
          MicPrank [параметры]

        РЕЖИМЫ
          --mode hotkey          Отправлять горячую клавишу Discord (по умолчанию).
          --mode system          Выключать устройство записи Windows.
          --system               То же, что --mode system.

          В режиме hotkey приложение нажимает за вас горячую клавишу Discord.
          Это влияет только на Discord, но требует, чтобы горячая клавиша
          работала глобально (или используйте --focus-discord).
          В режиме system выключается микрофон на уровне Windows, поэтому
          он замолкает во всех приложениях, которые его используют.

        ПАРАМЕТРЫ
          --hotkey <сочетание>      Горячая клавиша Discord (по умолчанию Ctrl+Shift+M).
          --focus-discord           Кратко переключаться на Discord перед нажатием.
                                    Нужно, если глобальные горячие клавиши Discord
                                    выключены. Окно Discord будет появляться на миг.
          --only-in-call            Прикалываться только при активном голосовом канале.
          --min-delay <сек>         Минимальная пауза между приколами (по умолчанию 45).
          --max-delay <сек>         Максимальная пауза (по умолчанию 150).
          --duration <сек>          Фиксированная длительность, например 3.
          --min-duration <сек>      Минимальная длительность (по умолчанию 2).
          --max-duration <сек>      Максимальная длительность (по умолчанию 5).
          --device <часть имени>    Устройство записи для режима system.
          --once                    Один прикол и выход.
          --dry-run                 Показать, что было бы, не выключая микрофон.
          --test-hotkey             Проверить горячую клавишу: выключить на 3 секунды
                                    и включить обратно.
          --test-focus              Проверить переключение фокуса на Discord
                                    (без нажатия горячей клавиши).
          --diagnose                Диагностика Discord, устройств и горячей клавиши.
          --verbose                 Подробный вывод.
          -h, --help                Эта справка.
          --version                 Версия программы.

        ПРИМЕРЫ
          MicPrank
              Бесконечный режим с горячей клавишей Discord.

          MicPrank --focus-discord
              То же, но с кратким переключением на окно Discord.

          MicPrank --mode system --device "Realtek" --only-in-call
              Выключать микрофон Realtek только во время голосового канала.

          MicPrank --once --min-delay 5 --max-delay 5 --duration 2
              Один прикол через 5 секунд, микрофон выключен 2 секунды.

          MicPrank --diagnose
              Показать, что видит программа.

        ВАЖНО
          • Начинайте с включённого микрофона. Горячая клавиша Discord —
            это переключатель: если вы уже в муте, прикол, наоборот, включит вас.
          • В режиме hotkey программа не может прочитать состояние мута Discord.
            Не трогайте микрофон вручную во время прикола.
          • Глобальная горячая клавиша Discord включается в Discord:
            Настройки пользователя → Горячие клавиши → глобальные клавиши.
          • Не используйте программу в важных звонках. Это шутка.
        """;
}
