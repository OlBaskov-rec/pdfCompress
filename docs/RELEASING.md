# Выпуск новой версии PDF Compress

Памятка по сборке и публикации релиза. Авто-обновление работает через **Velopack**,
источник обновлений — раздел **GitHub Releases** репозитория
[OlBaskov-rec/pdfCompress](https://github.com/OlBaskov-rec/pdfCompress), а не дерево файлов.

---

## Быстрый чек-лист

- [ ] Поднять `<Version>` в `src/PdfCompress.Desktop/PdfCompress.Desktop.csproj`
- [ ] Создать `build/release-notes-X.Y.Z.md` (необязательно, но попадёт в описание релиза)
- [ ] Добавить раздел `## [X.Y.Z] — ГГГГ-ММ-ДД` в `CHANGELOG.md`
- [ ] `dotnet build PdfCompress.sln -c Release` и `dotnet test`
- [ ] `git commit` + `git push`
- [ ] Сборка пакета: `build/pack-win.ps1 -Version X.Y.Z`
- [ ] В `Releases/` появились `RELEASES`, `releases.win.json`, `*-full.nupkg`,
      `PdfCompress-win-Setup.exe`, `PdfCompress-win-Portable.zip`
- [ ] Публикация: `-Upload` с токеном либо вручную через GitHub → Releases
- [ ] Проверить обновление на установленной прошлой версии

---

## Откуда программа берёт обновления

`UpdateService` использует `GithubSource` → `https://github.com/OlBaskov-rec/pdfCompress`.
Velopack через GitHub API находит **последний релиз-тег** и качает прикреплённые к нему
**ассеты**: `RELEASES`, `releases.win.json`, `*-full.nupkg`, `*-delta.nupkg`.

> Папка `Releases/` в `.gitignore` — коммитить её не нужно. Обновление идёт только из ассетов
> GitHub-релиза; `.nupkg` в дереве репозитория ничего не даёт и лишь раздувает историю.

---

## Шаг 1. Подготовка

1. Внести правки в код.
2. Поднять версию в `src/PdfCompress.Desktop/PdfCompress.Desktop.csproj` — версия в заголовке
   окна берётся оттуда же (`AppInfo.Version`).
3. Добавить раздел в `CHANGELOG.md` (двуязычно: сначала EN, потом RU — как в pdfGrouping).
4. Сборка и тесты:

   ```powershell
   dotnet build PdfCompress.sln -c Release
   dotnet test
   ```

5. Закоммитить и запушить.

---

## Шаг 2. Сборка пакета

```powershell
powershell -ExecutionPolicy Bypass -File build\pack-win.ps1 -Version X.Y.Z
```

Скрипт делает `dotnet tool restore`, self-contained `publish` под `win-x64` и `vpk pack`.
Артефакты кладутся в `Releases/`:

| Файл | Что это |
|------|---------|
| `PdfCompress-win-Portable.zip` | Portable-сборка со встроенным `Update.exe` — распаковал и запустил |
| `PdfCompress-win-Setup.exe` | Установщик (ставит в `%LocalAppData%\PdfCompress`) |
| `PdfCompress-<версия>-full.nupkg` | Пакет обновления |
| `RELEASES`, `releases.win.json` | Метаданные фида обновлений |

Перед запуском стоит **закрыть работающий PdfCompress.exe**: иначе `publish` упадёт на
блокировке файлов в `bin`.

---

## Шаг 3. Публикация в GitHub Releases

**Проще всего — через уже сохранённую авторизацию GitHub.** Если на машине настроен Git
Credential Manager и вы хоть раз входили в GitHub через браузер, токен уже лежит в хранилище
Windows, и отдельный PAT заводить не нужно:

```bash
TOK=$(printf 'protocol=https\nhost=github.com\n\n' | git credential fill | grep '^password=' | cut -d= -f2-)
```

```bash
GITHUB_TOKEN="$TOK" powershell -ExecutionPolicy Bypass -File build/pack-win.ps1 -Version 0.1.2 -Upload
```

GCM отдаёт OAuth-токен вида `gho_…` с правами `gist, repo, workflow` — `repo` покрывает создание
релизов. Проверить права, ничего не публикуя:
`curl -sI -H "Authorization: Bearer $TOK" https://api.github.com/user | grep -i x-oauth-scopes`.

> Токен нигде не сохраняем и не печатаем: он живёт только в переменной окружения одной команды.

Либо обычным путём, с личным токеном (нужны права **Contents: Read and write** на репозиторий):

```powershell
$env:GITHUB_TOKEN = "<github_pat_...>"
powershell -ExecutionPolicy Bypass -File build\pack-win.ps1 -Version 0.1.2 -Upload
```

Вручную: GitHub → Releases → Draft new release → тег `vX.Y.Z` → приложить **все** файлы из
`Releases/` → Publish.

> Токен в репозиторий не коммитим. Создание: GitHub → Settings → Developer settings →
> Personal access tokens → Fine-grained tokens.

---

## Подпись кода

Для pdfCompress сертификат **не заведён** — Windows SmartScreen покажет «Неизвестный издатель».
Плумбинг в скрипте готов: параметр `-SignParams` пробрасывается в `vpk --signParams`, например

```powershell
powershell -ExecutionPolicy Bypass -File build\pack-win.ps1 -Version X.Y.Z `
  -SignParams "/fd SHA256 /sha1 <ОТПЕЧАТОК>"
```

Как завести самоподписанный сертификат и раздать доверие к нему — сделано в соседнем проекте
pdfGrouping (`build/trust-cert.ps1`), при необходимости переносится один в один.

---

## Грабли

- **`.ps1` с кириллицей обязан быть в UTF-8 с BOM.** Windows PowerShell 5.1 (в отличие от
  `pwsh` 7) читает файл без BOM как ANSI, и все русские строки превращаются в мусор — скрипт
  падает на разборе, а не на логике. `build/pack-win.ps1` сохранён с BOM намеренно; при
  редактировании инструментами, которые BOM срезают, его нужно вернуть.
- **Velopack не перезаписывает артефакты версии, которая уже лежит в `Releases/`.** Поправили
  код, пересобрали ту же версию — а в `Releases/` остался прежний пакет, и в релиз уедет старая
  сборка (проверяется по времени изменения файлов). `pack-win.ps1` теперь удаляет артефакты
  собираемой версии перед `vpk pack`; пакеты **прошлых** версий не трогает — по ним Velopack
  строит delta-обновления. Если пакуете руками, чистите `Releases/` сами.
- **Velopack обновляет только «вверх».** Версия нового релиза должна быть строго больше
  установленной, иначе клиенты обновление не увидят.
- **Проверка обновления работает лишь в установленной сборке.** При `dotnet run`
  `UpdateManager.IsInstalled == false`, и `UpdateService` — безопасный no-op.
