# Плагин ELHART HMI

Плагин 0.6.0 объединяет MCP 0.6.0 и навык `elhart-hmi-projects` с ресурсами.
Нужны Windows, ELHART HMI Soft (проверена 3.0.15569.0), Python 3.11+,
.NET Framework 4.x/x86. Библиотеки производителя берутся из установленного
редактора и не поставляются в плагине. Сервер использует стандартную библиотеку
Python; устанавливать сторонние Python-пакеты для stdio не нужно.

## Установка

```powershell
codex plugin marketplace add IlistTeam/plugin-marketplace
```

В каталоге **IlistTeam Plugins** установите **ELHART HMI**.
Если CLI поддерживает установку: `codex plugin add elhart-hmi@ilistteam`.
Из корня **установленного пакета** выполните, выбрав собственные пути:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-plugin.ps1 -Workspace 'C:\HMI' -PythonPath 'C:\path\to\python.exe'
```

Это примеры; не подставляйте чужие пути. Для нестандартной установки используйте
`-StudioRoot '<каталог ELHART HMI Soft>'`. Выбирайте короткий workspace:
редактор ограничивает длину полного пути проекта (90 символов).
Setup собирает DTO-мост и локальный addin с одним workspace/профилем Elhart;
редактор, симулятор и панель не запускаются. Настройки Python/workspace/Bin
хранятся в `%LOCALAPPDATA%\IlistTeam\elhart-hmi\config.json`, проекты — вне кэша.
Незавершённое обновление занятой DLL требует сохранения/закрытия использующего
её редактора и повторного setup. Не завершайте чужие редакторы принудительно.

Перезапустите соединение MCP. Проверьте `fstudio_health`, профиль Elhart,
`fstudio_capabilities` и `fstudio_list_models` (86 инструментов в tools/list).
Это проверяет DTO-мост и каталог, а не состояние работающего редактора.
`fstudio_visible_state` отдельно проверяет native host и совпадение workspace.
Редактор запускается только явным `fstudio_visible_start` по задаче.
USB-загрузка — отдельное действие по [процедуре панели](panel-download.md).

Если уже подключён сервер `elhart`/FStudio или установлен отдельный
`elhart-hmi-projects`, используйте один экземпляр MCP и один вариант навыка.
Отключите прежнюю регистрацию перед включением плагина; проекты сохраняйте.

## Навык и происхождение

Рабочий навык со scripts/references/assets перенесён в
`skills/elhart-hmi-projects` из `IlistTeam/elhart-hmi-projects`, commit
`e0935c8c26e1bedc9d82c88a4b54274fe5086203`.
Дальнейшее сопровождение bundled-копии выполняется здесь; исходный репозиторий
остаётся доступен для прежних отдельных установок. Пример SLM — демонстрация,
его регистры и блокировки не являются картой оборудования пользователя.

Исходный навык FStudio сохранён в `upstream/fstudio-skills`, вне автоматически
обнаруживаемого `skills/`: он не должен направлять работу Elhart на FStudio.
Сборка старого архива `package-skill.py --profile fstudio` сохранена.
MIT-лицензия MCP и исходные авторы сохранены; копирование навыка не расширяет
лицензию на ПО/ресурсы производителя.

## Обновление и проверка

Обновите каталог: `codex plugin marketplace upgrade ilistteam`, затем плагин
в клиенте. Повторите setup из нового установленного пакета с прежними путями:
нативные сборки не входят в Git. Настройки/проекты переживают обновление кэша.
Переподключите MCP после подготовки согласованных Python-файлов и addin.

```powershell
python -m unittest discover -s tests
python skills/elhart-hmi-projects/scripts/test_helpers.py
```

Нативные проверки системных настроек и порядка DTO требуют установленного
ELHART HMI Soft, поэтому не запускаются на обычном GitHub runner:

```powershell
python skills/elhart-hmi-projects/scripts/test_system_validation.py
python skills/elhart-hmi-projects/scripts/test_control_validation.py
```

Host-тесты и MCP-handshake без редактора не заменяют нативную сборку и
визуальную проверку HMI. Их границы описаны в [установке](elhart-installation.md).
