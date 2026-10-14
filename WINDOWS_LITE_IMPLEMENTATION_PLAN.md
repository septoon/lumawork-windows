# «Инженер» для Windows Lite — план реализации

> **Для исполнителя:** выполнять этапы последовательно по `superpowers:executing-plans`. Отмечать пункты только после фактической проверки. Текущая задача — планирование; код приложения и CI ещё не созданы. Разработка начинается с Lite, полный вариант остаётся отдельным продуктом по [полному плану](WINDOWS_IMPLEMENTATION_PLAN.md).

**Цель:** небольшой Windows-инструмент для подготовки месячных ГСМ-отчётов через существующий сервер LumaWork: импорт реквизитов из готового XLSX, работа с данными месяца и минимальный архив закрытых заявок.

**Архитектура:** собственное переносимое .NET-ядро и WinUI 3 UI. Обязательная сессия LumaWork для профиля, маршрутов, заправок и формирования отчёта; отдельная необязательная сессия SimpleOne для автоматического обновления закрытых заявок. Реквизиты из XLSX распознаются локально, проходят preview и подтверждение перед сохранением через существующий API.

**Предлагаемый стек:** C#, .NET 10, WinUI 3, stable Windows App SDK, HttpClient/System.Text.Json; чтение XLSX через ZIP/XML, без установленного Excel; xUnit для ядра. Стек и Windows CI наследуют решения полного плана. WinUI и package собираются на Windows, редактирование/core tests доступны с Mac после установки SDK.

**Спецификация:** требования пользователя и уточнения 14.10.2026, разделы 1–8 этого документа. Исходные бизнес-правила — существующие iOS/GSM/backend sources, перечисленные в разделе 9. При расхождении scope полный план не расширяет Lite.

**Дата:** 14.10.2026. **Статус:** локальная часть этапа 0 подготовлена: solution/core/WinUI scaffold, 24/24 core tests, dependency locks и CI workflow. Windows XAML build, deployment/startup и GitHub Actions run ещё не подтверждены. Этапы 1–7 впереди.

## 1. Подтверждённый scope

Пользователь определил:

- Будут две версии: полная и Lite. Начинаем с Lite.
- Основное назначение Lite — создание месячных ГСМ-отчётов.
- Исходный XLSX — **готовый ГСМ-отчёт за прошлый месяц**, не выгрузка заявок SimpleOne. При этом файл может относиться к любому месяцу, необязательно непосредственно предыдущему.
- Новый отчёт формируется **через существующий сервер LumaWork**, не локальным генератором.
- Оставить «Главную» и минимальные «Заявки»: закрытые заявки, на экране только номера и адреса.
- Архив заявок сначала наполняется ручным XLSX-импортом; далее обновляется автоматически, ручной импорт остаётся доступным.
- Исключить экраны «Мой рюкзак», «Сотрудники», «Авто», «Топливо», «Помощник», «FTP», «Зарплата», «Координация», «Трудозатраты», «Аналитика», «Админка». Полноценный экран «Заявки» также исключён и заменён минимальным.

Служебные формы входа, реквизитов ГСМ, импорта и подключения SimpleOne открываются из двух основных экранов; не являются дополнительными разделами навигации.

## 2. Две версии и границы Lite

Предлагаемая организация: один Windows checkout, отдельный Lite UI project/target и собственная package identity. Переносимые домены могут использоваться полной версией позднее, внутри этого же checkout. Зависимость сборки от iOS/Mac/backend checkout запрещена.

- Пользовательское имя Lite — предлагаемое «Инженер Lite»; Windows publisher/identity проверить перед упаковкой.
- Lite и полная версия имеют отдельные local cache/settings/credential namespaces; установка одной не заменяет другую.
- Серверные данные привязаны к существующему LumaWork user: изменения реквизитов, маршрутов и заправок видны другим клиентам этого аккаунта. Изолировать их новым фиктивным серверным namespace нельзя.
- Не создавать сейчас full UI target и исключённые модули «на будущее». Не компилировать скрытые admin/assistant/salary screens в Lite.
- Не переносить фоновый monthly mileage→fuel side effect только ради паритета: для Lite нужны маршруты и заправки отчёта, без отдельной финансовой модели топлива.
- Backend/VPS, iOS и macOS остаются read-only. Разрешения на commit/push/deploy из соседних проектов не действуют для Lite.
- Прямое указание 14.10.2026: этот проект публикуется в **public** GitHub repo; private repo не предлагать. Перед публичным push проверять весь коммит на секреты и личные данные. Пользователь явно разрешил текущий push и подготовку GitHub CI.

## 3. Пользовательский сценарий

### Первый запуск

1. Вход в LumaWork прежним email-code flow. Это определяет серверный аккаунт; XLSX не является способом авторизации.
2. Загрузка готового ГСМ XLSX любого месяца.
3. Preview: найденные реквизиты, месяц исходного файла, откуда прочитано каждое поле, отсутствующие/неоднозначные значения и отличия от текущего серверного профиля.
4. Пользователь проверяет/дополняет данные, выбирает проекты при неоднозначном сопоставлении и явно сохраняет реквизиты ГСМ.
5. Выбор **целевого месяца** на «Главной». Он не связан автоматически с месяцем исходного файла.
6. Загрузка существующих маршрутов/заправок выбранного месяца; подготовка недостающих данных на «Главной».
7. По отдельной кнопке — формирование и отправка месячного отчёта через сервер.

Импорт реквизитов не копирует старые маршруты, заправки и пробег в новый месяц, не запускает генерацию/рассылку и не заменяет profile без подтверждения.

### Повторный запуск

