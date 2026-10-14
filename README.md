# Инженер Lite — Windows

Самостоятельное Windows-приложение для месячных ГСМ-отчётов через сервер LumaWork. Разработка начинается с Lite; полный вариант описан отдельно.

Сейчас создан минимальный solution: переносимое ядро, тесты, WinUI startup window и Windows CI workflow. Login, XLSX importer, месячные данные и серверное формирование отчёта ещё не подключены. Windows compilation/startup и CI run пока не подтверждены.

Планы: [Lite](WINDOWS_LITE_IMPLEMENTATION_PLAN.md), [полная версия](WINDOWS_IMPLEMENTATION_PLAN.md).

## Локальные проверки на Mac

Нужен .NET SDK из `global.json` — 10.0.402 или совместимый patch этой feature band. Тестовый скрипт ищет SDK в PATH, затем в пользовательском `~/.dotnet`; настройки shell не изменяет.

```bash
./scripts/test-core.sh
```

Ядро целится в `net10.0`, не импортирует WinUI/WinRT. Тесты проверяют двухэкранный Lite scope, восстановление запрещённого раздела и config resolution/validation. Они не обращаются к production API.

## Windows build

Предлагаемый runtime baseline — Windows 11 25H2 x64. В проекте минимальный уровень Windows API — Windows 11 build 22000; это технический minimum, а не подтверждение runtime на каждой версии Windows.

Нужны .NET SDK из `global.json`, PowerShell 7, Visual Studio 2026 MSBuild и Windows SDK/WinUI build components. Versions pinned: Windows App SDK 2.5.1, SDK BuildTools 10.0.28000.2705; XAML target — Windows SDK 26100. Stable SDK выбран по [официальным downloads](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads).

```powershell
pwsh -File scripts/build-windows-lite.ps1 -Configuration Debug
```

Скрипт выполняет core tests, locked restore и native x64 build. Результаты: `artifacts/tests/core.trx`, `artifacts/windows-lite-build.binlog`, compiled output в `artifacts/windows-lite/`.

Это packaged Debug build с выключенной подписью и генерацией установщика. Каталог build output не является готовым MSIX: запуск packaged app требует корректного deployment/registration на Windows. Первую проверку старта выполнить через Visual Studio/development deployment; публичный сертификат и installer появятся на отдельном этапе.

Если намеренно меняются зависимости, обновить locks на Windows и проверить diff:

```powershell
pwsh -File scripts/build-windows-lite.ps1 -Configuration Debug -UpdateLocks
```

## GitHub Actions

Workflow: `.github/workflows/windows-lite-ci.yml`. Runner: `windows-2025-vs2026`, VS 18.x, pinned action commit SHAs, SDK по `global.json`; tests и Debug x64 build сохраняются как artifacts на 7 дней. Workflow не подписывает, не публикует release и не изменяет серверные данные.

Локальный Git создан; remote пока не назначен. Workflow можно запустить после подтверждённых commit/push в доступный GitHub repo. Windows Server runner подтверждает сборку, Windows 11 runtime проверяется отдельно.

## Конфигурация и источники

`src/Engineer.Lite.Windows/appsettings.json` содержит только несекретную configuration. API origin пока пустой; startup window показывает реальное состояние конфигурации, сетевой probe не выполняется.

Ключи LumaWork: `LUMAWORK_API_URL` / `LUMAWORK_API_ORIGIN`. Порядок сохранён из исходного AppConfig: environment → packaged config → preferences для primary key, затем тот же порядок для alias. В этом scaffold preferences store ещё отсутствует. Значения с credentials/query/fragment и не-HTTP(S) origin отклоняются; некорректный настроенный URL не подменяется fallback.

Секреты в JSON не добавлять. JWT/SO credential storage относится к следующему этапу.

`tests/Engineer.Core.Tests/Fixtures/source-baseline.json` хранит hashes read-only source файлов, включая текущие незакоммиченные изменения reference checkout. Эти пути не читаются build/test scripts и не являются dependency.

Windows PNG assets получены уменьшением существующей Mac app icon `AppIcon.appiconset/icon_512x512@1x.png`; исходный asset не изменялся, новый бренд не создавался. Package publisher `CN=LumaWork.Development` — development identity, не зарегистрированный production publisher.
