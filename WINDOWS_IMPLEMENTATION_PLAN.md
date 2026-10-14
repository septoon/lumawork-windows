# «Инженер» для Windows — план реализации

> **Для исполнителя:** выполнять этапы последовательно по `superpowers:executing-plans`. Делегирование — только по выбранному пользователем способу работы. Чекбоксы отмечать по фактическому результату; после каждого этапа дописывать изменения, проверки и оставшиеся ограничения в раздел 13.

**Цель:** создать самостоятельное нативное Windows-приложение «Инженер» с функциями существующего LumaWork, собственным desktop UI и прежними серверными контрактами.

**Архитектура:** независимый проект `lumawork-windows`; собственное C#-ядро для DTO, transport, расчётов, репозиториев и синхронизации. Windows UI и системные адаптеры отделены от ядра. iOS, macOS и backend служат источниками контрактов, без зависимости сборки от соседних checkout.

**Предлагаемый стек:** C#, .NET 10 LTS, WinUI 3/XAML, стабильный Windows App SDK, System.Text.Json, HttpClient, WebView2; тесты ядра — xUnit. Это допущение данного плана, а не ранее утверждённое пользователем решение для Windows. Microsoft рекомендует WinUI 3 для новых нативных Windows desktop apps; актуальная инструкция создания использует .NET 10. [WinUI 3](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/), [создание проекта](https://learn.microsoft.com/en-us/windows/apps/get-started/start-here).

**Спецификация:** исходный [план macOS](../lumawork-macos/docs/superpowers/plans/2026-10-08-engineer-macos.md), разделы 1–21, с приоритетом уточнения о независимом приложении. Этот документ переносит функциональные требования на Windows, заменяет платформенные механизмы и задаёт собственный порядок реализации.

**Дата:** 14.10.2026. **Статус:** подготовлен план; приложение, solution, workflow и установщик ещё не созданы.

## 1. Источники и исходное состояние

В проверенных каталогах `projects`, `Desktop`, `Documents` и `Downloads` отдельный общий план macOS/Windows не найден. Основной найденный документ посвящён macOS. Его журнал реализации и отметки готовности не являются статусом Windows.

| Источник | Для чего использовать |
| --- | --- |
| [План macOS](../lumawork-macos/docs/superpowers/plans/2026-10-08-engineer-macos.md) | Функциональный охват, контрактные инварианты, окна, кеши, ошибки, этапы и критерии готовности |
| [Mac EngineerCore](../lumawork-macos/Packages/EngineerCore/) | Read-only пример выделенных DTO/API/repositories; правила и тестовые сценарии, без импорта Swift-кода в Windows |
| [Manifest fixtures](../lumawork-macos/docs/core-contract-fixtures.md) и [тесты](../lumawork-macos/Packages/EngineerCore/Tests/EngineerCoreTests/) | Синтетические JSON и проверяемые request/response semantics; неполное покрытие не выдавать за полный контрактный барьер |
| [iOS-исходники](../LumaWork/LumaWork/LumaWork/) | Актуальные функции, поля, фильтры, расчёты и platform-independent semantics |
| [Политика архива SimpleOne](../LumaWork/CLOSED_REQUESTS_SYNC.md) | Bootstrap, delta, watermarks, ручной импорт и recovery |
| Локальная копия `../LumaWork/deploy/lumawork-api/backend/src/` | Подтверждение endpoints/permissions/validators при переносе конкретного домена; не доказательство production runtime |

На 14.10.2026 HEAD iOS — `2fbbc5f`, macOS — `7516fc1`; оба reference checkout содержат незакоммиченные изменения. Поэтому HEAD не описывает весь прочитанный код. На этапе 0 фиксировать commit и SHA-256 каждого использованного файла, сохраняя посторонние изменения.

Особенно учесть изменения после исходного плана 08.10.2026:

- `SimpleOneRequestsFeature/SimpleOneRequestsModels.swift`: API `yyyy-MM-dd HH:mm[:ss]` трактуется как UTC, локализованные SO/XLSX даты — как локальное время. Нельзя повторно сдвигать XLSX-время.
- `AppModels.swift` и `SalaryFeature/SalaryServiceStore.swift`: отпускные, `vacationStartDate`/`vacationEndDate`, fallback расширенных полей из `payload`. Это источник сценария и формата; успешный реальный backend round trip в этой задаче не проверен.
- Оставшиеся Mac admin groups уже имеют локальные DTO/API/views и synthetic tests. Их наличие не доказывает полный runtime-паритет; backend permissions имеют приоритет над более широким старым UI-допуском.

## 2. Можно ли разрабатывать с Mac и использовать GitHub Actions

**Да: исходники редактировать на Mac, переносимое ядро проверять локально, WinUI и пакет собирать на Windows runner GitHub Actions. Полностью локальный цикл сборки и запуска WinUI на macOS не предусмотрен этим стеком.**

### Проверено на текущем Mac

| Возможность | Результат | Что требуется |
| --- | --- | --- |
| ОС и архитектура | macOS 15.6.1, arm64 | Подходят для редактирования и переносимого .NET-ядра |
| Git | Доступен | В текущем корне Git не инициализирован |
| GitHub CLI | `gh 2.93.0`; API подтвердил аккаунт `septoon` | Можно управлять CI с Mac после появления доступного репозитория |
| .NET SDK | `dotnet` отсутствует в PATH; не найден и в проверенных стандартных каталогах | Установить .NET 10 SDK arm64 для локальных core tests |
| PowerShell / MSBuild | Не найдены в PATH | Mac не обязан иметь их при выполнении Windows-скриптов в CI |
| Windows VM | Parallels Desktop, VMware Fusion, UTM не найдены в `/Applications` | Это не проверка всех возможных удалённых Windows-машин; Windows test host пока не подтверждён |
| Xcode | 26.3 | Не заменяет Windows SDK, XAML compiler и Windows runtime |
| GitHub-репозиторий | `gh repo view septoon/lumawork-windows` не разрешил репозиторий | Создать/указать доступный репозиторий и проверить, разрешены ли Actions |
| Подпись и дистрибуция | Не проверялись | Для публичного установщика нужны выбранная package identity, подпись и способ доверия/доставки |

