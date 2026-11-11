# Doodlik — контекст Unity-проекта

<!-- unity-onboarding:generated:start -->

Дата анализа: 2026-11-11. Исходная база Git: `475853f8d5f7daa2eaddc87c32d6bf33cce2820b`; анализ включает локальное обновление до экспедиций. Корень проекта — каталог, содержащий `Assets`, `Packages` и `ProjectSettings`.

## Подтверждённое окружение

- Unity `6000.5.2f1` (`ProjectSettings/ProjectVersion.txt`). Версия игры `0.0.4`, Android code 4; целевые сборки — Windows x64 и Android ARM64.
- URP `17.5.0`: установлен в manifest/lock и выбран через `customRenderPipeline` в профилях QualitySettings. Отсутствие глобального pipeline в GraphicsSettings не означает отсутствие URP.
- Input System `1.19.0`, `activeInputHandler: 1`; `Player.cs` использует Keyboard и Touchscreen.
- uGUI `2.5.0`, TextMesh Pro; интерфейс создаётся кодом при запуске.
- Unity Test Framework `1.7.0` установлен. Основной набор проверок — собственные Editor Validation-классы, а не отдельные тестовые asmdef.
- Сетевой игровой код не обнаружен в рассмотренных системах. Пакет Multiplayer Center сам по себе не подтверждает мультиплеер.

## Структура и запуск

- Игровые C#-файлы находятся непосредственно в `Assets`; редакторские инструменты — в `Assets/Editor`.
- `Assets/Resources` содержит загружаемые ресурсы, шейдеры и настройки. `Assets/TextMesh Pro/Examples & Extras` — поставляемые примеры, не собственный игровой код.
- Собственных `.asmdef` и `.asmref` не найдено: используются стандартные Assembly-CSharp и Assembly-CSharp-Editor.
- В EditorBuildSettings включена одна игровая сцена: `Assets/Scenes/SampleScene.unity`. Та же сцена указана в BuildReleases и ExpeditionTestBuild.
- `FrontEndUI`, `GameMenuUI`, `RunFlow` и компоненты с RuntimeInitializeOnLoadMethod управляют стартом, меню и переходами; отдельной сцены главного меню нет.

## Основные системы

| Система | Источники |
| --- | --- |
| Движение и платформы | `Player.cs`, `Spawner.cs`, `JumpReachability.cs`, `MovingPlatform.cs` |
| Режим и сложность | `GameplayPreferences.cs`, `DifficultyRules.cs`, `JourneySettings.cs` |
| Прогресс и сохранения | `PlayerProgress.cs`, `RunRecords.cs`, `RunStatistics.cs`, `ExpeditionProgress.cs` |
| Испытания | `ChallengeCatalog.cs`, `ChallengeSession.cs`, `ChallengeBoardUI.cs` |
| Экспедиции | `ExpeditionRun.cs`, `ExpeditionSession.cs`, `ExpeditionEnemy.cs`, `ExpeditionWorkshop.cs` |
| Интерфейс и звук | `FrontEndUI.cs`, `GameMenuUI.cs`, `GameAudio.cs`, `ResponsiveDisplay.cs` |

Подтверждены MonoBehaviour-компоненты, статические сервисные классы, настройки ScriptableObject, PlayerPrefs и JSON через JsonUtility. У сохранений есть версия и резервная копия; это локальное хранение, а не облачный профиль.

Обычно используются глобальное пространство имён, PascalCase для типов и методов, camelCase для полей и отдельные скобки. В коде есть как развёрнутые, так и компактные методы; единый стиль нельзя выводить из одного файла.

## Проверки и границы доказательств

- 2026-11-11 выполнен `Tools/ValidatePickupSchedule.ps1`: 768000 платформ в 256 последовательностях, проверка прошла.
- Прямые зависимости manifest/lock совпадают; в публикуемых ресурсах нет отсутствующих `.meta` и повторяющихся GUID.
- Все 195 C#-файлов совпали со снимком `.utmp/ExpeditionTestBuild-20261102`; это сопоставление исходников, не новая компиляция.
- Прямой запуск `ValidateReleaseBindings.py` на прежних Windows/Android-артефактах завершился ошибкой разрешения `unity default resources`. Файл имеется внутри Resources; скрипт не завершил полную проверку связей.
- Unity не открывался, Console не очищалась, Play Mode и сборка не запускались. Работа на физическом телефоне этим анализом не подтверждена.

## Инструменты и ограничения работы

- Unity MCP в доступном наборе инструментов не обнаружен; подключение к редактору не проверялось. Источники анализа — файлы проекта и предыдущие отчёты.
- Не менять игровые механики для оформления портфолио. Сохранять `.meta` и GUID.
- Специальные тестовые сборки требуют изолированной `.utmp`-копии и отдельной идентичности приложения. Обычный BuildReleases пишет на рабочий стол в `Doodlik/v<версия>`.
- Не публиковать `Library`, `Logs`, `.utmp`, пользовательские сохранения, ключи подписи и локальные исполняемые сборки в дереве исходников.

Дополнительные источники: `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/QualitySettings.asset`, `ProjectSettings/ProjectSettings.asset`, `ProjectSettings/EditorBuildSettings.asset`, `Docs/JourneyUpdate.md`, `Docs/Progression.md`, `Docs/ExpeditionRoguelite-Minimum.txt`, `Docs/ExpeditionTest-20261102-Readme.txt`, `Assets/Editor/BuildReleases.cs` и `Assets/Editor/ExpeditionTestBuild.cs`.

<!-- unity-onboarding:generated:end -->