Сохранённые подтверждённые реквизиты и месячный snapshot показываются из кеша, затем обновляются. Повторный XLSX нужен для смены/обновления реквизитов; первоначальная настройка не повторяется автоматически. У аккаунта с готовым серверным профилем импорт доступен без принудительной перезаписи профиля.

### Два независимых импорта

| Импорт | Назначение | Результат |
| --- | --- | --- |
| Готовый ГСМ-отчёт XLSX | Первичная настройка/обновление реквизитов | Локальный profile preview → подтверждённый PUT профиля |
| Выгрузка закрытых заявок SimpleOne XLSX | Первичное наполнение/ручное восстановление архива | Preview eligible records → merge/upsert локального архива |

Не угадывать назначение выбранного файла между этими действиями. Неверный тип/структура дают объяснимую ошибку; предыдущие данные сохраняются.

## 4. «Главная» — подготовка месяца

Компактный рабочий экран содержит:

- Месяц отчёта; состояние подключения/последнего обновления; действия «Реквизиты ГСМ», «Импортировать ГСМ XLSX», «Сформировать и отправить отчёт».
- Сводку данных месяца: маршруты, пробег, заправки и наличие неподтверждённых локальных изменений. Это проверка входов отчёта, не отдельная аналитика.
- Список дней/маршрутов с POS/АРМ, адресами, номерами заявок и километражем; editor выбранного дня сохраняет существующий route contract.
- Начальный одометр периода с источником подсказки и возможностью подтверждения/ручной коррекции через поддерживаемый route payload.
- Небольшую область «Заправки для отчёта»: дата, тип топлива, литры, стоимость, добавить/исправить запись. Это необходимые входы отчёта без экрана «Топливо», выплат, долгов и carryover.
- Результат последней операции отчёта и сведения о текущем выбранном месяце; без глобальной истории/нового центра отчётов.

Данные месяца вводятся отдельно от реквизитов исходного XLSX. Приложение не создаёт маршруты автоматически из факта закрытия заявки: закрытая заявка сама по себе не доказывает поездку, её дату и километраж.

### Сохранение и формирование

- «Сохранить день/заправку» и «Сформировать и отправить отчёт» — разные действия. Генерация не выполняет скрытые writes всех открытых draft.
- Перед отчётом показать конкретный месяц/email и незавершённые сохранения. Отчёт запускается только после успешного завершения выбранных пользователем сохранений и обновления server snapshot.
- Ошибка сохранения одного входа не запускает отчёт по частично подготовленным данным. Сервер не предоставляет общей транзакции профиля, дней и заправок; успешные отдельные writes не откатывать вслепую.
- При загрузке существующих маршрутов из полной версии сохранять скрытые поля остановок/summary/overrides. Редактирование двух видимых колонок не должно обнулять `org`, `tid`, `reason`, statuses и прочие реальные transport fields.
- Незавершённые local drafts не считаются уже сохранёнными на сервере. Генератор читает серверные записи, а не память Lite.
- MapKit и автоматический расчёт расстояния не включаются в первый Lite scope. Километраж вводится/проверяется вручную; существующая внешняя ссылка Яндекс.Карт может использоваться без нового routing API.
- Отсутствие заправок может быть корректным пустым месяцем по существующему генератору; не подставлять фиктивную заправку.
- Текущий шаблон имеет 31 route row (`ПЛ-ТК`, строки 11–41). POS и ARM одного дня дают отдельные записи; превышение лимита показать до отправки, не обрезать и не выдумывать новый формат.

## 5. Импорт реквизитов из готового ГСМ XLSX

### Что действительно можно прочитать

Карта ниже получена из текущего `fill_report.py`, функций `write_fixed_pl_tk`, `write_fixed_sz_tk`, `write_fixed_report` и row-compaction. Это карта базового формата, **не обещание одинаковых координат всех исторических файлов**.

| Поле | Источник базового формата | Правило |
| --- | --- | --- |
| Полное ФИО | `ПЛ-ТК!C44`, `Отчет по ТК!A3` | Сверить доступные значения; при конфликте показать выбор |
| Должность/компания | `ПЛ-ТК!E5` | Составная строка с ФИО; распознать надёжные части, недостающие подтвердить вручную |
| Адрес/телефон | `ПЛ-ТК!E6` | Телефон нормализовать по прежнему формату `+7(XXX)XXX-XX-XX`; неоднозначный адрес не разделять произвольно |
| Водительское удостоверение | `ПЛ-ТК!AC5` | Строка, без потери ведущих нулей |
| Топливная карта | `ПЛ-ТК!AC6`, `Отчет по ТК!D3` | Строка; конфликт не скрывать |
| Модель автомобиля | `ПЛ-ТК!J7`, `Отчет по ТК!B3` | Только текстовый реквизит, не server vehicle ID |
| Госномер | `ПЛ-ТК!AE7` | Реквизит, без выдуманного поиска/создания автомобиля |
| Норма расхода | `Отчет по ТК!C3` | Число с корректной русской запятой, строго положительное по API |
| Виды топлива | Custom property `LumaWorkGsmFuelTypes`, подтверждённые строки fuel sheet | Сопоставление с API `availableFuelTypes`; не терять несколько типов и не выводить тип из отсутствующих операций |
| Краткое ФИО сотрудника | Базовый `СЗ-ТК!Q51`, после compaction — смещённый footer | По format/anchors; не использовать имя файла как гарантированное значение |
| Подпись сотрудника | Базовый `Отчет по ТК!A41` с префиксом «ФИО» | Учитывать сдвиг после compaction |
| Уполномоченный | `ПЛ-ТК!C47`, базовый `СЗ-ТК!H59` | Полное/краткое значения отдельно; отключённые блоки могут отсутствовать |
| Месяц исходного отчёта | `ПЛ-ТК!M3/Q3`, даты/подписи других листов | Проверить согласованность; конфликт требует выбора пользователя |
| Начальный/конечный одометр исходного месяца | `ПЛ-ТК!N8/AE8` | Не переносить конечный пробег старого произвольного месяца как начало целевого месяца |
| Проекты/бюджет POS/ARM | Route budget column `ПЛ-ТК!D11:D41`, hidden type `AJ`, project sheet и СЗ-ТК по формату | Только предложения для сопоставления со справочником сервера, не готовые project IDs |

