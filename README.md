# Sidearm

**English** · [Українська](#українська)

**A pocket browser for Windows gamers.** Press Alt + ` and a small browser window appears over your game: check a wiki, a map, an exchange rate or a video. Press Alt + ` again and you're back in the game.

> *Switching to your sidearm is faster than opening a browser.*

## Download

The latest version is on the **[Releases](../../releases/latest)** page:

- **`Sidearm-<version>-share-uk.zip`**: for everyone (starts in Ukrainian; switch to English in Settings → General).
- **`Sidearm-<version>.zip`**: the same plus the source code in a `source` folder.

You need Windows 10 or 11 with the Microsoft Edge WebView2 Runtime (Windows 11 and an up-to-date Windows 10 already have it).

**Install:** unzip the archive and run `Sidearm.exe`. It lives in the tray (the crosshair icon). Windows may show a SmartScreen warning because the program isn't signed: "More info" → "Run anyway".

**Update:** from 0.8 on, Sidearm checks this page once a day and offers an Update button when a new version is out. It downloads the shared zip, checks it against GitHub's checksum, swaps the files in and restarts; your `data` folder is never touched. By hand: quit Sidearm (right-click the tray icon → "Quit Sidearm"), then unzip the new version over the old folder, or into a new folder. In a new folder, Sidearm finds the earlier copy and offers to bring over your sign-ins, tabs and settings.

> ⚠️ The `data` folder next to `Sidearm.exe` holds your sign-ins. Never send it to anyone.

## What it does

| | |
| --- | --- |
| **Alt + `** | Show / hide over the game (works with borderless games) |
| **Alt + Space** | Quick bar at the mouse: search, math, `100 usd`, `5 ft in cm`, `timer 5m` |
| **Alt + G** | Ghost mode: the window is see-through and clicks pass into the game |
| **Alt + P** | Pop a video out into its own small window over the game |
| **Alt + T** | Translate the page |
| **Hold Alt over a word** | Meaning, Wikipedia and translation |
| **Prefixes** | `w` Wikipedia, `y` YouTube, `t` translator, `a` your AI, `c` Claude… |

Also: notes and pinned sites on the start page (`n` + text in the bar adds a note), a fix for text typed in the wrong keyboard layout, ad blocking (YouTube ads skipped too), reader view, "clear fog" (overlays and paywall pop-ups), mouse gestures, sound off by default, clean links without trackers, your own color and grain, Ukrainian and English.

The full list of commands is on Sidearm's start page, in the "Field manual" section.

## Build from source

Sidearm builds with the Mono compiler for .NET Framework 4.8 (x64):

```bash
./build.sh          # needs mcs (Mono), llvm-rc and python3 with pefile
```

The result is in `build/Sidearm/`. Layout:

| Path | What's there |
| --- | --- |
| `src/` | The C# WinForms shell around WebView2 (`MainForm.*.cs` split by topic) |
| `ui/` | Everything you see: HTML, CSS and JavaScript (the strip, start page, settings, quick bar) |
| `ui/lang/uk.js` | The Ukrainian interface dictionary |
| `assets/` | Icon, manifest, README for the archive |
| `lib/` | WebView2 SDK 1.0.3856.49 (Microsoft) |
| `tests/` | Tests that run on Linux: `tests/run.sh` |
| `docs/DEVELOPMENT.md` | Developer guide: architecture, message protocol, gotchas, changelog |

## Third-party components

- [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/): the page engine
- [Mozilla Readability](https://github.com/mozilla/readability) (Apache 2.0): reader view
- NV Bitter, NV Jost and Sourcerer fonts from Nico Verbruggen's eBook Fonts (OFL): `ui/fonts/LICENSES.txt`
- EasyList, EasyPrivacy and EasyList Cookie filter lists (GPLv3 / CC BY-SA 3.0): downloaded on first start
- [uBlock Origin Lite](https://github.com/uBlockOrigin/uBOL-home) (GPLv3): only if you pick it in Settings; downloaded from its official GitHub
- Exchange rates: the National Bank of Ukraine

---

## Українська

[English](#sidearm) · **Українська**

**Кишеньковий браузер для геймерів під Windows.** Натисніть Alt + `, і над грою з'явиться маленьке вікно браузера: глянути вікі, мапу, курс валют чи відео. Ще раз Alt + `, і ви знову в грі.

> *Switching to your sidearm is faster than opening a browser.*

### Завантажити

Остання версія лежить на сторінці **[Releases](../../releases/latest)**:

- **`Sidearm-<версія>-share-uk.zip`**: для всіх (стартує українською; англійську можна ввімкнути в Налаштування → Загальні).
- **`Sidearm-<версія>.zip`**: те саме плюс вихідний код у папці `source`.

Потрібна Windows 10 або 11 з Microsoft Edge WebView2 Runtime (на Windows 11 і оновленій Windows 10 він уже є).

**Встановлення:** розпакуйте архів і запустіть `Sidearm.exe`. Він живе в треї (іконка-приціл). Windows може показати попередження SmartScreen, бо програма не підписана: «Додаткові відомості» → «Усе одно запустити».

**Оновлення:** починаючи з 0.8, Sidearm раз на день перевіряє цю сторінку й пропонує кнопку «Оновити», коли виходить нова версія. Він завантажує спільний zip, звіряє його з контрольною сумою GitHub, замінює файли й перезапускається; папка `data` лишається недоторканою. Вручну: закрийте Sidearm (правий клік по іконці в треї → «Вийти з Sidearm»), потім розпакуйте нову версію поверх старої папки або в нову папку. У новій папці Sidearm знайде попередню копію й запропонує перенести входи в акаунти, вкладки й налаштування.

> ⚠️ Папка `data` поруч із `Sidearm.exe` містить ваші входи в акаунти. Нікому її не надсилайте.

### Що вміє

| | |
| --- | --- |
| **Alt + `** | Показати / сховати поверх гри (працює з безрамковими іграми) |
| **Alt + Space** | Швидкий рядок біля миші: пошук, обчислення, `100 usd`, `5 ft in cm`, `timer 5m` |
| **Alt + G** | Режим привида: вікно напівпрозоре, кліки проходять у гру |
| **Alt + P** | Винести відео в окреме маленьке вікно поверх гри |
| **Alt + T** | Перекласти сторінку |
| **Затиснути Alt над словом** | Значення, Вікіпедія й переклад |
| **Префікси** | `w` Вікіпедія, `y` YouTube, `t` перекладач, `a` ваш ШІ, `c` Claude… |

А ще: нотатки й закріплені сайти на стартовій сторінці (`n` + текст у рядку додає нотатку), виправлення тексту, набраного не в тій розкладці, блокування реклами (і пропуск реклами на YouTube), режим читання, «прибрати туман» (оверлеї й вікна з підписками), жести мишею, вимкнений за замовчуванням звук, чисті посилання без трекерів, власний колір і зернистість, українська й англійська.

Повний список команд є на стартовій сторінці Sidearm, у розділі «Польовий довідник».

### Зібрати з коду

Sidearm збирається компілятором Mono під .NET Framework 4.8 (x64):

```bash
./build.sh          # потрібні mcs (Mono), llvm-rc і python3 з pefile
```

Результат з'явиться в `build/Sidearm/`. Структура:

| Шлях | Що там |
| --- | --- |
| `src/` | Оболонка на C# WinForms навколо WebView2 (`MainForm.*.cs` поділено за темами) |
| `ui/` | Усе, що видно: HTML, CSS і JavaScript (смужка, стартова сторінка, налаштування, швидкий рядок) |
| `ui/lang/uk.js` | Український словник інтерфейсу |
| `assets/` | Іконка, маніфест, README для архіву |
| `lib/` | WebView2 SDK 1.0.3856.49 (Microsoft) |
| `tests/` | Тести, що запускаються на Linux: `tests/run.sh` |
| `docs/DEVELOPMENT.md` | Посібник розробника (англійською): архітектура, протокол повідомлень, підводні камені, історія змін |

### Сторонні компоненти

- [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/): рушій сторінок
- [Mozilla Readability](https://github.com/mozilla/readability) (Apache 2.0): режим читання
- Шрифти NV Bitter, NV Jost і Sourcerer з eBook Fonts Ніко Вербрюгена (OFL): `ui/fonts/LICENSES.txt`
- Списки фільтрів EasyList, EasyPrivacy і EasyList Cookie (GPLv3 / CC BY-SA 3.0): завантажуються при першому запуску
- [uBlock Origin Lite](https://github.com/uBlockOrigin/uBOL-home) (GPLv3): лише якщо обрати його в налаштуваннях; завантажується з офіційного GitHub
- Курси валют: Національний банк України
