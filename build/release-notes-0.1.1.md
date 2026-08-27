**EN — Fixed: scans that "compressed" by 0 %.**

When an image sits under a **filter chain** — `/Filter [/FlateDecode /DCTDecode]`, i.e. JPEG
additionally wrapped in Flate, which is what many scanners and MFPs write — only a single filter
was understood, so the image looked like an unknown format and was silently left alone. A whole
batch of scanned documents would report 0 % savings without a single error.

The chain is now unwound: Flate, ASCII85, ASCIIHex and RunLength as transport filters, JPEG as the
terminal one. Images that use a predictor (`/DecodeParms /Predictor`) are explicitly skipped
instead of being decoded incorrectly.

On a real batch of 56 scanned contracts: **96 MB → 37.6 MB (−61 %)** at the Medium level, every
file compressed, ~36 seconds. Text stays fully legible.

**Also:** when a file does not shrink, the report now says why — "no rasters in the document" vs
"rasters (N) are already optimal or in an unsupported format". That distinction is what turns a
silent 0 % into something you can act on.

---

**RU — Исправлено: сканы, которые «сжимались» на 0 %.**

Если растр лежит под **цепочкой фильтров** — `/Filter [/FlateDecode /DCTDecode]`, то есть JPEG,
поверх упакованный ещё и Flate (так пишут многие сканеры и МФУ), — разбирался только одиночный
фильтр. Картинка выглядела «непонятным форматом» и молча оставалась нетронутой: целая пачка
сканов показывала 0 % экономии и ни одной ошибки.

Теперь цепочка разворачивается: Flate, ASCII85, ASCIIHex и RunLength как транспортные фильтры,
JPEG как терминальный. Растры с предиктором (`/DecodeParms /Predictor`) явно пропускаются, а не
декодируются неверно.

На реальной пачке из 56 сканированных договоров: **96 МБ → 37,6 МБ (−61 %)** на степени
«Среднее», сжались все файлы, ~36 секунд. Текст остаётся полностью читаемым.

**Ещё:** если файл не уменьшился, в отчёте теперь видно почему — «растров в документе нет» либо
«растры (N шт.) уже оптимальны или в неподдержанном формате». Именно этой строчки не хватало,
чтобы молчаливые 0 % стали понятны.