Названия листов нормализовать с учётом trailing spaces: source использует в том числе `Отчет по ТК `. Читать реальный workbook relationship map; не считать `sheet1.xml` нужным листом.

Профиль сохраняет source hash, importer version и provenance каждого найденного поля. Preview state отделён от server profile. Старые/неполные реквизиты можно дополнить в форме; импорт не обещает восстановить отсутствующие поля.

### Что нельзя надёжно восстановить автоматически

- LumaWork user/email/token и SimpleOne credentials — не читаются из отчёта и не заменяются ФИО.
- `vehicleId`, `posProjectId`, `armProjectId` — server IDs отсутствуют. Загружать справочник проектов; при нескольких совпадениях или конфликте выбирать вручную.
- `reportStartMonth` — бизнес-настройка начала ведения отчётов, не обязательно месяц загруженного XLSX. Существующее значение сохраняется; для нового profile пользователь задаёт его явно.
- Compound fields, недостающие подписи и выключенные layout blocks не дают полной исходной profile structure. Не подставлять чужого/исторического куратора константой.
- Formula cached value может отсутствовать. Не выполнять произвольные formulas/macros/external links; для известной прямой ссылки допустим контролируемый resolver, иначе значение остаётся unresolved.

### Согласование с текущим серверным профилем

1. GET profile/projects и допустимых fuel types; сравнить найденные поля с текущими.
2. Показать отличия, выбранные projects, существующую vehicle link и последствия сохранения для других клиентов.
3. PUT только после явного подтверждения; сохранённые IDs не обнулять автоматически, но учитывать, что linked vehicle может переопределить импортированную модель/номер/настройки.
4. Прочитать сохранённое состояние и проверить результат. Иначе UI не должен показывать «импортировано» только по локальному preview.

Серверный `PUT /gsm/profile` также обновляет linked `gsmVehicleSettings`. Автоматически отвязывать/переключать автомобиль нельзя. При конфликте XLSX с linked vehicle пользователь должен увидеть его до PUT.

Текущий backend сохраняет существующий `defaultStartOdometer` при PUT, даже если клиент передал другой. Не обещать изменение существующего начального пробега через этот endpoint. Для target month используются поддерживаемые route `periodStartOdometer` и server suggestion; точное применение проверить contract tests.

Для месяца, непосредственно следующего за source month, конечный одометр XLSX можно предложить как подсказку после сверки с server suggestion. Для иных месяцев не делать автоматического переноса; missing/conflicting values требуют ручного подтверждения, не zero fallback.

### Безопасность/устойчивость чтения

- Чтение локальное и read-only; файл пользователя не изменяется и целиком не отправляется как профильный import.
- ZIP/XML parser обрабатывает shared/inline strings, numbers, dates, merged anchors и cached formula values; защищён от DTD/external entities, path traversal и decompression limits.
- Предлагаемый предел profile import: 6 MiB compressed, 64 MiB unpacked, 16 MiB entry, 512 entries, 32 worksheets, 50 000 cells. Эти значения совпадают с текущими лимитами server fuel importer; для profile parser закрепить в tests, не считать автоматической серверной поддержкой profile import.
- Неподдержанный format, повреждённый/encrypted workbook, неверный месяц и отказ чтения сохраняют прежний profile/draft.
- Personal workbook не копируется в repo/CI. Fixtures синтетические: старый/новый формат, несколько топлив, пустой месяц, disabled blocks, compacted footers и отсутствующий formula cache.

## 6. «Заявки» — минимальный закрытый архив

### Видимые данные и действия

- Только две колонки: **«Номер заявки»** и **«Адрес»**.
- Поиск по номеру/адресу; период и статус покрытия архива; ручной import и обновление/подключение SimpleOne.
- Копирование номера/адреса; выбор заявки для добавления в редактируемый маршрут на «Главной» с явным подтверждением даты. Добавление не запускает network write.
- Нет карточки заявки, описаний, comments/ИНН, active/warehouse modes, координации, трудозатрат и аналитики.

Внутри записи хранится минимум, необходимый для eligibility/date/sync: stable ID/`sys_id`, номер, адрес, status, request type, closed/registered timestamps, `sys_updated_at` и scope metadata. Два видимых поля не позволяют корректно синхронизировать архив без этой служебной информации. Raw description может читаться для извлечения адреса, но не требуется показывать/постоянно хранить целиком.

### Фильтрация

- В Lite включать только закрытую/завершённую группу по существующему `isIncludedStatus`: `completed`, `closed`, `resolved`, русские выполнена/закрыта/решена и отказ/отклонение.
- **Уточнение Lite по требованию «только закрытых»:** source исключение `returnEquip` не даёт права включать открытую заявку возврата. Для неё также нужен допустимый terminal status; остальные closed status semantics сохраняются.
- Effective date для допустимого закрытого `returnEquip` — корректный `registeredAt`, иначе `closedAt`. UTC API и уже локальные XLSX timestamps различаются.
- Непустой поиск снимает period gate и ищет по всему доступному личному архиву. Если архив неполон, это явно отражено.
- Номер/stable ID определяют merge; импортные повторы используют детерминированное source правило последней строки, без mirror delete отсутствующих заявок.

