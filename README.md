# Zapret Control

<img src="Windows/Assets/app.png" width="128" height="128" alt="Zapret Control" />

Неофициальный графический интерфейс Windows для [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) и [TG WS Proxy](https://github.com/Flowseal/tg-ws-proxy). Помогает запускать обход ограничений для YouTube, Discord и других сервисов, а также подключать локальный MTProto-прокси для Telegram Desktop.

## Возможности

- Запуск и остановка выбранной стратегии zapret.
- Отдельный запуск Telegram-прокси или совместный запуск кнопкой **«Запустить всё»**.
- Подключение Telegram Desktop через готовую `tg://proxy` ссылку.
- Пользовательские списки доменов, IP-адресов и исключений.
- Настройка игрового фильтра и IPSet.
- Проверка доступности YouTube и Discord и встроенная диагностика.
- Автоматическая проверка официальных релизов GitHub каждые 6 часов.
- Необязательная автоматическая установка обновлений с обязательной проверкой SHA-256.
- Сохранение пользовательских списков, настроек и секрета прокси при обновлении.
- Сворачивание в системный трей при закрытии окна. Полный выход выполняется через контекстное меню значка.

## Требования

- Windows 10 или Windows 11 x64.
- Права администратора для запуска zapret и управления системной службой.
- Telegram Desktop для использования MTProto-прокси.

## Установка и запуск

1. Скачайте `ZapretControl-win-x64.zip` со страницы [Releases](../../releases/latest).
2. Полностью распакуйте архив в отдельную папку. Не запускайте приложение внутри ZIP.
3. Запустите `ZapretControl.exe` и разрешите запуск от имени администратора.
4. Нажмите **«Запустить всё»** или включите обход и Telegram-прокси по отдельности.
5. Для Telegram после запуска прокси нажмите **«Подключить Telegram»** и подтвердите добавление прокси в Telegram Desktop.

При первом включении Telegram-прокси приложение скачивает официальный `TgWsProxy_windows.exe` из релиза Flowseal и проверяет его SHA-256.

### Закрытие и трей

Крестик в окне и `Alt+F4` скрывают приложение в системный трей, не останавливая запущенные компоненты. Чтобы восстановить окно, дважды нажмите на значок или выберите **«Открыть Zapret Control»**. Для завершения интерфейса нажмите на значок правой кнопкой и выберите **«Закрыть полностью»**.

## Обновления

На странице **«Обновления»** можно отдельно проверить и установить новые версии ядра zapret и TG WS Proxy. Автоматическая проверка и автоустановка включаются и выключаются переключателями; изменения сохраняются сразу.

Загружаются только файлы из официальных репозиториев Flowseal. Обновление отменяется, если GitHub не предоставил SHA-256 или фактический хэш не совпал.

## Сборка

Нужен [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) с поддержкой рабочей нагрузки Windows Desktop.

```powershell
cd Windows
dotnet build ZapretControl.csproj -c Release
./build.ps1
```

Готовые файлы создаются в `Windows/ZapretControl`, а архив — `Windows/ZapretControl-win-x64.zip`.

### Проверки

```powershell
dotnet bin/Release/net6.0-windows/ZapretControl.dll --self-test
dotnet bin/Release/net6.0-windows/ZapretControl.dll --verify-update
dotnet bin/Release/net6.0-windows/ZapretControl.dll --verify-ui
dotnet bin/Release/net6.0-windows/ZapretControl.dll --verify-tray
```

- `--self-test` проверяет стратегии и доступность метаданных релизов.
- `--verify-update` дополнительно скачивает релизы во временную папку, проверяет установку и запускает Telegram-прокси на тестовом локальном порту.
- `--verify-ui` создаёт снимки страниц во временной папке для визуальной проверки.
- `--verify-tray` проверяет скрытие, восстановление и полный выход.

## Структура

- `Windows/Services` — запуск процессов, настройки, обновления и диагностика.
- `Windows/ViewModels` — команды и состояние интерфейса.
- `Windows/Models` — настройки и модели данных.
- `Windows/engine` — ядро `zapret-discord-youtube`, стратегии и списки.
- `Windows/Assets` — иконка приложения.

## Первоисточники и лицензии

Проект является независимым неофициальным интерфейсом и не связан с Flowseal или Telegram. Основная работа по обходу и MTProto-прокси выполняется сторонними проектами:

- [Flowseal/zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — ядро и стратегии zapret.
- [Flowseal/tg-ws-proxy](https://github.com/Flowseal/tg-ws-proxy) — локальный MTProto-прокси для Telegram Desktop.
- [bol-van/zapret](https://github.com/bol-van/zapret) — исходный проект zapret.
- [basil00/WinDivert](https://github.com/basil00/WinDivert) — драйвер перехвата пакетов.

Подробности и лицензионные уведомления находятся в [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) и `Windows/engine/LICENSE.txt`.