### Контур GitHub Actions

GitHub документирует x64 runner `windows-2025-vs2026`; он подходит для первоначального WinUI/.NET 10 CI. Выбирать явный label и записывать версии инструментов в лог. Образ обновляется, поэтому label сам по себе не фиксирует MSBuild/SDK. [GitHub runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners), [состав Windows Server 2025 / VS 2026](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-VS2026-Readme.md).

Планируемый `.github/workflows/windows-ci.yml`:

1. Триггеры: push, pull request, ручной `workflow_dispatch`; `permissions: contents: read`, ограниченный timeout, отмена устаревшего запуска одной ветки.
2. Checkout; установка SDK по `global.json`; выбор VS 2026 MSBuild; проверка Windows SDK и WinUI build components. Официальные actions фиксировать на проверенные commit SHA при создании workflow.
3. Восстановление зависимостей из lock files, сборка реального ядра и offline contract/domain tests.
4. Debug x64 сборка WinUI с XAML compiler, без production credentials и без release-подписи.
5. Сохранение build output с зависимостями, TRX, MSBuild binlog и диагностикой как artifacts. Не включать пользовательские кеши, токены, личные документы и private signing keys.
6. Release/package job отдельно, по ручному запуску после проверки базовой сборки. Секреты подписи нужны только этому job; первый compile job их не требует.

Windows runner здесь работает на Windows Server 2025. Его сборка не доказывает запуск и оформление на Windows 11. Обычный CI также не подтверждает Windows Hello, системные диалоги, экран блокировки и поведение на слабой машине. Для этого нужен интерактивный Windows 11 test host.

Рекомендуемый первый цикл: Mac → core tests → GitHub Windows build → скачать artifact и логи → исправить на Mac. Actions runner не является постоянным удалённым рабочим столом. Для UI нужен отдельный Windows PC/VM или согласованный интерактивный test host. На Apple Silicon Windows ARM VM проверяет ARM64/emulation, а заявленный x64 runtime дополнительно проверить на x64 Windows.

По прямому указанию пользователя 14.10.2026 используется публичный GitHub-репозиторий и стандартные public runners; private repo для этого рабочего контура не предлагать. Перед push проверять весь коммит на секреты/личные данные; private keys, credentials, локальные конфиги и кеши не публиковать. Actions policy и наличие нужных workload проверяются в выбранном репозитории. [Условия GitHub-hosted runners](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).

## 3. Обязательные ограничения

- Самостоятельное приложение; iOS/macOS/backend не изменять. Не создавать общие cross-repo packages, symlinks или загрузку соседних исходников при сборке.
- Сохранить имя «Инженер», русские бизнес-термины, названия разделов и точные условия фильтрации.
- HTTP method/path/query, auth headers/cookies, JSON keys, enum raw values, envelopes, chunks и SSE остаются прежними. Новые endpoints и миграции в scope не входят.
- Переносить правила, а не SwiftUI/UIKit/AppKit views. Windows UI вызывает одну реализацию домена в собственном ядре.
- Работающие контракты не заменять предполагаемыми. Не добавлять массовые mutations, глобальный поиск, новые отчёты, daemon или универсальную offline write queue.
- Кеш, сети, загрузки и sync принадлежат сессии приложения; filters, selection, dirty drafts и локальные gates — окну.
- Cache-first: при повторном входе показывать сохранённый snapshot, обновлять без обнуления списка; stale/partial/offline маркировать явно.
- Loading, empty, offline, network error, forbidden и expired auth — разные состояния. Кратковременные сообщения имеют общий presenter по смыслу `AppBannerCenter`.
- Секреты, PIN и личные данные не помещать в исходники, fixtures, обычные settings, URL, логи и artifacts.
- Сборка, тесты ядра, runtime UI и установочный пакет — отдельные уровни доказательства.
- Commit/push, создание GitHub-репозитория, публичный release и изменения VPS выполняются только по поручению пользователя. Разрешения из журнала Mac-проекта не переносятся сюда.

### Предлагаемые Windows-решения