### Ручной bootstrap → автоматическое обновление

1. Первоначальная загрузка архива — ручной XLSX пользователя. Preview показывает eligible/rejected rows и ошибки; commit локального merge только после проверки.
2. Без SimpleOne login архив доступен локально, auto-refresh честно выключен. Вход LumaWork не является входом SimpleOne.
3. После отдельного SO login связать archive scope с identity. Исторический ручной XLSX не содержит надёжного `sys_updated_at` baseline; не выдавать его import timestamp за watermark.
4. Запустить прежний reconciliation/recovery path: сброс narrow/wide baselines/generation, `requiresNarrowFullReconciliation`; wide delta только при достоверном baseline. Политика полного bootstrap/export и narrow/wide берётся из current source, не из предположения «любая XLSX равна полной базе».
5. Далее прежняя delta policy `(sys_updated_at, sys_id)`, cadence 60/120/300 секунд и wide check шесть часов, один refresh owner. Авто работает при открытом приложении и действующей SO-сессии.
6. Manual reimport остаётся доступным; pause/serialize с auto-refresh, upsert/reset generation и последующий reconciliation. Late batch не перезаписывает более новый manual import.

При смене SimpleOne account не показывать чужой archive автоматически. Manual import без SO identity остаётся scoped к app user и несвязанному local archive; перед связыванием с SO показать владельца/предупреждения. Нельзя гарантировать владельца выгрузки только по её имени или ненадёжному полю исполнителя.

Записи, исключённые из-за status, отличать от ошибок parsing/загрузки: source eligibility exclusions не должны терять watermark/recovery корректность. Переход записей в/из terminal status проверять отдельно, не вводить произвольное удаление всего snapshot.

## 7. Минимальный серверный контур

Проверены локальные declarations. Production availability и authenticated runtime в этой задаче не проверялись.

| Операция | Existing API | Правило Lite |
| --- | --- | --- |
| Вход/выход/identity | Прежний email-code auth, `/me`, `/auth/logout` | Только необходимые auth функции, без нового профиля продукта |
| Реквизиты | `GET/PUT /api/v2/gsm/profile` | Preview/merge/confirmation; backend validation и linked vehicle effects учитываются |
| Проекты | `GET /api/v2/gsm/projects` | Read-only selection внутри формы реквизитов, без админки справочника |
| Начальный одометр | `GET /api/v2/gsm/odometer-suggestion?month=YYYY-MM` | Подсказка имеет источник и не отменяет проверку target month |
| Маршруты | `GET /api/v2/routes?from=...&to=...`, прежние `POST /api/v2/routes`, `PUT /api/v2/routes/:date` | `date/workType` key, полные hidden fields и read-back reconciliation |
| Заправки | `GET/POST /api/v2/fuel`, `PUT /api/v2/fuel/:id` | Только refuel inputs отчёта; не новый Fuel dashboard/финансовые операции |
| Формирование | `POST /api/v2/gsm/report` | Прежнее тело `{ month }`; explicit confirmation, без blind retry |
| Статус после неопределённого ответа | `GET /api/v2/gsm/jobs` | Сверять user/month/time/result; неоднозначное совпадение не считать своим успешным запросом |
| Заявки | Existing SimpleOne login/list/cell-value/detail/XLSX contracts | Только личный закрытый scope и минимум нужных полей; не добавлять выдуманный Lite endpoint |

`POST /gsm/report` не принимает произвольный workbook/profile/month payload вместо данных базы. Новый server profile import endpoint не требуется: локальный parser строит draft, подтверждённый profile сохраняется обычным PUT.

Формирование сейчас запускает генератор и отправку на email текущего аккаунта. `success`, `generated`, `sent`, `status`, `outputFileName`, `sentToEmail` различаются. Подтверждение SMTP-приёма не равно фактическому получению письма пользователем. Не добавлять кнопку «Скачать XLSX» без подтверждённого user-scoped file download contract: source response содержит имя/статус, а не готовый binary/URL.

Сервер выбирает route records по месяцу и валидные fuel records, но **не фильтрует дни только по `sent=true`** в прочитанном `loadGsmReportInput`. Поэтому локальный gate «отправленный день» не является server report boundary. Показывать действительно сохранённые входы, сохранять прежнюю route semantics и не обещать, что `sent=false` исключает строку отчёта.

Profile PUT, refuel create и report request не становятся идемпотентными от local UUID. Для timeout сначала проверять server state/jobs; повторную отправку подтверждает пользователь после проверки. Jobs endpoint не предоставляет доказанной атомарной привязки к произвольному local request ID.

Сохранить Windows version/build headers. Existing server version metadata общие для платформ; не притворяться iOS и не вводить platform updater/feed в Lite.

## 8. Состояние и обязательные проверки

- Session-owned repositories/coordinator; UI filters, selection и drafts принадлежат окну. Начальный Lite UX — одно главное окно, без отдельной multiwindow feature.
- Account/SO-scoped cache, private credential storage, atomic writer и versioned snapshots; cache-first без пустого экрана при повторном входе.
- На profile/route whole-object writes preflight/current baseline; refresh не затирает dirty draft. Нет общей server CAS — конфликт между Lite и iOS/Mac выявлять, но не обещать строгую блокировку.
- Logout блокирует local data access, cancels requests/sync; late response не попадает в следующую сессию. 401/403 не маскируются offline-кешем.
- F5 обновляет разрешённые данные, Ctrl+F фокусирует локальный поиск, Ctrl+S сохраняет draft. Ни одна shortcut не формирует/отправляет месячный отчёт без подтверждения.
- Offline позволяет подготовку доступных drafts и локальный импорт; profile/routes/refuels/report остаются неподтверждёнными до server success.
- Нет background service/tray/startup task. Sleep checkpoint, wake catch-up; после закрытия приложения auto-refresh не обещается.
- Светлая/тёмная/system theme, keyboard/Narrator, DPI 100/150/200%, virtualized rows. Никаких тяжёлых UI dependencies и WebView2 только ради двухколоночного списка.

