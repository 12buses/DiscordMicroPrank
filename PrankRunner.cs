using System.Globalization;

namespace MicPrank;

internal sealed class PrankRunner
{
    private readonly Options _options;
    private readonly HotkeyDefinition _hotkey;
    private readonly AudioController _audio = new();
    private readonly object _restoreLock = new();

    private string? _systemMutedDeviceId;
    private bool _systemMuteActive;
    private volatile bool _hotkeyMuteActive;

    private bool _warnedNoDiscord;
    private bool _warnedHotkeyFree;

    public PrankRunner(Options options)
    {
        _options = options;
        _hotkey = HotkeyDefinition.Parse(options.Hotkey);
    }

    public async Task<int> RunAsync(CancellationToken token)
    {
        PrintBanner();

        if (!ValidateStartup())
        {
            return 2;
        }

        while (!token.IsCancellationRequested)
        {
            if (_options.OnlyInCall)
            {
                if (!await WaitForVoiceCallAsync(token))
                {
                    break;
                }
            }
            else if (_options.Mode == PrankMode.Hotkey && !DiscordHelper.IsRunning())
            {
                WarnNoDiscordOnce();
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                continue;
            }

            var delay = NextDelay();
            Log.Info($"Следующий прикол через {FormatSeconds(delay)}...");
            await Task.Delay(delay, token);

            if (_options.OnlyInCall && !IsInVoiceCall())
            {
                Log.Info("Голосовой канал уже не активен — пропускаю прикол.");
                continue;
            }

            await DoPrankAsync(token);

            if (_options.Once)
            {
                break;
            }
        }

        return 0;
    }

    public async Task<int> RunTestFocusAsync(CancellationToken token)
    {
        Log.Info("=== Проверка фокусировки Discord ===");

        if (!DiscordHelper.IsRunning())
        {
            Log.Error("Discord не запущен.");
            return 1;
        }

        var previous = DiscordWindow.GetForegroundWindow();
        if (!DiscordWindow.TryFocusDiscord(out var error))
        {
            Log.Error(error ?? "Не удалось переключить фокус на Discord.");
            return 1;
        }

        Log.Action("Фокус переключён на Discord. Возвращаю через 2 секунды...");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        finally
        {
            if (previous == IntPtr.Zero)
            {
                Log.Warn("Предыдущее активное окно не найдено — фокус не восстановлен.");
            }
            else if (DiscordWindow.TryFocus(previous))
            {
                Log.Info("Фокус возвращён на предыдущее окно.");
            }
            else
            {
                Log.Warn("Не удалось вернуть фокус на предыдущее окно.");
            }
        }

        return 0;
    }

    public async Task<int> RunTestHotkeyAsync(CancellationToken token)
    {
        PrintBanner();

        if (!ValidateStartup())
        {
            return 2;
        }

        if (_options.DryRun)
        {
            Log.Action(
                $"[dry-run] Нажал бы {_hotkey.DisplayName}, подождал 3 секунды и нажал бы ещё раз.");
            return 0;
        }

        Log.Warn("Сейчас микрофон в Discord будет выключен на 3 секунды.");
        Log.Info("Начните с включённого микрофона. Проверка начнётся через 2 секунды...");
        await Task.Delay(TimeSpan.FromSeconds(2), token);

        Log.Action($"Нажимаю {_hotkey.DisplayName} (выключить)...");
        if (!await SendHotkeyAsync(token))
        {
            Log.Error("Не удалось отправить горячую клавишу.");
            return 1;
        }

        _hotkeyMuteActive = true;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }
        catch (OperationCanceledException)
        {
            // Ниже всё равно попробуем включить микрофон.
        }
        finally
        {
            Log.Action($"Нажимаю {_hotkey.DisplayName} (включить)...");
            if (await SendHotkeyAsync(CancellationToken.None))
            {
                _hotkeyMuteActive = false;
            }
            else
            {
                Log.Error("НЕ УДАЛОСЬ ВКЛЮЧИТЬ МИКРОФОН. Включите его вручную в Discord!");
            }
        }

