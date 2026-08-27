**EN — Fixed: files landed right on the size limit.**

Sizes were counted in binary units (1 MB = 1 048 576 B), and the binary search by its nature
brought each file flush against the boundary. On a real batch of 56 scanned contracts with a 1 MB
limit, 22 files came out above 1 000 000 bytes and Explorer showed them as "1 024 KB". Anything
that reads a megabyte as a million bytes — mail servers, government portals — would reject them.

Sizes are now **decimal** everywhere (1 KB = 1000 B, 1 MB = 1 000 000 B). That is the strictest
reading: a file that fits a decimal limit fits a binary one too. And the search aims **2 % below**
the limit instead of at it.

Same batch, same 1 MB limit: **0 overruns, largest result 979 442 B — 97.9 % of the limit.**

If the 2 % margin turns out to be unreachable but the limit itself is met, that still counts as
success. And when compression cannot help and the original is written instead, the report no longer
claims success in target-size mode — it says the limit was not met, which is what is on disk.

---

**RU — Исправлено: файлы ложились впритык к пределу.**

Размеры считались в двоичных единицах (1 МБ = 1 048 576 Б), а двоичный поиск по своей природе
подводит результат вплотную к границе. На реальной пачке из 56 сканированных договоров с пределом
1 МБ 22 файла вышли больше 1 000 000 байт, и Проводник показывал их как «1 024 КБ». Всё, что
понимает мегабайт как миллион байт — почтовые серверы, госпорталы, — такие документы не примет.

Размеры теперь **десятичные** везде (1 КБ = 1000 Б, 1 МБ = 1 000 000 Б). Это самое строгое
прочтение: файл, уложившийся в десятичный предел, уложится и в двоичный. И подбор целится
на **2 % ниже** предела, а не в него.

Та же пачка, тот же предел 1 МБ: **превышений 0, максимальный результат 979 442 Б — 97,9 % от
предела.**

Если запас в 2 % взять не удалось, но сам предел соблюдён, это по-прежнему успех. А когда сжатие
не помогает и записывается оригинал, отчёт в режиме предельного размера больше не выдаёт это за
успех — он честно говорит, что предел не соблюдён.
