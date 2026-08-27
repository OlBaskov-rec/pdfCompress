**EN — First working version.** Batch PDF compression for Windows.

Pick a folder and the window lists only its PDF files with their sizes (optionally including
subfolders); any file can be excluded with a checkbox. Two mutually exclusive ways to set
compression — a 5-step level slider, or a maximum file size with a unit (bytes / KB / MB / GB,
MB by default). Exactly one is active; the other is disabled and dimmed.

The engine downsamples raster images to the requested dpi based on their **actual** size on the
page — content streams are parsed (`q`/`Q`/`cm`/`Do`, nested Form XObjects included) — re-encodes
them as JPEG (grayscale originals as grayscale JPEG), and replaces a stream only when the result
is at least 10 % smaller. Line-art scans (CCITT, JBIG2), indexed palettes, `/Decode` tables and
colour-key masking are deliberately left alone; soft masks are downsampled losslessly.
Target-size mode binary-searches the same scale for the gentlest setting that still fits.

Results go to a separate folder (`compressed` by default) — originals are never modified — with a
per-file report and a batch total. Processing can be cancelled. No external tools required:
no Ghostscript, no qpdf, no installed .NET.

---

**RU — Первая рабочая версия.** Пакетное сжатие PDF под Windows.

Указываете папку — в окне появляется список только PDF-файлов с размерами (при желании — из
вложенных папок); любой файл можно исключить флажком. Два взаимоисключающих способа задать
сжатие: бегунок на 5 степеней либо максимальный размер файла с единицей измерения
(байты / КБ / МБ / ГБ, по умолчанию МБ). Активен ровно один, второй выключен и приглушён.

Движок уменьшает растры до заданного dpi, исходя из их **реального** размера на странице —
для этого разбираются потоки содержимого (`q`/`Q`/`cm`/`Do`, включая вложенные Form XObject) —
перекодирует их в JPEG (полутоновые в серый JPEG) и заменяет поток, только если новый минимум
на 10 % меньше. Штриховые сканы (CCITT, JBIG2), индексированные палитры, таблицы `/Decode` и
цветовое маскирование намеренно не трогаются; маски прозрачности уменьшаются без потерь.
В режиме предельного размера параметры подбираются двоичным поиском по той же шкале.

Результаты пишутся в отдельную папку (по умолчанию `compressed`) — исходные файлы не меняются, —
по каждому файлу и по всей пачке показывается отчёт. Обработку можно прервать. Внешние утилиты
не нужны: ни Ghostscript, ни qpdf, ни установленный .NET.

> Сборка не подписана — Windows SmartScreen покажет «Неизвестный издатель».
