**EN — Added: several source folders at once, and a size limit that applies to a folder rather
than to each file.**

A panel on the left holds the list of source folders: add as many as needed, each with its own
checkbox for whether to process it and a summary of how many PDFs it holds and how much they
weigh. The file list shows paths relative to each source folder.

Next to the maximum size there are now two mutually exclusive checkboxes (only one can be on):
**limit on the whole folder** — the sum of everything in it, nested folders included, must fit;
and **limit on each subfolder** — every first-level subfolder fits on its own. With neither one
on, the limit applies per file as before.

For a folder a single compression level is chosen for the whole group, not per file. That is
fairer on quality — every document in the batch looks the same instead of one being crisp and
its neighbour mushy — and more likely to succeed, because slack in one file works for another.
Search passes measure sizes only and discard the compressed bytes, so a folder of any size fits
in memory.

**Fixed:** results used to be written out flat, by file name alone, so files with the same name
in different subfolders overwrote each other; the subfolder structure is now preserved. And a
second run with "search subfolders" no longer re-compresses its own output.

**Security:** `PdfSharp.Cryptography.dll` and `System.Security.Cryptography.Pkcs.dll` no longer
ship. The latter is a package with two open advisories (one high severity) that PdfSharp pulls in
transitively for a single class that puts a digital signature on a PDF. This app only reads and
rebuilds documents — it never signs or verifies — so the vulnerable CMS/PKCS#7 parsing was never
reachable, and that code is now absent from the build entirely.

---

**RU — Добавлено: несколько папок-источников сразу и предел размера на папку, а не на файл.**

Слева появилась панель со списком папок: добавляйте сколько нужно, у каждой свой флажок
«обрабатывать» и сводка — сколько в ней PDF и сколько они весят. В списке файлов видны пути
относительно своей папки-источника.

Рядом с максимальным размером — два взаимоисключающих флажка (активен может быть только один):
**предел на всю папку целиком** — в него должна уложиться сумма всего её содержимого, включая
вложенные папки; и **предел на каждую подпапку** — каждая подпапка первого уровня укладывается
сама по себе. Если не включён ни один, предел применяется к каждому файлу, как раньше.

Для папки подбирается одна степень сжатия на всю группу, а не своя на каждый файл. Так честнее
по качеству (все документы пачки выглядят одинаково, а не «этот чёткий, а соседний мыльный») и
успешнее по результату: запас одного файла работает на другой. Проходы поиска считают только
размеры и тут же выбрасывают сжатые байты — папка любого размера помещается в память.

**Исправлено:** результаты раскладывались плоско, по одному имени файла, и одноимённые файлы из
разных подпапок затирали друг друга — теперь структура подпапок сохраняется. Повторный запуск
с поиском во вложенных папках больше не сжимает собственный вывод по второму кругу.

**Безопасность:** из поставки убраны `PdfSharp.Cryptography.dll` и
`System.Security.Cryptography.Pkcs.dll`. Второй — пакет с двумя открытыми уязвимостями (одна
высокого уровня), который PdfSharp тянет транзитивно ради единственного класса, ставящего на PDF
цифровую подпись. Программа документы только читает и пересобирает, подписи не ставит и не
проверяет, поэтому уязвимый разбор CMS/PKCS#7 не вызывался ни из одной ветки — а теперь этого
кода в сборке нет вовсе.

> Сборка не подписана — Windows SmartScreen покажет «Неизвестный издатель».