| Риск | Проверка |
| --- | --- |
| Historical/compacted XLSX или отключённый блок | Importer не читает чужую ячейку как ФИО; unknown/missing остаётся явным |
| Older report принят за данные целевого месяца | Реквизиты переносимы, old routes/refuels и одометр не переносятся автоматически |
| Profile PUT затронул существующий vehicle/config | Preview и read-back показывают реальные значения/связи; IDs не обнулены |
| Lite обнулил скрытые route fields полной версии | Edit + request fixture сохраняют исходные fields/overrides |
| Manual archive import пересёкся с delta/account switch | Generation guards, missing baseline и isolation tests |
| Timeout route/refuel/report вызвал дубль | Read-back/jobs/unknown outcome; automatic retry отсутствует |

## 9. Источники и карта файлов

Read-only sources:

- [iOS GsmReportFeature.swift](../LumaWork/LumaWork/LumaWork/GsmReportFeature.swift): profile DTO, validation, report API/result and odometer suggestion.
- [server.ts](../LumaWork/deploy/lumawork-api/backend/src/server.ts): profile/projects/routes/fuel/report/jobs declarations и profile PUT effects.
- [gsmReports.ts](../LumaWork/deploy/lumawork-api/backend/src/gsmReports.ts): DB inputs, validation, generation/email and suggestion.
- [fill_report.py](../LumaWork/deploy/lumawork-api/generator/fill_report.py): worksheet cells, layout compaction, custom properties и route row limits.
- [fuelImports.ts](../LumaWork/deploy/lumawork-api/backend/src/fuelImports.ts): ZIP/XML/date/fuel format, limits и import warnings. Это **fuel importer**, не готовый полный profile importer.
- [v2Writes.ts](../LumaWork/deploy/lumawork-api/backend/src/v2Writes.ts): route/fuel schemas и whole-route upsert.
- [ClosedRequestsXLSXRequestsParser.swift](../LumaWork/LumaWork/LumaWork/ClosedRequestsFeature/ClosedRequestsXLSXRequestsParser.swift), [ClosedRequestsIncrementalSync.swift](../LumaWork/LumaWork/LumaWork/ClosedRequestsFeature/ClosedRequestsIncrementalSync.swift), [CLOSED_REQUESTS_SYNC.md](../LumaWork/CLOSED_REQUESTS_SYNC.md): archive parsing, eligibility, dates, baselines и delta.

Планируемые пути относительно текущего корня, создавать только по мере этапов:

```text
EngineerLite.sln
global.json
src/Engineer.Core/Engineer.Core.csproj
src/Engineer.Core/Auth/
src/Engineer.Core/Gsm/GsmModels.cs
src/Engineer.Core/Gsm/GsmProfileApi.cs
src/Engineer.Core/Gsm/GsmReportApi.cs
src/Engineer.Core/Gsm/GsmProfileImport.cs
src/Engineer.Core/Spreadsheet/XlsxReader.cs
src/Engineer.Core/Routes/RouteRepository.cs
src/Engineer.Core/Fuel/ReportRefuelRepository.cs
src/Engineer.Core/ClosedRequests/ClosedRequestsImport.cs
src/Engineer.Core/ClosedRequests/ClosedRequestsSync.cs
src/Engineer.Core/Persistence/
src/Engineer.Lite.Windows/Engineer.Lite.Windows.csproj
src/Engineer.Lite.Windows/App.xaml(.cs)
src/Engineer.Lite.Windows/Views/MainWindow.xaml(.cs)
src/Engineer.Lite.Windows/Features/Home/
src/Engineer.Lite.Windows/Features/ClosedRequests/
src/Engineer.Lite.Windows/Forms/    # login, SO connection, profile/import/daily editors
src/Engineer.Lite.Windows/Platform/ # credential/storage/file picker/lifecycle adapters
src/Engineer.Lite.Windows/Package.appxmanifest
tests/Engineer.Core.Tests/Engineer.Core.Tests.csproj
tests/Engineer.Core.Tests/Fixtures/
scripts/build-windows-lite.ps1
.github/workflows/windows-lite-ci.yml
```

Ядро — `net10.0`, без WinUI/WinRT. DTO/adapters/files для поддерживающих типов размещать рядом с владельцем, без общей framework-архитектуры. Не создавать пустые directories/screens исключённых функций.

## 10. План реализации

Порядок: сборочный scaffold → реальный login/profile → importer → первая рабочая генерация месяца → optional archive import → automatic SO sync → release. Closed requests ускоряют заполнение маршрута, но не блокируют report tool с ручными адресами.

Для сложных parsing/sync/write правил сначала fixture/test, затем implementation на реальных C# sources. После каждого этапа targeted tests и Windows build по необходимости; не запускать визуальную/production матрицу на каждой правке.

### Этап 0. Минимальный Lite scaffold и CI

**Файлы:** solution/core/lite/test projects, App/MainWindow/manifest, `global.json`, build script и workflow из раздела 9.

