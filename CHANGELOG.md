# Changelog

Все заметные изменения проекта документируются в этом файле.
Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
проект придерживается [семантического версионирования](https://semver.org/lang/ru/).

## [0.2.1] — 2026-10-23

**EN — Fixed: the update was downloaded but the banner never appeared.** The download had a
two-minute timeout, which is not enough for a ~50 MB package on a slow link. Worse, a timeout
here only stops waiting — it does not cancel the work, so the download quietly ran to completion
after the app had already given up, and pressing the button again started the same fifty
megabytes over. The wait is now ten minutes, and a repeated press joins the download already in
progress instead of starting a new one.

**Changed:** checking for updates now makes several attempts. Measurements on the work network:
`api.github.com`, which is what Velopack talks to, answers in 0.6 s, then 17 s, then not at all,
while `github.com` itself answers in about a second consistently. A single attempt simply missed
roughly a third of the time.

> Note for anyone on 0.1.0–0.1.2: those versions do not look for updates at all — the check was
> added later. Update once by hand (new Portable.zip or Setup.exe); from 0.2.0 on it is automatic.

**RU — Исправлено: обновление скачивалось, а баннер не появлялся.** На загрузку стоял таймаут
в две минуты, а пакета там полсотни мегабайт — на небыстром канале этого мало. Хуже того, таймаут
здесь только перестаёт ждать, но работу не отменяет: загрузка спокойно доходила до конца уже
после того, как приложение сдалось, а повторное нажатие начинало качать те же мегабайты заново.
Теперь ждём десять минут, а повторное нажатие подхватывает уже идущую загрузку.

**Изменено:** проверка обновлений делает несколько попыток. Замеры на рабочей сети:
`api.github.com`, куда ходит Velopack, отвечает то за 0,6 с, то за 17 с, то не отвечает вовсе,
тогда как сам `github.com` стабильно отвечает за секунду. С одной попытки проверка не доходила
примерно в трети случаев.

> Тем, кто на 0.1.0–0.1.2: эти версии обновление не ищут вообще — проверка появилась позже.
> Обновитесь один раз вручную (новый Portable.zip или Setup.exe), дальше будет автоматически.

## [0.2.0] — 2026-10-20

**EN — Added: several source folders at once, and a size limit that applies to a folder rather
than to each file.**

A panel on the left holds the list of source folders: add as many as needed, each with its own
checkbox for whether to process it and a summary of how many PDFs it holds and how much they
weigh. The file list shows paths relative to each source folder, and results keep the subfolder
structure — previously files with the same name in different subfolders overwrote each other in
one flat output folder.

Next to the maximum size there are now two mutually exclusive checkboxes (only one can be on):
**limit on the whole folder** — the sum of everything in it, nested folders included, must fit;
and **limit on each subfolder** — every first-level subfolder fits on its own. With neither one
on, the limit applies per file as before.

For a folder a single compression level is chosen for the whole group, not per file. That is
both fairer on quality — every document in the batch looks the same instead of one being crisp
and its neighbour mushy — and more likely to succeed, because slack in one file works for
another, whereas handing each file its own quota lets one stubborn scan fail the entire folder.
Search passes measure sizes only and discard the compressed bytes, so a folder of any size fits
in memory; a pass whose running total already exceeds the limit is abandoned on the spot.

By default results go into a subfolder of each source folder; files produced by a previous run
are excluded from scanning, so a second run with "search subfolders" no longer re-compresses its
own output.

---

**RU — Добавлено: несколько папок-источников сразу и предел размера на папку, а не на файл.**

Слева появилась панель со списком папок: добавляйте сколько нужно, у каждой свой флажок
«обрабатывать» и сводка — сколько в ней PDF и сколько они весят. В списке файлов видны пути
относительно своей папки-источника, а результаты раскладываются с сохранением структуры
подпапок — раньше одноимённые файлы из разных подпапок затирали друг друга в одной общей
выходной папке.

Рядом с максимальным размером — два взаимоисключающих флажка (активен может быть только один):
**предел на всю папку целиком** — в него должна уложиться сумма всего её содержимого, включая
вложенные папки; и **предел на каждую подпапку** — каждая подпапка первого уровня укладывается
сама по себе. Если не включён ни один, предел применяется к каждому файлу, как раньше.

Для папки подбирается одна степень сжатия на всю группу, а не своя на каждый файл. Так честнее
по качеству (все документы пачки выглядят одинаково, а не «этот чёткий, а соседний мыльный») и
успешнее по результату: запас одного файла работает на другой, тогда как при раздаче каждому
своей квоты один упрямый скан проваливает всю папку. Проходы поиска считают только размеры и
тут же выбрасывают сжатые байты — папка любого размера помещается в память; проход, у которого
накопленная сумма уже превысила предел, бросается сразу.

По умолчанию результаты складываются в подпапку каждой папки-источника, а файлы прошлого запуска
исключаются из поиска — повторный запуск с поиском во вложенных папках больше не сжимает
собственный результат по второму кругу.

**Безопасность:** из поставки убраны `PdfSharp.Cryptography.dll` и
`System.Security.Cryptography.Pkcs.dll`. Второй — пакет с двумя открытыми уязвимостями
(одна высокого уровня), который PdfSharp тянет транзитивно ради единственного класса,
ставящего на PDF цифровую подпись. Мы документы только читаем и пересобираем, подписи не
ставим и не проверяем, поэтому уязвимый разбор CMS/PKCS#7 не вызывался ни из одной ветки —
а теперь этого кода в сборке нет вовсе. Исправленной версии пакета не существует:
предупреждение висит на всех опубликованных, включая 9.0 и 10.0.

## [0.1.3] — 2026-08-28

**EN — Added:** an "About" button in the header with the version, author, repository link and
update checking (background check at start-up, manual check, download, "update and restart").
Sections are now separated by a thin rule with a faint arrow showing the direction of work.
**Fixed:** the default output subfolder stopped being suggested after the very first run, because
"the user picked this folder" was inferred from "settings contain an output folder"; it is now a
separate flag. Squeezing the window vertically made sections overlap — the lists had a minimum
height, so the rows no longer fit and Grid stacked them on top of each other.

**RU — Добавлено:** кнопка «О программе» в шапке — версия, автор, ссылка на репозиторий и там же
проверка обновлений (фоновая при старте, ручная, скачивание, «Обновить и перезапустить»).
Разделы отделены тонкой линией с бледной стрелкой, показывающей направление работы.
**Исправлено:** папка результатов по умолчанию переставала предлагаться после первого же запуска,
потому что признак «папку выбрал пользователь» выводился из «в настройках есть папка вывода»;
теперь это отдельный флаг. При сжатии окна по вертикали разделы наезжали друг на друга — у
списков был минимальный размер, строки переставали помещаться, и Grid накладывал их одна на
другую.

## [0.1.2] — 2026-08-27

**EN — Fixed: files landed right on the size limit.** Sizes were counted in binary units
(1 MB = 1 048 576 B), and the binary search by its nature brought each file flush against the
boundary — on a real batch 22 of 56 documents came out above 1 000 000 bytes, and Explorer showed
them as "1 024 KB". Anything that reads a megabyte as a million bytes — mail servers, government
portals — would reject them.

Two changes. Sizes are now **decimal** everywhere (1 KB = 1000 B, 1 MB = 1 000 000 B): that is the
strictest reading, so a file that fits a decimal limit fits a binary one too. And the search now
aims **2 % below** the limit instead of at it. On the same batch: 0 overruns, the largest result
979 442 B — 97.9 % of the limit. If the margin turns out to be unreachable but the limit itself is
met, that still counts as success. **Also:** when compression cannot help and the original is
written instead, the report no longer claims success in target-size mode — it says the limit was
not met, which is what is actually on disk.

**RU — Исправлено: файлы ложились впритык к пределу.** Размеры считались в двоичных единицах
(1 МБ = 1 048 576 Б), а двоичный поиск по своей природе подводит результат вплотную к границе —
на реальной пачке 22 файла из 56 вышли больше 1 000 000 байт, и Проводник показывал их как
«1 024 КБ». Всё, что понимает мегабайт как миллион байт — почтовые серверы, госпорталы, — такие
документы не примет.

Два изменения. Размеры теперь **десятичные** везде (1 КБ = 1000 Б, 1 МБ = 1 000 000 Б): это самое
строгое прочтение, файл, уложившийся в десятичный предел, уложится и в двоичный. И подбор целится
на **2 % ниже** предела, а не в него. На той же пачке: превышений 0, максимальный результат
979 442 Б — 97,9 % от предела. Если запас взять не удалось, но сам предел соблюдён, это по-прежнему
успех. **Ещё:** когда сжатие не помогает и записывается оригинал, в режиме предельного размера
отчёт больше не выдаёт это за успех — он честно говорит, что предел не соблюдён.

## [0.1.1] — 2026-08-27

**EN — Fixed:** scans whose images carry a **filter chain** — `/Filter [/FlateDecode /DCTDecode]`,
that is JPEG additionally wrapped in Flate, which is what many scanners and MFPs write — were
silently left untouched: only a single filter was understood, so such a batch "compressed" by 0 %.
The chain is now unwound (Flate, ASCII85, ASCIIHex, RunLength as transport filters, JPEG as the
terminal one) and the image is recompressed normally. On a real batch of 56 scanned contracts this
turned 0 % into roughly a threefold reduction. Images using a predictor (`/DecodeParms
/Predictor`) are now explicitly skipped rather than decoded incorrectly.

**RU — Исправлено:** сканы, у которых растр лежит под **цепочкой фильтров** —
`/Filter [/FlateDecode /DCTDecode]`, то есть JPEG, поверх упакованный ещё и Flate (так пишут
многие сканеры и МФУ), — молча оставались нетронутыми: разбирался только одиночный фильтр, и
такая пачка «сжималась» на 0 %. Теперь цепочка разворачивается (Flate, ASCII85, ASCIIHex,
RunLength как транспортные фильтры, JPEG как терминальный), и растр пересжимается как обычно.
На реальной пачке из 56 сканированных договоров это превратило 0 % в сжатие примерно втрое.
Растры с предиктором (`/DecodeParms /Predictor`) теперь явно пропускаются, а не декодируются
неверно.

## [0.1.0] — 2026-08-27

**EN — Added:** first working version. Pick a folder and the window lists only its PDF files with
their sizes (optionally including subfolders); each file can be excluded with a checkbox. Two
mutually exclusive ways to set compression — a 5-step level slider, or a maximum file size with a
unit (bytes / KB / MB / GB, MB by default); exactly one is active, the other is disabled and dimmed.
The compression engine downsamples raster images to the requested dpi based on their **actual**
size on the page (content streams are parsed for `q`/`Q`/`cm`/`Do`, nested Form XObjects included),
re-encodes them as JPEG — grayscale originals as grayscale JPEG — and only replaces a stream when
the result is at least 10 % smaller. Line-art scans (CCITT, JBIG2), indexed palettes, `/Decode`
tables and colour-key masking are deliberately left alone; soft masks (`/SMask`) are downsampled
losslessly. Target-size mode binary-searches the same scale for the gentlest setting that still
fits, up to 5 passes per file. Results are written to a separate folder (`compressed` by default),
originals are never modified, and a per-file report plus a batch total is shown. Processing can be
cancelled. Everything runs without external tools — no Ghostscript, no qpdf.

**RU — Добавлено:** первая рабочая версия. Указываете папку — в окне появляется список только
PDF-файлов с размерами (при желании — из вложенных папок); любой файл можно исключить флажком.
Два взаимоисключающих способа задать сжатие — бегунок на 5 степеней либо максимальный размер файла
с единицей измерения (байты / КБ / МБ / ГБ, по умолчанию МБ); активен ровно один, второй выключен
и приглушён. Движок уменьшает растры до заданного dpi, исходя из их **реального** размера на
странице (разбираются потоки содержимого — `q`/`Q`/`cm`/`Do`, включая вложенные Form XObject),
перекодирует их в JPEG — полутоновые в серый JPEG — и заменяет поток, только если новый минимум
на 10 % меньше. Штриховые сканы (CCITT, JBIG2), индексированные палитры, таблицы `/Decode` и
цветовое маскирование намеренно не трогаются; маски прозрачности (`/SMask`) уменьшаются без
потерь. В режиме предельного размера параметры подбираются двоичным поиском по той же шкале —
самый щадящий вариант, который ещё влезает, до 5 проходов на файл. Результаты пишутся в отдельную
папку (по умолчанию `compressed`), исходные файлы не меняются, по каждому файлу и по всей пачке
показывается отчёт. Обработку можно прервать. Всё работает без внешних утилит — ни Ghostscript,
ни qpdf.

[0.1.0]: https://github.com/OlBaskov-rec/pdfCompress/releases/tag/v0.1.0
