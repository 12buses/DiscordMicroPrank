using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;

namespace MicPrank;

internal static class Diagnostics
{
    public static int Run(Options options)
    {
        Log.Info("=== Диагностика MicPrank ===");
        Log.Info($"ОС: {RuntimeInformation.OSDescription}");
        Log.Info($"Платформа: {RuntimeInformation.ProcessArchitecture}, .NET {Environment.Version}");

        var discordProcessIds = DiscordHelper.GetProcessIds();
        if (discordProcessIds.Count == 0)
        {
            Log.Warn("Discord не запущен.");
        }
        else
        {
            Log.Info($"Discord запущен. Процессов: {discordProcessIds.Count}.");
            var window = DiscordWindow.FindMainWindow();
            Log.Info(window == IntPtr.Zero
                ? "Главное окно Discord: не найдено (возможно, свёрнуто в трей)."
                : "Главное окно Discord: найдено.");
        }

        var audio = new AudioController();

        if (discordProcessIds.Count > 0)
        {
            var inCall = audio.HasDiscordVoiceSession(discordProcessIds);
            Log.Info($"Голосовая сессия Discord (эвристика по аудиосессиям): {(inCall ? "да" : "нет")}");
        }

        HotkeyDefinition? hotkey = null;
        try
        {
            hotkey = HotkeyDefinition.Parse(options.Hotkey);
        }
        catch (Exception ex)
        {
            Log.Error($"Не удалось разобрать горячую клавишу «{options.Hotkey}»: {ex.Message}");
        }

        if (hotkey is not null)
        {
            var probe = HotkeyProbe.Probe(hotkey, out var errorCode);
            switch (probe)
            {
                case HotkeyProbeResult.Taken:
                    Log.Info(
                        $"Горячая клавиша {hotkey.DisplayName}: занята другим приложением " +
                        "(скорее всего, Discord). Глобальный хоткей должен работать.");
                    break;

                case HotkeyProbeResult.Free:
                    Log.Warn(
                        $"Горячая клавиша {hotkey.DisplayName}: свободна. " +
                        "Значит, Discord не зарегистрировал её глобально. " +
                        "Включите глобальные горячие клавиши в Discord или используйте --focus-discord.");
                    break;

                case HotkeyProbeResult.Error:
                    Log.Warn(
                        $"Не удалось проверить горячую клавишу {hotkey.DisplayName} " +
                        $"(код Windows {errorCode}).");
                    break;
            }
        }

        Log.Info("");
        Log.Info("Устройства записи:");
        var captureDevices = audio.GetCaptureDevices();
        if (captureDevices.Count == 0)
        {
            Log.Warn("Устройства записи не найдены.");
        }

        foreach (var device in captureDevices)
        {
            var defaultMark = device.IsDefaultCommunications ? " [по умолчанию для связи]" : "";
            var muteMark = device.IsMuted ? "MUTE" : "не выключен";
            Log.Info(
                $"  • {device.FriendlyName}{defaultMark}");
            Log.Info(
                $"    Windows mute: {muteMark}, громкость: {device.Volume * 100:0}%");

            if (device.Sessions.Count == 0)
            {
                Log.Debug("    Сессий нет.");
                continue;
            }

            foreach (var session in device.Sessions)
            {
                Log.Debug(
                    $"    {session.ProcessName} (PID {session.ProcessId}) — {Translate(session.State)}");
            }
        }

        if (discordProcessIds.Count > 0)
        {
            Log.Info("");
            Log.Info("Устройства воспроизведения с сессиями Discord:");
            var renderDevices = audio.GetRenderDevicesWithDiscordSessions(discordProcessIds);
            if (renderDevices.Count == 0)
            {
                Log.Debug("  Нет устройств с сессиями Discord.");
            }

            foreach (var device in renderDevices)
            {
                var defaultMark = device.IsDefaultCommunications ? " [по умолчанию для связи]" : "";
                Log.Info($"  • {device.FriendlyName}{defaultMark}");
                foreach (var session in device.Sessions)
                {
                    Log.Debug(
                        $"    {session.ProcessName} (PID {session.ProcessId}) — {Translate(session.State)}");
                }
            }
        }

        Log.Info("");
        Log.Info("Выбор устройства для режима system:");

        if (!string.IsNullOrWhiteSpace(options.Device))
        {
            var byName = audio.FindCaptureDeviceByName(options.Device);
            if (byName is null)
            {
                Log.Warn($"Устройство, содержащее «{options.Device}», не найдено.");
            }
            else
            {
                Log.Info($"  По имени «{options.Device}»: {byName.FriendlyName}");
            }
        }
        else
        {
            var discordDevice = audio.FindDiscordCaptureDevice(discordProcessIds);
            if (discordDevice is not null)
            {
                Log.Info(
                    $"  Устройство Discord: {discordDevice.FriendlyName} " +
                    (discordDevice.IsDefaultCommunications ? "(по умолчанию для связи)" : ""));
            }
            else
            {
                var fallback = audio.GetDefaultCommunicationsCapture();
                Log.Info(fallback is null
                    ? "  Discord-устройство не найдено, устройства по умолчанию тоже нет."
                    : $"  Discord-устройство не найдено; будет использовано по умолчанию: {fallback.FriendlyName}");
            }
        }

        Log.Info("");
        Log.Info("Готово.");
        return 0;
    }

    private static string Translate(AudioSessionState state)
    {
        return state switch
        {
            AudioSessionState.AudioSessionStateActive => "активна",
            AudioSessionState.AudioSessionStateInactive => "неактивна",
            AudioSessionState.AudioSessionStateExpired => "завершена",
            _ => state.ToString()
        };
    }
}