- [x] Зафиксировать hashes используемых source файлов; reference repos имеют незакоммиченные изменения, поэтому одного HEAD недостаточно. Manifest: `tests/Engineer.Core.Tests/Fixtures/source-baseline.json`.
- [x] Установить .NET SDK arm64 на Mac: 10.0.402 в `~/.dotnet`; создать локальный Git, branch `feat/windows-lite-bootstrap`, без commit/remote.
- [ ] Создать/указать GitHub repo и выполнить подтверждённые commit/push для CI.
- [x] Core test `LiteScope_HasOnlyHomeAndClosedRequests` фиксирует две domain destinations; исключённые функции отсутствуют в UI composition. Restore/config tests: 24/24 PASS.
- [x] Создать исходники минимального native окна, предложенную development Lite identity и реальную version/config; без fake month/profile data. Это source scaffold, не Windows runtime proof.
- [x] Подготовить workflow/build script: SDK/toolchain diagnostics → restore → core tests → Debug x64 XAML build → TRX/binlog/build artifacts. Actionlint и PowerShell syntax/host guard PASS; MSIX launch profile validation PASS.
- [ ] Запустить GitHub CI и подтвердить native XAML build/artifact; отдельно deployment/startup на Windows 11.

**Готово:** Lite отдельно собирается, стартовое окно проверено на Windows; полный UI target не создан.

### Этап 1. LumaWork auth, profile и scoped storage

**Файлы:** `Core/Auth/`, `Core/Gsm/GsmModels.cs`, `GsmProfileApi.cs`, `Core/Persistence/`, login/profile forms и Windows platform adapters.

**Интерфейс:** profile client предоставляет `FetchProfileAsync(CancellationToken)`, `FetchProjectsAsync(CancellationToken)`, `SaveProfileAsync(GsmProfile, CancellationToken)`; precise JSON properties/envelopes берутся из source. Типы результата объявляются рядом с client до использования UI.

- [ ] Тесты `AuthScopes_AreIndependent`, `LateResponse_DoesNotCrossAccount`, `ProfilePut_PreservesSelectedIdsAndRequiredContacts`, `SnapshotWriteFailure_PreservesPreviousData`.
- [ ] Реализовать existing email code/me/logout и cache-first profile restore; credentials в отдельном Lite namespace.
- [ ] Форма реквизитов с настоящими server fields/projects/fuel types, validation/missing states; никаких default чужих ФИО/проекта/нормы.
- [ ] Проверить restart/logout/profile read-back; live profile write только с разрешённым пользователем аккаунтом и значениями.

**Готово:** пользователь входит, видит реальные реквизиты и может подтверждённо их сохранить.

### Этап 2. Импорт реквизитов ГСМ XLSX

**Файлы:** `Core/Spreadsheet/XlsxReader.cs`, `Core/Gsm/GsmProfileImport.cs`, supporting import DTO рядом, profile import preview form; `GsmProfileImportTests.cs`, `XlsxBoundaryTests.cs`.

**Интерфейс:** `ParseAsync(Stream xlsx, string fileName, CancellationToken)` возвращает `GsmProfileImportPreview`: source hash/month, candidates с sheet/cell provenance, missing/ambiguous fields и warnings. Метод не выполняет network/disk profile commit.

- [ ] Synthetic tests `CurrentReport_ExtractsProfileFields`, `CompactedFooters_AreLocated`, `DisabledBlock_StaysMissing`, `ConflictingCells_RequireChoice`, `MissingFormulaCache_IsNotZero`, `OldMonth_DoesNotSeedTargetMonth`.
- [ ] ZIP/XML tests: shared/inline strings, relationship map/trailing sheet space, limits, traversal, external entities и corrupt/encrypted input.
- [ ] Mapping раздела 5, безопасное compound-field parsing и preview/correction; source IDs/неоднозначные значения не генерируются.
- [ ] Сравнение с existing profile, projects и vehicle effects; explicit confirm → ordinary profile PUT → read-back.
- [ ] `CancelPreview_PerformsNoServerWrites` и `RepeatedImport_DoesNotChangeServerUntilConfirmed`; initial/target month и odometer provenance проверены отдельно.

**Готово:** готовый XLSX сокращает ручную настройку профиля, отсутствующие реквизиты остаются редактируемыми и явными.

### Этап 3. Подготовка маршрутов и заправок месяца

**Файлы:** `Core/Routes/RouteRepository.cs`, `Core/Fuel/ReportRefuelRepository.cs`, `Features/Home/` и daily/refuel forms; route/refuel contract/state tests.

**Интерфейсы:** route repository загружает диапазон month→from/to и держит records по date/workType; refuel repository читает existing records и локально выбирает refuels target month. UI drafts не являются shared repository state.

- [ ] Month/day/POS/ARM editor, адреса/номера, ручной километраж и period odometer; existing server days не перезаписываются draft при refresh.
- [ ] Tests `RouteEdit_PreservesHiddenStopFields`, `OldReportEndOdometer_OnlySuggestsNextMonth`, `ConcurrentClientChange_RequiresReview`, `LostRouteResponse_ReconcilesByDayAndType`.
- [ ] Небольшая форма заправок: дата/type/liters/cost, несколько записей и fuel types; no debts/payments/mileage-sync feature.
- [ ] `FuelCreateTimeout_DoesNotRetryBlindly`, `DateCommaAndNumericWire_MatchSource`, `NoFuelMonth_DoesNotInventTransaction`; редактирование existing import-sourced fuel учитывает реальную backend mutation semantics.
- [ ] Воспроизводимая локальная draft persistence, cancelled save и server read-back; при ошибке не заявлять месяц готовым к report.

**Готово:** все необходимые входы нового месяца можно подготовить через один экран и существующие API.

### Этап 4. Формирование и отправка серверного отчёта — первый полезный Lite milestone

