MicPrank

Консольная утилита для Windows: на несколько секунд выключает микрофон в Discord. По умолчанию — через глобальную горячую клавишу Discord Ctrl+Shift+M; альтернативно — через устройство записи Windows (WASAPI).

    Розыгрыш. Начинайте с включённого микрофона — горячая клавиша работает как переключатель.

Требования

    Windows 10/11 x64, установленный Discord.

    .NET 9 Runtime или self-contained .exe из dist\self-contained.

    Для hotkey-режима: глобальные горячие клавиши Discord включены, либо флаг --focus-discord.

    Права MicPrank и Discord должны совпадать.

Быстрый старт
powershell

.\MicPrank.exe --diagnose              # проверить окружение
.\MicPrank.exe --once --dry-run        # прогон без выключения
.\MicPrank.exe                         # рабочий режим

В Discord: Настройки → Горячие клавиши → включить глобальные, Toggle Mute = Ctrl+Shift+M.

Если глобальные клавиши недоступны:
powershell

.\MicPrank.exe --focus-discord
.\MicPrank.exe --mode system --device "Realtek" --only-in-call

Параметры
Параметр	Описание
--mode hotkey | --mode system	Способ выключения (по умолчанию hotkey).
--hotkey <сочетание>	По умолчанию Ctrl+Shift+M.
--focus-discord	Переключать фокус на Discord перед нажатием.
--only-in-call	Только при активном голосовом канале.
--min-delay / --max-delay <сек>	Пауза между срабатываниями (45/150).
--duration / --min-duration / --max-duration <сек>	Длительность (2–5).
--device <часть имени>	Устройство записи для system.
--once	Одно срабатывание и выход.
--dry-run	Без реального выключения.
--test-hotkey / --test-focus	Проверка клавиши / фокуса.
--diagnose	Диагностика.
--verbose	Подробный вывод.
-h / --help / --version	Справка / версия.

Числа — с точкой или запятой: 2.5 = 2,5.
Сборка
powershell

.\build.ps1

Создаёт dist\framework-dependent\ (нужен .NET 9 Runtime) и dist\self-contained\ (автономный).
Ограничения

    Состояние мута Discord не отслеживается — если вы уже в муте, программа вас включит.

    --focus-discord переключает активное окно (может свернуть игру).

    system выключает микрофон для всех приложений, не только для Discord. Если процесс убить через Task Manager во время срабатывания — устройство может остаться выключенным.

    Не используйте в рабочих и важных звонках.
