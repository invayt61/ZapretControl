# Уведомления о сторонних проектах

Zapret Control является неофициальным графическим интерфейсом. Перечисленные ниже проекты принадлежат их авторам. Упоминание не означает поддержку или одобрение со стороны авторов.

## zapret-discord-youtube

- Репозиторий: https://github.com/Flowseal/zapret-discord-youtube
- Автор производной сборки: Flowseal
- Лицензия: MIT
- Copyright (c) 2024–2026 Flowseal
- Файлы ядра и стратегий находятся в `Windows/engine`.
- Полное уведомление сохранено в `Windows/engine/LICENSE.txt` и включается в собранный пакет.

## zapret

- Репозиторий: https://github.com/bol-van/zapret
- Автор исходного проекта: bol-van
- Лицензия: MIT
- Copyright (c) 2016–2026 bol-van
- Используется через двоичные файлы в составе `zapret-discord-youtube`.

## WinDivert

- Репозиторий: https://github.com/basil00/WinDivert
- Лицензия на выбор: LGPLv3 или GPLv2.
- Немодифицированные двоичные файлы входят в состав ядра zapret.
- Исходный код и условия лицензий доступны в репозитории проекта. Соответствующее уведомление также сохранено в `Windows/engine/LICENSE.txt`.

## TG WS Proxy

- Репозиторий: https://github.com/Flowseal/tg-ws-proxy
- Автор: Flowseal
- Лицензия: MIT
- Copyright (c) 2026 Flowseal
- Полный текст лицензии: https://github.com/Flowseal/tg-ws-proxy/blob/main/LICENSE
- Zapret Control использует официальный CLI-движок `proxy.tg_ws_proxy` из исходного кода проекта. Отдельный entrypoint в `integration/TgWsProxyHeadless.py` удаляет только GUI/tray-оболочку и не изменяет алгоритмы прокси.
- Двоичный файл собирается в GitHub Actions из конкретного официального релиза Flowseal. Ссылка на использованный релиз указывается в описании каждой сборки движка.

## Telegram

Telegram Desktop и название Telegram принадлежат соответствующим правообладателям. Zapret Control не является продуктом Telegram и не связан с его разработчиками.