**Файлы:** `Core/Gsm/GsmReportApi.cs`, month submission state в `Features/Home/`; `GsmReportContractTests.cs`, `MonthlyReportStateTests.cs`.

**Интерфейсы:** `GenerateAndSendAsync(string month, CancellationToken)` сохраняет прежний `{ month }` и result DTO; jobs client читает фактический `/gsm/jobs`, без придуманного polling ID/cancel endpoint.

- [ ] Предварительная проверка profile/month/31-row limit/dirty or failed saves, explicit month/email confirmation.
- [ ] Tests `ReportBody_ContainsOnlyExistingFields`, `PendingDraft_BlocksAccidentalGeneration`, `FailedInputSave_DoesNotGenerate`, `GeneratedAndSent_AreDifferentStates`, `ReportTimeout_DoesNotResendAutomatically`.
- [ ] UI отображает source result/status и delivery distinction, unknown outcome сверяет jobs; неоднозначность остаётся видимой.
- [ ] Нет автоматически добавленной download button/force resend/skip-email flow без отдельного подтверждённого контракта/UX решения.
- [ ] Один разрешённый end-to-end check: confirmed profile + days/refuels → server generation → получение корректного month XLSX письмом. Без этого результат ограничен fixtures/build.

**Готово:** инструмент месячного отчёта работает с ручными адресами; SimpleOne ещё не обязателен.

### Этап 5. Минимальные закрытые заявки: ручной XLSX-импорт

**Файлы:** `Core/ClosedRequests/ClosedRequestsImport.cs`, archive models/storage, `Features/ClosedRequests/`; `ClosedRequestsImportTests.cs`, `ClosedRequestEligibilityTests.cs`.

**Интерфейс:** importer возвращает preview accepted/rejected rows и issues; repository commit делает deterministic upsert по source keys, без network writes/mirror delete.

- [ ] Source mapping номера/адреса/status/type/dates, минимальный hidden metadata и Lite terminal-status restriction.
- [ ] Tests `OnlyTerminalStatuses_AreVisible`, `OpenReturnEquipment_IsExcluded`, `ClosedReturnEquipment_UsesRegisteredDate`, `DuplicateNumber_LastRowWins`, `SearchAcrossArchive_IgnoresPeriod`, `LocalizedXlsxDate_IsNotShiftedTwice`.
- [ ] Две видимые колонки, search/period/partial coverage; manual import preview+confirm, corrupt file сохраняет предыдущий snapshot.
- [ ] Выбор/копирование номера и адреса в текущий route draft; request close date не становится датой поездки без user choice.

**Готово:** закрытые заявки доступны без SO login как помощь заполнению месячного маршрута.

### Этап 6. SimpleOne connection и автоматическое обновление

**Файлы:** SO auth/client внутри `Core/Auth/` и `Core/ClosedRequests/`, `ClosedRequestsSync.cs`, connection form/coordinator; `ClosedSyncTests.cs`, `ArchiveScopeTests.cs`.

- [ ] Separate SO credentials/identity, no app Bearer on SO host; opt-in connection, LumaWork session независима.
- [ ] Manual XLSX без watermark → existing reconciliation/bootstrap policy → valid delta; не терять partial coverage/recovery checkpoints.
- [ ] Tests `HistoricalImport_HasNoTrustedWatermark`, `ManualImportInvalidatesLateDelta`, `KnownRecordNewVersion_IsUpdated`, `StatusExclusion_IsNotParseFailure`, `AccountSwitch_CancelsAndSeparatesArchive`, `Wake_HasOneRefreshOwner`.
- [ ] Сохранить current scopes/encoding/sorting/watermarks/cadence; manual import остаётся и сериализуется с sync.
- [ ] Один разрешённый read-only SO check после fixtures: текущая identity/закрытые requests/адреса и update path; никакого изменения upstream заявок.

**Готово:** ручной bootstrap и автоматическое обновление работают вместе, список остаётся минимальным.

### Этап 7. Windows runtime, отдельный пакет и acceptance

**Файлы:** build/package scripts по потребности, Lite manifest/assets, реальные acceptance results в журнале.

- [ ] Проверить Windows 11 x64: login, profile XLSX picker/preview, сохранение реквизитов, month/day/refuel editors, закрытые requests import/refresh и серверный report flow.
- [ ] Keyboard/Narrator/DPI/themes, archive virtualization, no-space/corrupt snapshot, lock/sleep/network loss/logout и dirty close.
- [ ] Проверить отдельную Lite package identity/cache namespace и установку рядом с full package, когда он реально появится.
- [ ] Build dependency audit: нет excluded domain UI, локального GSM generator/Python/Excel requirement, лишнего WebView runtime и production secrets.
- [ ] Подписанный MSIX clean install/upgrade; certificate trust, dependencies и artifact checksum проверяются отдельно. Публичная публикация только по поручению.
- [ ] Final status/diff показать пользователю; commit/push только по подтверждению. VPS/backend не обновлять для этой реализации.

**Готово:** подтверждённый Lite workflow и installable candidate; неподтверждённые пункты явно остаются gaps.

## 11. Проверки и начало реализации

Планируемая локальная команда после появления SDK/projects:

```bash
dotnet test tests/Engineer.Core.Tests/Engineer.Core.Tests.csproj -c Release
```

На Windows CI: тот же test project + реальная Debug x64 сборка `src/Engineer.Lite.Windows/Engineer.Lite.Windows.csproj` через проверенный toolchain. Workflow/build script относятся только к Lite; полный UI не должен быть prerequisite.

Core tests не доказывают WinUI/file picker/Windows credential persistence. Windows Server Actions build не заменяет Windows 11 runtime. Live profile/route/fuel/report writes и отправка email проверяются только в отдельно согласованном объёме.

