# Changelog

Все заметные изменения проекта документируются в этом файле.
Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
проект придерживается [семантического версионирования](https://semver.org/lang/ru/).

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