        Log.Info("Проверка завершена. Если микрофон остался выключен — включите его вручную.");
        return 0;
    }

    /// <summary>
    /// Аварийное восстановление при выходе. Вызывается из finally и ProcessExit.
    /// </summary>
    public void RestoreNow()
    {
        try
        {
            RestoreSystemMute();
        }
        catch
        {
            // При выходе стараемся не падать из-за аудио.
        }

        if (!_hotkeyMuteActive)
        {
            return;
        }

        _hotkeyMuteActive = false;

        try
        {
            if (!DiscordHelper.IsRunning())
            {
                return;
            }

            if (_options.FocusDiscord)
            {
                var previous = DiscordWindow.GetForegroundWindow();
                if (!DiscordWindow.TryFocusDiscord(out _))
                {
                    return;
                }

                Thread.Sleep(150);
                _hotkey.Send(out _);
                Thread.Sleep(150);

                if (previous != IntPtr.Zero)
                {
                    DiscordWindow.TryFocus(previous);
                }
            }
            else
            {
                _hotkey.Send(out _);
            }
        }
        catch
        {
            // При выходе остаётся только предупредить пользователя.
        }
    }

    private async Task DoPrankAsync(CancellationToken token)
    {
        var duration = NextDuration();

        if (_options.Verbose)
        {
            Log.Debug($"Длительность прикола: {FormatSeconds(duration)}.");
        }

        if (_options.DryRun)
        {
            Log.Action(
                $"[dry-run] Выключил бы микрофон на {FormatSeconds(duration)} ({DescribeMode()}).");
            return;
        }

        if (_options.Mode == PrankMode.Hotkey)
        {
            await PrankHotkeyAsync(duration, token);
        }
        else
        {
            await PrankSystemAsync(duration, token);
        }
    }

    private async Task PrankHotkeyAsync(TimeSpan duration, CancellationToken token)
    {
        if (!DiscordHelper.IsRunning())
        {
            WarnNoDiscordOnce();
            return;
        }

        if (!_options.FocusDiscord)
        {
            var probe = HotkeyProbe.Probe(_hotkey, out var errorCode);
            if (_options.Verbose)
            {
                Log.Debug($"Проверка {_hotkey.DisplayName}: {probe}, код {errorCode}.");
            }

            if (probe == HotkeyProbeResult.Free)
            {
                WarnHotkeyFreeOnce();
                return;
            }

            if (probe == HotkeyProbeResult.Error)
            {
                Log.Warn(
                    $"Не удалось проверить горячую клавишу (код Windows {errorCode}) — пропускаю.");
                return;
            }
        }

        Log.Action($"Выключаю микрофон в Discord на {FormatSeconds(duration)}...");

        if (!await SendHotkeyAsync(token))
        {
            Log.Warn("Прикол отменён: горячая клавиша не отправлена.");
            return;
        }

        _hotkeyMuteActive = true;

        try
        {
            await Task.Delay(duration, token);
        }
        catch (OperationCanceledException)
        {
            // Ниже включим микрофон обратно.
        }
        finally
        {
            Log.Action("Включаю микрофон обратно...");
            if (await SendHotkeyAsync(CancellationToken.None))
            {
                _hotkeyMuteActive = false;
            }
            else
            {
                Log.Error("НЕ УДАЛОСЬ ВКЛЮЧИТЬ МИКРОФОН. Включите его вручную в Discord!");
            }
        }
    }

    private async Task PrankSystemAsync(TimeSpan duration, CancellationToken token)
    {
        var selection = ResolveSystemDevice(out var reason);
        if (selection is null)
        {
            Log.Warn("Устройство записи не найдено — пропускаю.");
            return;
        }

        if (_options.Verbose)
        {
            Log.Debug(
                $"Устройство для прикола: «{selection.FriendlyName}» ({reason}), id={selection.Id}.");
        }

        if (!_audio.TrySetMute(selection.Id, true, out var wasMuted, out var error))
        {
            Log.Error($"Не удалось выключить «{selection.FriendlyName}»: {error}");
            return;
        }

        if (wasMuted)
        {
            Log.Info(
                $"«{selection.FriendlyName}» уже был выключен в Windows — пропускаю, " +
                "чтобы не включить его обратно.");
            return;
        }

        lock (_restoreLock)
        {
            _systemMutedDeviceId = selection.Id;
            _systemMuteActive = true;
        }

        Log.Action(
            $"Устройство «{selection.FriendlyName}» выключено на {FormatSeconds(duration)} ({reason}).");

        try
        {
            await Task.Delay(duration, token);
        }
        catch (OperationCanceledException)
        {
            // Ниже включим обратно.
        }
        finally
        {
            RestoreSystemMute();
        }
    }

    private async Task<bool> SendHotkeyAsync(CancellationToken token)
    {
        var previousWindow = IntPtr.Zero;
        var focused = false;

        try
        {
            if (_options.FocusDiscord)
            {
                previousWindow = DiscordWindow.GetForegroundWindow();
                if (!DiscordWindow.TryFocusDiscord(out var focusError))
                {
                    Log.Error(focusError ?? "Не удалось переключить фокус на Discord.");
                    return false;
                }

                focused = true;
                await Task.Delay(150, token);
            }

            if (!_hotkey.Send(out var sendError))
            {
                Log.Error("Не удалось отправить горячую клавишу: " + sendError);
                return false;
            }

            // После отправки ожидание нельзя отменять: иначе вызывающий код
            // может не узнать, что клавиша уже была нажата.
            await Task.Delay(200, CancellationToken.None);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (focused && previousWindow != IntPtr.Zero)
            {
                DiscordWindow.TryFocus(previousWindow);
            }
        }
    }

    private void RestoreSystemMute()
    {
        string? deviceId;

        lock (_restoreLock)
        {
            if (!_systemMuteActive || _systemMutedDeviceId is null)
            {
                return;
            }

            deviceId = _systemMutedDeviceId;
            _systemMuteActive = false;
            _systemMutedDeviceId = null;
        }

        if (_audio.TrySetMute(deviceId, false, out _, out var error))
        {
            Log.Action("Микрофон включён обратно.");
        }
        else
        {
            Log.Error(
                $"Не удалось включить микрофон обратно: {error}. Включите его вручную!");
        }
    }

    private async Task<bool> WaitForVoiceCallAsync(CancellationToken token)
    {
        var announced = false;

        while (true)
        {
            token.ThrowIfCancellationRequested();

            var processIds = DiscordHelper.GetProcessIds();
            if (processIds.Count > 0 && _audio.HasDiscordVoiceSession(processIds))
            {
                if (announced)
                {
                    Log.Info("Голосовой канал найден.");
                }

                return true;
            }

            if (!announced)
            {
                Log.Info("Жду активного голосового канала в Discord...");
                announced = true;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }
    }

    private bool IsInVoiceCall()
    {
        var processIds = DiscordHelper.GetProcessIds();
        return processIds.Count > 0 && _audio.HasDiscordVoiceSession(processIds);
    }

    private DeviceSelection? ResolveSystemDevice(out string reason)
    {
        if (!string.IsNullOrWhiteSpace(_options.Device))
        {
            reason = $"по имени «{_options.Device}»";
            return _audio.FindCaptureDeviceByName(_options.Device);
        }

        var processIds = DiscordHelper.GetProcessIds();
        if (processIds.Count > 0)
        {
            var discordDevice = _audio.FindDiscordCaptureDevice(processIds);
            if (discordDevice is not null)
            {
                reason = "устройство, которое использует Discord";
                return discordDevice;
            }
        }

        var fallback = _audio.GetDefaultCommunicationsCapture();
        reason = fallback is null
            ? "устройство не найдено"
            : "устройство по умолчанию для связи; сессия Discord не найдена";
        return fallback;
    }

    private bool ValidateStartup()
    {
        if (_options.Mode == PrankMode.Hotkey)
        {
            if (!DiscordHelper.IsRunning())
            {
                Log.Warn("Discord не запущен. Программа будет ждать его появления.");
            }
            else if (!_options.FocusDiscord)
            {
                var probe = HotkeyProbe.Probe(_hotkey, out var errorCode);

                if (probe == HotkeyProbeResult.Free)
                {
                    Log.Error(
                        $"Глобальная горячая клавиша {_hotkey.DisplayName} не зарегистрирована.");
                    Log.Info("Discord не получит её, пока его окно не в фокусе.");
                    Log.Info("Варианты:");
                    Log.Info("  1) Включите глобальные горячие клавиши в Discord " +
                             "(Настройки пользователя → Горячие клавиши).");
                    Log.Info("  2) Запустите с --focus-discord " +
                             "(окно Discord будет кратко появляться).");
                    Log.Info("  3) Используйте --mode system.");

                    if (!_options.DryRun)
                    {
                        return false;
                    }

                    Log.Info("Продолжаю только потому, что включён --dry-run.");
                }
                else if (probe == HotkeyProbeResult.Error)
                {
                    Log.Warn(
                        $"Не удалось проверить горячую клавишу (код Windows {errorCode}). Продолжаю.");
                }
                else
                {
                    Log.Info(
                        $"Глобальная горячая клавиша {_hotkey.DisplayName} зарегистрирована " +
                        "(вероятно, Discord).");
                }
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(_options.Device))
            {
                var device = _audio.FindCaptureDeviceByName(_options.Device);
                if (device is null)
                {
                    Log.Error(
                        $"Устройство записи, содержащее «{_options.Device}», не найдено.");
                    Log.Info("Запустите --diagnose, чтобы увидеть список устройств.");
                    return false;
                }

                Log.Info($"Устройство: {device.FriendlyName}");
            }
            else
            {
                var selection = ResolveSystemDevice(out var reason);
                if (selection is null)
                {
                    Log.Error("Не найдено устройство записи для режима system.");
                    return false;
                }

                Log.Info($"Устройство: {selection.FriendlyName} ({reason})");
            }
        }

        if (_options.OnlyInCall)
        {
            Log.Info("Приколы только во время активного голосового канала.");
        }

        if (_options.DryRun)
        {
            Log.Info("Режим проверки: микрофон выключаться не будет.");
        }

        if (_options.Once)
        {
            Log.Info("Будет выполнен один прикол.");
        }

        return true;
    }

    private void PrintBanner()
    {
        Log.Info("============================================================");
        Log.Info("  MicPrank — случайный микрофонный пранк для Discord");
        Log.Info("============================================================");
        Log.Info($"Режим: {DescribeMode()}");

        if (_options.Mode == PrankMode.Hotkey)
        {
            Log.Info("Начните с ВКЛЮЧЁННОГО микрофона: горячая клавиша — переключатель.");
        }

        Log.Info("Для выхода нажмите Ctrl+C.");
        Log.Info("");
    }

    private string DescribeMode()
    {
        if (_options.Mode == PrankMode.Hotkey)
        {
            return $"горячая клавиша {_hotkey.DisplayName}" +
                   (_options.FocusDiscord ? " с фокусировкой Discord" : "");
        }

        var selection = ResolveSystemDevice(out _);
        return selection is null
            ? "системное выключение устройства записи"
            : $"устройство «{selection.FriendlyName}»";
    }

    private void WarnNoDiscordOnce()
    {
        if (_warnedNoDiscord)
        {
            return;
        }

        _warnedNoDiscord = true;
        Log.Warn("Discord не запущен. Жду...");
    }

    private void WarnHotkeyFreeOnce()
    {
        if (_warnedHotkeyFree)
        {
            return;
        }

        _warnedHotkeyFree = true;
        Log.Warn(
            $"Глобальная горячая клавиша {_hotkey.DisplayName} не зарегистрирована. " +
            "Приколы пропускаются. Включите глобальные клавиши в Discord или " +
            "перезапустите с --focus-discord.");
    }

    private TimeSpan NextDelay()
    {
        return RandomBetween(_options.MinDelaySeconds, _options.MaxDelaySeconds);
    }

    private TimeSpan NextDuration()
    {
        return RandomBetween(_options.MinDurationSeconds, _options.MaxDurationSeconds);
    }

    private static TimeSpan RandomBetween(double minSeconds, double maxSeconds)
    {
        if (maxSeconds <= minSeconds)
        {
            return TimeSpan.FromSeconds(minSeconds);
        }

        var seconds = minSeconds + Random.Shared.NextDouble() * (maxSeconds - minSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private static string FormatSeconds(TimeSpan value)
    {
        var seconds = value.TotalSeconds;
        return seconds >= 10
            ? seconds.ToString("0", CultureInfo.CurrentCulture) + " с"
            : seconds.ToString("0.0", CultureInfo.CurrentCulture) + " с";
    }
}
