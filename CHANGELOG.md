# Changelog

Все заметные изменения проекта документируются в этом файле.
Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
проект придерживается [семантического версионирования](https://semver.org/lang/ru/).

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