| Решение | Предложение для реализации |
| --- | --- |
| Основная проверяемая ОС | Windows 11 25H2, build 26200, x64; 8 ГБ RAM. Это baseline плана, не требование пользователя. [Версии Windows 11](https://learn.microsoft.com/en-us/windows/release-health/windows11-release-information) |
| Дополнительные ОС/архитектуры | Поддержку более старых Windows и ARM64 объявлять только после отдельного запуска; первый milestone — x64 |
| Сборка | VS 2026 MSBuild, .NET 10, стабильный Windows App SDK; точные SDK/NuGet версии закрепить после минимального успешного build |
| Приложение | `EngineerWindows`, отдельная package identity; не использовать iOS/Mac bundle identity как готовую Windows identity |
| Дистрибуция | Сначала Debug artifact; затем подписанный MSIX. Store и updater не требуются для первого preview |
| Оформление | Fluent, система/светлая/тёмная темы, system brushes; Mica только при поддержке и без потери читаемости |
| Окно | Начальный ориентир 1280 × 820 DIP; рабочий минимум 1000 × 640 DIP, скрываемые sidebar/details. Проверять DPI 100/150/200% |

## 4. Архитектура и карта файлов

Пути ниже — планируемые, относительно этого корня. Создавать только файлы ближайшего этапа, без пустых экранов всех доменов.

```text
EngineerWindows.sln
global.json
Directory.Build.props
src/Engineer.Core/
  Engineer.Core.csproj
  App/                  # config, session identity, section metadata, coordinator
  Networking/           # отдельные HTTP clients, errors, request builders
  Persistence/          # scoped snapshots, atomic writer, schema/checkpoints
  Routes/ Requests/ Fuel/ Salary/ Vehicles/ Documents/
  Equipment/ Employees/ WorkSchedule/ Assistant/ Wiki/ FTP/ Feedback/ Admin/
src/Engineer.Windows/
  Engineer.Windows.csproj
  App.xaml(.cs)
  App/SessionContainer.cs
  App/WindowWorkspaceState.cs
  Views/MainWindow.xaml(.cs)
  Settings/SettingsPage.xaml(.cs)
  Features/<Domain>/     # новый UI и window-owned editors
  Platform/             # credentials, files, browser, auth, lifecycle, media
  Assets/
  Package.appxmanifest
tests/Engineer.Core.Tests/
  Engineer.Core.Tests.csproj
  Fixtures/
tests/Engineer.Windows.Tests/   # только при появлении Windows-specific logic
scripts/build-windows.ps1
scripts/package-windows.ps1
.github/workflows/windows-ci.yml
```

`Engineer.Core` целится в переносимый `net10.0`, без WinUI/WinRT. Platform interfaces вводить только для реального injected seam: storage key/credentials, local auth, file access, lifecycle. Не строить общий DI framework.

Контракты интерфейсов закреплять на этапе владельца до реализации зависимого этапа. DTO и raw values берутся из инвентаризации; proposed C# names не являются wire keys. UI обновляется через DispatcherQueue, I/O/parse/import выполняются вне UI thread; поздние результаты проверяют account/session generation и mutation revision.

| Компонент | Обязанность |
| --- | --- |
| `SessionContainer` | Composition root, независимые app/SO/Wiki clients, credentials и repositories |
| `ApplicationCoordinator` | Один владелец polling, sync, monthly mileage и session invalidation |
| `WindowWorkspaceState` | Window ID, раздел, filters, selection, opened editors; без токенов и общего кеша |
| `RouteDayRepository` | Key `(date, workType)`, serial writes, read-back reconciliation |
| Route editor | Собственный base fingerprint, draft, dirty state, отдельные Save/Send |
| Windows adapters | Только системные API; не business permissions, transport mapping и расчёты |

## 5. Функциональный охват и Windows UI

Сохранить 12 обычных разделов и условную «Админку». Документы, график, ГСМ, профиль и route archive остаются вложенными сценариями. В metadata сохранить существующие raw mappings: `maintenance` → «Авто», `wiki` → «Помощник», `users` → «Админка».

| Раздел | Функции | Windows-представление |
| --- | --- | --- |
| Главная | День POS/АРМ, маршрут, остановки/статусы, archive, пробег, ГСМ, заправка, отправка | Сводка, выбор даты/типа, список строк с колонками, point details, явные Save/Send |
| Мой рюкзак | ЗИП, личное оборудование, фото и карточки | Виртуализированный список, details/preview, существующие локальные подрежимы |
| Сотрудники | Каталог, населённый пункт, контакты, график | Список/колонки, карточка, month schedule и фильтр города |
| Авто | Действующее/другие авто, реквизиты, документы, ТО/затраты | Список, detail forms, maintenance rows, документы |
| Топливо | Заправки, виды топлива, выплаты/вычеты, carryover, баланс, аналитика, импорт | Период, таблица операций, summary, import preview отдельно от commit |
| Помощник | История, чат/SSE/tools/approvals, Wiki, attachments и диктовка | Conversations + chat, multiline composer, file/drop/paste, Wiki results/article |
| FTP | HTTPS proxy catalog, roots, favorites, breadcrumbs, search, ZIP, downloads | File browser и persistent transfer panel; Save As/Open/Show in Explorer |
| Зарплата | Выплаты, отпускные/период, month analytics, slips и защита | Protected rows/details, документы, PIN/Windows Hello по capability |
| Заявки | Active/closed/warehouse, detail/browser, поиск, scopes/periods/SLA, XLSX, comments/ИНН | Виртуализированные строки/колонки, stable selection, detail pane и web fallback |
| Координация | Инженеры, group active/closed, employee details, pagination | Инженеры + заявки выбранного scope, checkpoint и partial-load status |
| Трудозатраты | JSON head/full refresh, XLSX fallback, Работа/Дорога | Таблица по периоду, итоги, export |
| Аналитика | Существующие periods, aggregates и charts | Те же метрики из общего snapshot, расчёт вне render path |
| Админка | Все реальные группы и permissions | Локальная навигация, таблицы, editors/previews и guarded actions |

Shell: `NavigationView`, toolbar/CommandBar, context menus и detail pane. Начинать с stable виртуализированного `ListView` с колонками; grid/chart/Markdown зависимости выбирать только при реальной необходимости. Не считать экспериментальные controls WinUI Gallery production API.

Команды: Ctrl+F — поиск текущего раздела, F5 — его разрешённое обновление, Ctrl+S — сохранение текущего draft, Ctrl+C — корректное копирование выделения. Save не отправляет отчёт. Chat Return отправляет, Shift+Return добавляет строку; IME composition не вызывает send. Контекстная обработка не перехватывает обычное редактирование текста.

## 6. Контракты, парсинг и точные правила

### Сеть и авторизация

- LumaWork API: прежний email-code/token flow, Bearer, `/me`, `/api/v2/*` и фактический Windows version/build в `X-LumaWork-Version`/`X-LumaWork-Build`.
- SimpleOne: прежние `/auth/login`, `/user/me`, cookie `auth=<authKey>`, собственный session scope. App login не означает SO login.
- Wiki: отдельный origin/token; прежние `/health`, `/search`, `/article/{id}`, `/pdf/{id}`. Wiki token не становится app/SO credential.
- Config сохраняет существующие names/aliases и порядок разрешения: environment → packaged non-secret config → scoped preferences вместо Apple Bundle/UserDefaults. Запуск из Start Menu должен работать без shell env; placeholders/invalid origins — configuration error.
- Отдельные clients/cookie jars; percent encoding, null/absent, numeric JSON, envelopes, accepted statuses и `X-LumaWork-Personal-Comments-Version: 2` сохраняются там, где нужны.
- HTTP 204 не требует JSON. 401/403/404/429/5xx различаются; invalid response не заменяет валидный кеш.
- Retry чтения — прежний; writes/streams не повторять вслепую. Неизвестный результат create требует проверки, а не сообщения «точно не отправлено».
- Серверные `lastAppVersion/lastAppBuild/lastAppSeenAt` пока общие для платформ. Windows version suffix не гарантирует платформенную сегментацию outdated-email; проверить до release, не представляться iOS-клиентом.

### Доменные инварианты

- Route `POST /api/v2/routes`: прежние `date`, `workType`, `distanceKm`, `periodStartOdometer`, `sent`, `stops`; статусы «В процессе», «Выполнена», «Отказ»; `ARM` на wire и «АРМ» в UI.
- Потерянный route POST response: GET дня/типа и сравнение payload до повтора; server IDs могут измениться. Прежняя route queue не превращается в очередь mutations всех доменов.
- Dirty route не перезаписывать чужим refresh. Preflight выявляет remote change; без server CAS нельзя обещать строгую защиту от одновременной записи iOS/Mac/Windows.
- Непустой поиск по закрытым заявкам снимает оба period gates в личном/групповом архиве. Ищет во всём доступном архиве; partial coverage явно видна.
- Для `returnEquip` effective date — корректный `registeredAt`, иначе `closedAt`; правило одно для списков, analytics и read-only answers.
- Selection — по `sys_id`/stable domain ID; full description не заменять обрезанным list description. ИНН — строка с ведущими нулями; exact matching и permissions сохраняются.
- Closed sync: XLSX bootstrap с baseline до выгрузки → delta; watermark `(sys_updated_at, sys_id)`, narrow/wide scopes, sort, cell-value enrichment, `nextPage`/`refreshedIDs`/`totalCount` сохраняются.
- Manual XLSX — upsert и согласованный reset baseline/generation, без mirror delete. Poll cadence 60/120/300 секунд и wide-check шесть часов брать из актуальных constants; один in-flight владелец на scope.
- Time reports: JSON head check/full refresh/XLSX fallback; display sorting не изменяет сетевую sync sorting и суммы Работа/Дорога.
- Авто/ТО связаны по реальному vehicle ID. Заправки и выплаты остаются отдельными записями; fuel-import preview и commit разделены.
- Деньги/числа принимают русскую запятую, пустое поле и отмену. Тип вычислений выбирать по source parity, JSON остаётся числовым; округление и порядок расчётов не менять молча.
- SimpleOne API UTC и локальные XLSX dates различаются; один parser для card/detail/cache/SLA/export. Проверить UTC+3, другую зону, пустую дату/epoch и отсутствие двойного сдвига.
- Зарплата сохраняет все актуальные payment kinds, включая отпускные и период; fallback `payload` проверяется offline fixture, backend persistence — отдельным разрешённым round trip.
- Assistant: прежние SSE events, `clientRequestId`, history, tools/approvals. Разрыв соединения не доказывает отмену server run; новый cancel endpoint не вводить.
- Feedback: draft → chunks → submit → additions; устойчивый `clientRequestId`, draft recovery и неизвестный исход без дублирования. `FeedbackDeviceInfo` имеет прежние keys и реальные Windows values.
- Admin: overview/audit, users/permissions/block/delete/email, images/catalog/requests, ГСМ template/layout/projects, feedback, assistant settings/limits, site/screenshots/order, engineer files/source editor. Read-only Wiki snapshots и IPA/SideStore areas сохраняют прежний смысл; Windows publish endpoint не изобретать.
- Source JSON editor сохраняет неизвестные metadata. Каждая admin action сверяется с backend permission; неподтверждённый template DELETE не переносить как рабочую функцию.

## 7. Платформенные замены и ограничения

| Mac/iOS механизм | Windows-план |
| --- | --- |
| Keychain | Отдельный app credential namespace через Windows Credential Manager/DPAPI; выбрать один production adapter и проверить persistence/удаление |
| CryptoKit sensitive vault | AES-GCM через .NET crypto, per-user key защищён Windows adapter; atomic versioned envelope. PIN экрана не равен шифрованию |
| Touch ID | Windows Hello через поддерживаемый desktop API, с правильным владельцем окна и capability/result states; прежний 4-значный salary PIN и email recovery сохраняются |
| NSOpenPanel/NSSavePanel/bookmarks | Windows file/folder pickers и выбранные пользователем destinations; owner window и доступ после restart проверить для выбранного API. [Pickers](https://learn.microsoft.com/en-us/windows/apps/develop/files/using-file-folder-pickers) |
| Quick Look/PDFKit | PDF/image preview или явное открытие локального файла системным приложением; безопасная очистка чувствительных temp files |
| WKWebView | WinUI WebView2, отдельный account-scoped user data folder/cookies, host restrictions и logout cleanup. [WebView2](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/webview2) |
| NSPasteboard/NSWorkspace | Clipboard и shell open/reveal для реально существующих локальных файлов; URLs с tokens не передавать наружу |
| MapKit / Apple geocoding/directions | MapKit напрямую не переносится. Existing Yandex route URLs сохраняются; встроенная карта/автоматический расчёт требуют отдельного подтверждённого доступного provider/API |
| ImageIO/CoreImage/Vision | Windows image decode/downsample, QR, PDF text/OCR adapters с тем же payload/limits; конкретную библиотеку выбрать на этапе потребителя |
| AVAudioEngine/Speech | Windows speech adapter только после проверки русского языка, offline capability и permissions. Системная диктовка доступна как fallback, но не объявляется собственным offline recognition приложения |
| UNUserNotificationCenter | Windows App SDK app notifications с прежними preference/event/dedup rules; отказ не блокирует приложение |
| WidgetKit/App Intents | Apple-only surfaces не переносятся буквально. Реальные read-only domain queries сохраняются; Windows Widgets/deep links — отдельная capability-проверка, без обещания Siri/Shortcuts на Windows |

**Карта — отдельный открытый вопрос:** исходный Mac transport использует MapKit для geocode/search/directions. Наличие ссылки Яндекс.Карт не доказывает наличие API для автоматического расчёта. Не подменять дорожное расстояние прямой линией. До выбора provider доступен route editor с сохранённым/ручным километражом и существующей внешней ссылкой; полный map parity остаётся незакрытым. Новая платная/ключевая интеграция требует отдельного решения перед её подключением.

Attachment limits из source сверить перед переносом: image 1800 px/4 MiB; document 12 MiB, legacy DOC/XLS 5 MiB, 12000 extracted chars, archive entry 8 MiB, PDF 30 pages, OCR 8 pages, worksheets 6. ZIP inflation/path traversal и malformed/encrypted documents — отдельные проверки, лимиты не уменьшать случайно.

## 8. Состояние, безопасность, файлы и lifecycle

- Дисковый namespace: app user + SimpleOne identity для SO-источников. Нет anonymous fallback пользовательского кеша. Hydrate только после подтверждения identity.
- Windows snapshot format собственный, с schemaVersion; не пытаться автоматически читать Apple app containers. Сломанные файлы quarantine, drafts сохранять.
- Один serial writer, atomic replace, error/no-space сохраняет предыдущий snapshot. Не сообщать «сохранено» при неуспешной записи.
- Logout сначала закрывает локальный доступ, gates/previews, cancels polls/streams/writes и отзывает credentials; сетевой сбой revoke не сохраняет старую сессию активной.
- 401/403 не маскировать offline-кешем. Late account-A response не попадает account B; role/token changes инвалидируют grants.
- Два окна имеют независимые selection/date/type/drafts. Изменение shared record не заменяет dirty editor; закрытие/смена account предлагает сохранить draft, отменить или отказаться.
- Protected salary/admin guard проверяется также для export/clipboard/preview/commands. Windows Hello cancel/failure/lockout не открывает контент; lock screen закрывает gates.
- FTP использует existing HTTPS proxy. Queue/history persist; after restart underlying transfer/state проверяется, interrupted download не остаётся вечно «загружается».
- Загрузка stream-ится во внутренний managed файл; export во внешний destination — отдельный шаг с overwrite/cancel/no-space/moved-folder handling. Remote item не имеет Explorer path до загрузки.
- Закрытие последнего окна завершает обычный процесс после draft/transfer handling. Tray/startup task/service не добавлять. Polling/HTTP transfers гарантируются только пока работает процесс.
- Sleep/network loss — checkpoints и pause; wake — один catch-up, без replay всех таймеров. Crash/force quit — recovery на следующем запуске, без обещания фонового завершения.
- OS background transfer может исследоваться позже; его наличие не объявлять до проверки именно текущего proxy/auth/logout и Windows lifecycle.
- WebView2 cookie перед загрузкой, redirect/new-window host validation, никакой передачи app JWT/Wiki token чужому host; tokenized PDF скачать private client перед preview.
- Filename sanitization учитывает Windows reserved names, traversal, case collisions, long paths и reparse points. Cleanup ограничен managed directory, не следует наружу по ссылкам.
- UI доступен с keyboard/Narrator, уважает contrast/reduced motion/text scale; тема не запускает повторный network load. Большие списки виртуализированы, filtering/indexing вне UI thread.

## 9. Основные риски проверки

| Сценарий | Ожидаемое поведение | Владелец проверки |
| --- | --- | --- |
| Поздний GET после logout/account switch/write | Не раскрывает старые данные и не заменяет новую mutation | Этапы 2, 4, 10 |
| POST принят, ответ потерян | Reconciliation/unknown outcome; нет blind duplicate | Этапы 4, 6, 10 |
| XLSX bootstrap/import пересёкся с delta | Generation/checkpoint корректны, нет потерянных строк | Этап 5 |
| Две Windows-сессии/другой клиент изменили запись | Dirty draft сохранён, конфликт явный; CAS не обещается | Этапы 3, 4, 10 |
| Lock/403/no-space/sleep во время preview/transfer | Gates и ownership сохранены, состояние восстановимо | Этапы 2, 6, 7, 12 |

## 10. Этапы реализации

Во всех этапах: сначала инвентаризация source → synthetic fixture/targeted failing test для нетривиального правила → минимальная реализация → релевантная проверка → запись evidence. Domain tests работают на настоящем `Engineer.Core`, без извлечения фрагментов текста Swift/C# и без production mutations.

### Этап 0. Зафиксировать baseline и подготовить Windows build contour

**Файлы:** этот план; позднее `tests/Engineer.Core.Tests/Fixtures/manifest.json`, `global.json` и dependency lock files по потребности scaffold.

- [ ] Зафиксировать source fingerprints, exact method/path/query/DTO/raw/permissions для auth/config/sections и первой route вертикали; личные данные заменить синтетическими.
- [ ] Установить .NET 10 arm64 на Mac; подтвердить SDK и возможность выполнения `net10.0` core tests.
- [ ] По поручению пользователя создать локальный Git и доступный GitHub repo; проверить Actions policy/quota. Не переносить Git history или release permissions Mac-проекта.
- [ ] На `windows-2025-vs2026` проверить .NET 10, MSBuild, Windows SDK/WinUI components; закрепить рабочие stable версии в scaffold.
- [ ] Выбрать Windows 11 runtime test host; отсутствие такого host не блокирует core/compile, но фиксируется как blocker UI/release proof.

**Готово:** источники трассируемы, контур сборки определён и его ограничения записаны.

### Этап 1. Минимальный solution, ядро, WinUI окно и CI

**Создать:** solution/core/windows/test projects, `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, manifest/assets, `scripts/build-windows.ps1`, `.github/workflows/windows-ci.yml`.

- [ ] Core tests фиксируют 13 title/raw mappings, POS/ARM/status values, config validation и HTTP null/204/error semantics.
- [ ] Создать небольшое стартовое native окно с настоящей version/config information; перенести существующую иконку в требуемые Windows sizes, без генерации нового бренда.
- [ ] CI запускает core tests и Debug x64 XAML build, сохраняет artifact/TRX/binlog. Проверить скачанный artifact, зависимости и identity.
- [ ] Запустить минимальное окно на выбранном Windows host; отдельно зафиксировать startup evidence, не выдавать compile за runtime.

**Готово:** воспроизводимая Windows-сборка и стартовое приложение; auth/feature parity ещё отсутствуют.

### Этап 2. App/SimpleOne auth, credentials, кеш и lifecycle

**Файлы:** `Core/App/`, `Core/Networking/`, `Core/Persistence/`, Windows `App/SessionContainer.cs`, credential/storage/lifecycle adapters; `AuthContractTests.cs`, `SessionIsolationTests.cs`, `SnapshotPersistenceTests.cs`.

- [ ] Реализовать прежний email code/me/logout и SO login/me с разными auth scopes; Wiki config/token хранить отдельно.
- [ ] User-scoped encrypted storage, atomic errors/quarantine, single coordinator и generation guards; обеспечить cache-first restore.
- [ ] Тесты: 204/401/403, SO expired при валидном app token, account A→B late response, malformed restore, no-space, credential-write failure и offline logout.
- [ ] Windows runtime: credentials после restart, logout cleanup, sleep/wake и lock; gate не восстанавливается через чужую identity.

**Готово:** настоящий login/session lifecycle без утечек предыдущего аккаунта.

### Этап 3. Desktop shell, настройки, команды и окна

**Файлы:** Windows `Views/`, `App/WindowWorkspaceState.cs`, `Settings/`; `WindowStateTests.cs`, `CommandRoutingTests.cs`.

- [ ] NavigationView с доступными реальными разделами, toolbar/context actions, theme/visibility/account settings. Не рисовать фиктивные domain controls.
- [ ] Независимые selection/filter/drafts; восстановленный запрещённый раздел возвращает к разрешённому.
- [ ] Tests: два окна, focused commands, dirty close/logout/cancel; на Windows проверить keyboard focus/DPI/minimum size.

**Готово:** рабочая оболочка; разделы получают содержимое в следующих этапах.

### Этап 4. Главная, маршрут, пробег и ГСМ — первая вертикаль

**Файлы:** `Core/Routes/`, `Core/Fuel/` по текущим операциям; `Features/Home/`, route draft editor; `RouteContractTests.cs`, `RouteRepositoryTests.cs`, `RouteDraftTests.cs`.

- [ ] Keyed day/type repository, stops/status/coordinates, local Save, explicit Send, archive/odometer, существующая fuel/GSM entry.
- [ ] Reconciliation после lost POST, base fingerprint/conflict, late refresh/mutation revision; тесты двух dirty окон и route ID replacement.
- [ ] Existing Yandex URL builder; source map capability inventory и отдельное решение provider до встроенного geocode/directions. Незавершённый расчёт пометить gap, не имитировать.
- [ ] Проверить отчёт/даты/POS/АРМ и ручной километраж; отправку проверять offline fixture, live write только по отдельному поручению.

**Готово:** работающий маршрут с прежним wire contract; map gap закрывается только реальной интеграцией.

### Этап 5. Заявки, архив, координация, трудозатраты и аналитика

**Файлы:** `Core/Requests/`, четыре соответствующих Windows feature areas; `RequestFilterTests.cs`, `SimpleOneDateTests.cs`, `ArchiveSyncTests.cs`, `TimeReportTests.cs`, `AnalyticsTests.cs`.

- [ ] Active/closed/warehouse, native details и WebView2 fallback; comments/ИНН, stable selection и exact filters/effective dates.
- [ ] Bootstrap→delta→manual XLSX import→resume; account/SO-scoped checkpoints, one refresh owner, stale generation guards.
- [ ] Tests: непустой поиск вне периода, returnEquip, incomplete archive, одинаковые timestamps watermark, updated known record, import race и group resume.
- [ ] JSON head/full/XLSX трудозатрат; Работа/Дорога и analytics на общем snapshot; local XLSX import/export сохраняет поля и даты.
- [ ] UTC/local XLSX/date-cache-detail-SLA-export regression; виртуализация и измерение фильтрации на большом синтетическом архиве.

**Готово:** все request domains с одинаковыми правилами и проверенной sync correctness.

### Этап 6. Авто, топливо и защищённая зарплата

**Файлы:** `Core/Vehicles/`, `Core/Fuel/`, `Core/Salary/`, соответствующий UI и local authentication adapter; domain contract/calculation/gate tests.

- [ ] Vehicle ID associations, ТО/документы; fuel operations/carryover/monthly mileage с одним владельцем side effect.
- [ ] Fuel import preview→commit с unknown-outcome handling, без двойного импорта; запятая/empty/cancel/numeric wire и source rounding tests.
- [ ] Все payment kinds, отпускной период и payload fallback; fixtures не подменяют отдельную проверку серверной persistence.
- [ ] Salary PIN/email recovery, Windows Hello capability, encrypted snapshots/slips; export/preview/clipboard guarded.
- [ ] Tests: cancelled/failed local auth, stale gate after lock/account change, delayed load after write, потерянный create reply; Windows runtime без Hello и с доступным Hello.

**Готово:** финансовые правила сохранены; защита подтверждена за пределами первого экрана.

### Этап 7. Документы, FTP, previews и image adapters

**Файлы:** `Core/Documents/`, `Core/FTP/`, `Features/FTP/`, Windows file/preview/image adapters; `FileBoundaryTests.cs`, `TransferRecoveryTests.cs` и document contract tests.

- [ ] Рабочие/авто/зарплатные documents с metadata/upload/download; native pickers, save/overwrite/cancel и local preview.
- [ ] FTP proxy catalog/search/favorites/breadcrumbs/ZIP и persisted queue, download streaming, restart reconciliation.
- [ ] Tests: traversal/reserved names/reparse boundary, no-space, missing/moved destination, unknown size, corrupt/expired partial state и late account-A completion.
- [ ] Windows runtime: owner-window picker, Explorer/Open только завершённого файла, lock/logout sensitive preview cleanup, sleep/crash/relaunch.

**Готово:** настоящие файлы и восстановимая очередь; работа вне процесса не обещается.

### Этап 8. Рюкзак, сотрудники, график и профиль

**Файлы:** `Core/Equipment/`, `Core/Employees/`, `Core/WorkSchedule/`, profile models/services и corresponding views; equipment/schedule/profile parity tests.

- [ ] Оборудование/ЗИП/photos, сотрудники/города/контакты, график и existing permission equivalents.
- [ ] Профиль/avatar, виртуальная карта/vCard QR, fuel card, work documents и route preferences — реальные вложенные workflows.
- [ ] Tests: exact IDs/roles/cities/schedule dates, image manifests/limits, avatar cancellation и одинаковый QR payload; permission change очищает запрещённый selection.

**Готово:** справочные и личные сценарии не потеряны за desktop shell.

### Этап 9. Помощник, Wiki, attachments и диктовка

**Файлы:** `Core/Assistant/`, `Core/Wiki/`, `Features/Assistant/`, document/OCR/speech adapters по потребности; SSE/history/tools/attachments/wiki tests.

- [ ] SSE parser/IDs/events, history, approvals и существующие local tools; window-owned composer/conversation и durable pending identity.
- [ ] Wiki version invalidation, search/article/PDF через private authenticated download; Markdown links/code/selection.
- [ ] Attachments соблюдают source limits; tests partial UTF-8/SSE chunks, reconnect/history race, malformed documents, inflation/cancel и tool permission refusal.
- [ ] Windows runtime: Return/Shift+Return/IME, file/drop/paste, русский speech capability, microphone denial и рабочий текстовый fallback.

**Готово:** прежний assistant protocol; отсутствие offline speech честно отражено при неподдерживаемой capability.

### Этап 10. Полная админка, ГСМ, обратная связь и настройки

**Файлы:** `Core/Admin/`, `Core/Feedback/`, `Features/Admin/`, `Features/Feedback/`, необходимые GSM/settings units; permission/admin/feedback recovery tests.

- [ ] Инвентаризировать реальные backend permissions/DTO для каждой группы из раздела 6; реализовать все подтверждённые операции.
- [ ] Source editor сохраняет unknown JSON; read-only snapshot/IPA areas, chunks, template/layout/project и image request states не переопределять для Windows.
- [ ] Guard на каждую action/command; role change/403 сбрасывает grants/cache; preflight conflict на whole-object editors.
- [ ] Feedback draft/chunk/submit/additions, устойчивый ID, disk draft registry и recovery после close/relaunch; Windows metadata в прежних keys.
- [ ] Tests: permission revoked between chunks, stale GET after mutation, unknown submit after restart, addition retry, source metadata preservation и remote editor conflict.
- [ ] Закрыть настоящие profile/route/GSM/notification/security/appearance settings; destructive confirmations называют объект/период. Никаких production deletes/mailing в обычном QA.

**Готово:** все реальные admin/feedback/GSM workflows, без fake destinations и обхода backend authority.

### Этап 11. Windows notifications и read-only системные сценарии

**Файлы:** Windows notification/deep-link adapters; widget/provider files только после подтверждения нужной capability; notification/query/activation tests.

- [ ] Existing preferences/events/dedup, no stale notification after logout, activation открывает разрешённый scope текущего аккаунта.
- [ ] Existing voice/read-only query semantics проверены через core; зарплата требует действующей защиты, Siri/App Intents на Windows не обещаются.
- [ ] Windows Widgets оценить отдельно: API, packaging/identity, protected/logged-out states и deep link. Apple widget не копируется как готовый Windows feature.
- [ ] Уведомления при завершённом процессе не заявлять без подтверждённого действующего транспорта. Не добавлять серверную регистрацию push, tray daemon или автозапуск.

**Готово:** системные интеграции имеют отдельное capability/runtime evidence; unsupported surfaces перечислены.

### Этап 12. Итоговые проверки Windows 11 и installable candidate

**Файлы:** существующие test suites, `scripts/package-windows.ps1`, acceptance report и release manifest/checksums только с реальными результатами.

- [ ] Сверить section/action matrix; пройти failure cases раздела 9 и полный contract/domain regression после последних изменений.
- [ ] На Windows 11 x64 проверить launch/login, два окна/drafts, file dialogs/preview, Hello/PIN, lock/wake/quit, accessibility/DPI/themes и большой архив на 8 ГБ RAM.
- [ ] Выбрать package identity/publisher, .NET/Windows App SDK/WebView2 dependency deployment; собрать подписанный MSIX и проверить trust/install/upgrade/uninstall на чистом host без dev tools.
- [ ] Различать compile artifact, unsigned package, dev-signed test install и public trusted installer. Наличие self-signed CI certificate не означает доверенную публичную дистрибуцию.
- [ ] Проверить version/update-email ограничения, secret-free artifact, реальную архитектуру, checksum и отдельно разрешённый public download после публикации.
- [ ] Git diff/status показать пользователю; commit/push/release только по поручению. iOS SideStore, Mac DMG и VPS не обновляются этой сборкой.

**Готово:** каждый заявленный результат связан с проверкой; map/speech/widget/ARM64 и прочие незакрытые пункты явно перечислены.

## 11. Команды и уровни доказательства

Это будущие команды после создания названных проектов, не результаты текущего запуска.

На Mac после установки .NET SDK:

```bash
dotnet --list-sdks
dotnet test tests/Engineer.Core.Tests/Engineer.Core.Tests.csproj -c Release
```

На Windows runner: сначала подтвердить версии SDK/MSBuild и совместимость template; затем build script вызывает проверенный VS 2026 MSBuild. Ориентир для первого Debug compile:

```powershell
dotnet test tests/Engineer.Core.Tests/Engineer.Core.Tests.csproj -c Release --logger trx
msbuild src/Engineer.Windows/Engineer.Windows.csproj /restore /p:Configuration=Debug /p:Platform=x64 /p:AppxPackageSigningEnabled=false /p:GenerateAppxPackageOnBuild=false /bl:artifacts/windows-build.binlog
```

Compile без package signing/generation не является готовым установщиком. Windows script должен явно создавать каталог diagnostics и корректно завершаться при ненулевом exit code; конкретные template properties подтвердить этапом 1.

После появления repo/workflow управление с Mac:

```bash
gh workflow run windows-ci.yml
gh run list --workflow windows-ci.yml --limit 5
gh run watch <run-id> --exit-status
gh run download <run-id> --dir .codex-tmp/windows-artifacts
```

Не повторять тяжёлые проверки без причины. Core/contract tests проверяют semantics; Windows build — compiler/XAML; Windows 11 runtime — системное поведение; install/upgrade — distribution. Production smoke выполняется отдельно и только в разрешённом объёме.

## 12. Матрица готовности и ближайший шаг

- [ ] Source baseline/fingerprints и fixtures соответствуют текущим контрактам.
- [ ] Собственная сборка не зависит от iOS/macOS checkout.
- [ ] .NET core tests работают на Mac, Windows CI воспроизводит native build.
- [ ] Все 12 разделов, permission-aware «Админка» и вложенные workflows реализованы.
- [ ] Auth/scoped caches/generation guards/multiwindow drafts проверены.
- [ ] UTC/XLSX/SLA, фильтры, финансовые расчёты и sync/import recovery имеют parity evidence.
- [ ] Route lost reply и одновременная запись не создают ложных обещаний/дублей.
- [ ] Map provider выбран и встроенный расчёт подтверждён либо явно остаётся ограничением preview.
- [ ] Protected content, Windows files/Hello/browser/sleep/lock проверены на Windows 11.
- [ ] Assistant/Wiki/attachments и speech fallback работают с прежним протоколом.
- [ ] Подписанный пакет установлен и обновлён на чистой Windows; ограничения выпуска записаны.

**Ближайший шаг:** этапы 0–1: .NET SDK на Mac, локальный Git/доступный GitHub repo по поручению пользователя, минимальный WinUI solution и Windows Actions build. Для начала написания исходников отдельный Windows PC не обязателен; до заявления рабочего интерфейса нужен подтверждённый Windows runtime host. Публичный сертификат не блокирует первую compile-проверку, но release-подпись остаётся отдельным условием дистрибуции.

## 13. Журнал и передача следующему чату

### 14.10.2026 — подготовка плана и проверка окружения

- Создан только `WINDOWS_IMPLEMENTATION_PLAN.md` в корне. Windows implementation ещё не начата; исходные iOS/macOS/backend files не изменялись.
- Найден и прочитан локальный Mac-план, source/fixture references и актуальные изменения дат/зарплаты/admin; исторические Mac permissions/statuses не перенесены в Windows.
- Проверены Mac OS/architecture, Git, GitHub CLI/API account, Xcode; .NET/PowerShell/MSBuild и названные VM apps в проверенных местах не найдены.
- В текущей папке нет Git. Попытка чтения `septoon/lumawork-windows` завершилась невозможностью разрешить repo; его существование/доступность/Actions не подтверждены.
- По официальным Microsoft/GitHub docs проверена возможность WinUI/.NET 10 development и Windows-hosted CI; workflow не создан и не запускался.
- Продолжать с этапа 0. Не устанавливать секреты и не публиковать release под видом подготовки CI; результаты каждой будущей проверки фиксировать здесь с конкретной конфигурацией и оставшимися gaps.
