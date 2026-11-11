using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MicPrank;

public sealed record AudioSessionInfo(int ProcessId, string ProcessName, AudioSessionState State);

public sealed record CaptureDeviceInfo(
    string Id,
    string FriendlyName,
    bool IsDefaultCommunications,
    bool IsMuted,
    float Volume,
    IReadOnlyList<AudioSessionInfo> Sessions);

public sealed record RenderDeviceInfo(
    string Id,
    string FriendlyName,
    bool IsDefaultCommunications,
    IReadOnlyList<AudioSessionInfo> Sessions);

public sealed record DeviceSelection(
    string Id,
    string FriendlyName,
    bool IsDefaultCommunications,
    bool FromDiscordSession);

/// <summary>
/// Работа с устройствами записи Windows через WASAPI/NAudio.
/// </summary>
public sealed class AudioController
{
    public IReadOnlyList<CaptureDeviceInfo> GetCaptureDevices()
    {
        var result = new List<CaptureDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = GetDefaultId(enumerator, DataFlow.Capture);

            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    result.Add(new CaptureDeviceInfo(
                        device.ID,
                        device.FriendlyName,
                        string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                        GetMute(device),
                        GetVolume(device),
                        ReadSessions(device)));
                }
            }
        }
        catch
        {
            // Служба аудио может быть недоступна или устройство может исчезнуть.
        }

        return result;
    }

    public IReadOnlyList<RenderDeviceInfo> GetRenderDevicesWithDiscordSessions(HashSet<int> discordProcessIds)
    {
        var result = new List<RenderDeviceInfo>();

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = GetDefaultId(enumerator, DataFlow.Render);

            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    var sessions = ReadSessions(device)
                        .Where(session => discordProcessIds.Contains(session.ProcessId))
                        .ToList();

                    if (sessions.Count == 0)
                    {
                        continue;
                    }

                    result.Add(new RenderDeviceInfo(
                        device.ID,
                        device.FriendlyName,
                        string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                        sessions));
                }
            }
        }
        catch
        {
            // Служба аудио может быть недоступна.
        }

        return result;
    }

    /// <summary>
    /// Ищет устройство записи, на котором у Discord есть аудиосессия.
    /// Сначала предпочитает активную сессию, затем — любую существующую.
    /// </summary>
    public DeviceSelection? FindDiscordCaptureDevice(HashSet<int> discordProcessIds)
    {
        if (discordProcessIds.Count == 0)
        {
            return null;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = GetDefaultId(enumerator, DataFlow.Capture);
            DeviceSelection? inactiveMatch = null;

            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    var discordSessions = ReadSessions(device)
                        .Where(session => discordProcessIds.Contains(session.ProcessId))
                        .ToList();

                    if (discordSessions.Count == 0)
                    {
                        continue;
                    }

                    var selection = new DeviceSelection(
                        device.ID,
                        device.FriendlyName,
                        string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                        FromDiscordSession: true);

                    if (discordSessions.Any(session => session.State == AudioSessionState.AudioSessionStateActive))
                    {
                        return selection;
                    }

                    inactiveMatch ??= selection;
                }
            }

            return inactiveMatch;
        }
        catch
        {
            return null;
        }
    }

    public DeviceSelection? FindCaptureDeviceByName(string namePart)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var defaultId = GetDefaultId(enumerator, DataFlow.Capture);

            using var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in devices)
            {
                using (device)
                {
                    if (!device.FriendlyName.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return new DeviceSelection(
                        device.ID,
                        device.FriendlyName,
                        string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase),
                        FromDiscordSession: false);
                }
            }
        }
        catch
        {
            // Служба аудио может быть недоступна.
        }

        return null;
    }

    public DeviceSelection? GetDefaultCommunicationsCapture()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return new DeviceSelection(
                device.ID,
                device.FriendlyName,
                IsDefaultCommunications: true,
                FromDiscordSession: false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Включает или выключает mute на устройстве. Возвращает предыдущее состояние.
    /// </summary>
    public bool TrySetMute(string deviceId, bool mute, out bool previousMute, out string? error)
    {
        previousMute = false;
        error = null;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(deviceId);
            var volume = device.AudioEndpointVolume;

            previousMute = volume.Mute;
            volume.Mute = mute;

            if (volume.Mute != mute)
            {
                error = "Windows не применил новое состояние mute.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Эвристика «Discord в голосовом канале»:
    /// любая живая сессия Discord на устройстве записи или активная сессия
    /// на устройстве воспроизведения. Это ловит и случай, когда микрофон
    /// в Discord уже выключен, но входящий звук ещё идёт.
    /// </summary>
    public bool HasDiscordVoiceSession(HashSet<int> discordProcessIds)
    {
        if (discordProcessIds.Count == 0)
        {
            return false;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();

            using (var captureDevices =
                   enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                foreach (var device in captureDevices)
                {
                    using (device)
                    {
                        if (ReadSessions(device).Any(session =>
                                session.State != AudioSessionState.AudioSessionStateExpired &&
                                discordProcessIds.Contains(session.ProcessId)))
                        {
                            return true;
                        }
                    }
                }
            }

            using (var renderDevices =
                   enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                foreach (var device in renderDevices)
                {
                    using (device)
                    {
                        if (ReadSessions(device).Any(session =>
                                session.State == AudioSessionState.AudioSessionStateActive &&
                                discordProcessIds.Contains(session.ProcessId)))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
            // Служба аудио может быть недоступна.
        }

        return false;
    }

    private static string? GetDefaultId(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(flow, Role.Communications);
            return device.ID;
        }
        catch
        {
            return null;
        }
    }

    private static bool GetMute(MMDevice device)
    {
        try
        {
            return device.AudioEndpointVolume.Mute;
        }
        catch
        {
            return false;
        }
    }

    private static float GetVolume(MMDevice device)
    {
        try
        {
            return device.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return 0;
        }
    }

    private static List<AudioSessionInfo> ReadSessions(MMDevice device)
    {
        var result = new List<AudioSessionInfo>();

        try
        {
            var sessions = device.AudioSessionManager.Sessions;
            for (var i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];

                var processId = 0;
                try
                {
                    processId = (int)session.GetProcessID;
                }
                catch
                {
                    // Сессия системных звуков или процесс уже завершился.
                }

                if (processId <= 0)
                {
                    // Системные звуки и служебные сессии без процесса.
                    continue;
                }

                result.Add(new AudioSessionInfo(processId, GetProcessName(processId), session.State));
            }
        }
        catch
        {
            // Устройство могло исчезнуть во время перечисления.
        }

        return result;
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return "?";
        }
    }
}