**Следующий этап:** завершить Windows CI/deployment часть этапа 0, затем auth/profile и XLSX importer. Не начинать реализацию с excluded modules или полного приложения.

## 12. Журнал и передача

### 14.10.2026 — отдельный Lite-план

- Пользователь подтвердил два решения: input — готовый прошлый ГСМ XLSX; новый report — через существующий сервер LumaWork.
- Scope сокращён до «Главной» и минимальных закрытых «Заявок». Необходимые реквизиты/маршруты/refuels представлены как формы/входы отчёта, без возвращения исключённых экранов.
- Проверены локальные profile/report/jobs/routes/fuel declarations, generator cells/compaction/limits и closed import eligibility/sync references.
- Обнаруженные ограничения внесены в план: неполная обратимость XLSX→profile, server IDs, linked vehicle effects, ignored existing default odometer update, отсутствие доказанного binary download contract, отсутствие server `sent=true` фильтра маршрутов и невозможность считать manual XLSX trusted delta baseline.
- Создан только этот Markdown; полный план не изменён. Solution/app/workflow/install остаются будущими этапами; SDK/Git/GitHub setup из полного плана ещё не выполнялся.

### 14.10.2026 — этап 0, локальная основа Lite

- Пользователь подтвердил начало работы сообщением «делай». Выполняется первый предложенный этап: минимальная основа Lite и подготовка Windows CI; public release/VPS/server mutations не поручены.
- Установлен официальный .NET SDK 10.0.402 arm64 в пользовательский `~/.dotnet`. Shell settings не менялись; `scripts/test-core.sh` находит SDK без изменения PATH. Git init выполнен в новом самостоятельном корне, branch `feat/windows-lite-bootstrap`; соседние repos не изменялись.
- **Ruling:** новый корень до задачи содержал только два плана и не имел Git/commits. Работа идёт в этом самостоятельном checkout/feature branch; отдельный worktree без initial commit не создаётся. Commit/push не выполняются без подтверждения, stage-0 ledger ведётся здесь и в ignored `.codex-tmp/progress.md`.
- Созданы `EngineerLite.sln`, переносимый `Engineer.Core`, `Engineer.Lite.Windows` с двумя реальными navigation destinations и честными unavailable content states; настройки/version отображаются из настоящих источников. Доменные data/actions/login пока отсутствуют. Никаких сетевых запросов при запуске.
- **Ruling:** live NuGet/официальные docs подтвердили stable Windows App SDK 2.5.1; новые CLI templates пока preview, поэтому packaged project основан на официальном stable sample, без preview template dependency. Windows SDK BuildTools 10.0.28000.2705, target API 26100/minimum 22000; заявляемый runtime baseline Windows 11 25H2 не проверен.
- Source fingerprints сохранены в synthetic fixture manifest, runtime/build не читают reference checkout. Windows PNG 44/150/50 получены из существующей Mac app icon; исходный asset сохранён. Identity/publisher — development-only `LumaWork.Engineer.Lite` / `CN=LumaWork.Development`, сертификат не создавался.
- **TDD:** сначала реальный core test build RED (API/namespace отсутствовали), затем production scope/config APIs и GREEN 24/24. Проверены восстановление запрещённого раздела, порядок разрешения primary/alias, пустые значения и placeholders, неверные origins и запрет небезопасного file/fallback request.
- Local Windows-project NuGet restore и повторный locked restore PASS. Locks всех трёх проектов сохранены; это dependency resolution, не XAML build.
- Workflow `.github/workflows/windows-lite-ci.yml`: documented `windows-2025-vs2026`, .NET по global.json, VS 18.x MSBuild, pinned action SHAs, read-only permissions, timeouts/concurrency, tests/build output/binlog/TRX, без signing/release. Repo/remote пока отсутствует, workflow не запускался.
- Actionlint 1.7.12 PASS; XML/JSON parse и shell syntax PASS. Official PowerShell 7.6.7 использован только в ignored tooling для syntax check и реального Mac host refusal: build script не запускает Windows tools на macOS.
- Fresh read-only review нашёл один Important: отсутствующий `Properties/launchSettings.json` блокировал Visual Studio MSIX path. Reproduced RED через реальный SDK `VerifyLaunchSettings`; добавлен `MsixPackage` profile, тот же target GREEN exit 0. Других подтверждённых stage-0 defects reviewer не нашёл; XAML/Windows UI/deployment не оценивались как пройденные.
- Финальная local verification: `scripts/test-core.sh` с locked restore — 24/24 PASS, TRX в ignored scratch; повторные Actionlint/PowerShell syntax, XML/JSON/PNG sizes и MSIX launch validation PASS. Full plan SHA-256 и 8 source fingerprints не изменились. Изменения подготовлены в Git index для review, commit/push отсутствуют.
- **Не доказано:** native XAML compilation, Windows startup/UI, install/deployment, account/API runtime и GitHub Actions job. Для окончания этапа 0 нужны доступный GitHub repo/подтверждённый push и Windows test host.

### 14.10.2026 — разрешён публичный GitHub push

- Пользователь указал бесплатный GitHub/public repo и прямо поручил проверить секреты и выполнить push. Эта preference сохранена отдельной memory note; исправлено прежнее предложение private repo.
- Перед commit/push проверяются staged snapshot, личные данные/asset metadata и secret scanner. Для commit author используется GitHub noreply identity; реальные credentials не переносятся в configuration и CI.
- `.gitignore` дополнен private keys, `.env`, local configs, credentials/tokens/secrets JSON; ignored tooling/logs/cache не входят в публикацию.
