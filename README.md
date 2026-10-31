# Minimal Fences

<p align="center">
  <strong>Порядок на рабочем столе Windows · A tidier Windows desktop</strong><br>
</p>
<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%20x64-blue?logo=windows" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-10.0-purple?logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/UI-WPF-informational" alt="WPF" />
  <img src="https://img.shields.io/badge/License-MIT-green" alt="License" />
    <a href="https://www.donationalerts.com/r/sammpi"><img src="https://img.shields.io/badge/Support%20with%20donationalerts-gold" alt="Support with donationalerts" /></a>
  <a href="https://pay.cloudtips.ru/p/0ca64d36"><img src="https://img.shields.io/badge/Поддержать%20через%20cloudtips-gold" alt="Поддержать через cloudtips"></a>

</p>


---

[Русский](#-русский) • [English](#-english)

---

## 🇷🇺 Русский

**Minimal Fences** — нативное Windows-приложение для организации ярлыков, файлов и папок в настраиваемые группы на рабочем столе.

Без рекламы, телеметрии и учётных записей. Все настройки хранятся локально, права администратора не требуются.

> Приложение хранит только ссылки на объекты. Добавление или удаление элементов и групп не затрагивает исходные файлы на диске.

### Возможности

- **Группы:** Создание, перемещение, изменение размеров и переименование.
- **Поддержка файлов:** Drag-and-drop ярлыков `.lnk`, программ `.exe`, папок, файлов и ссылок `.url`.
- **Внешний вид:** Цвет и прозрачность фона, скругление углов, рамки, 3 размера значков.
- **Скрытие групп:** Двойной клик по пустому рабочему столу, через системный трей или настраиваемый хоткей (по умолчанию выключен).

### Установка и запуск

1. Перейдите в **[Releases](../../releases)** и скачайте `MinimalFences-win-x64.zip`.
2. Распакуйте архив в удобную папку (.NET уже встроен в сборку).
3. Запустите `MinimalFences.exe`.

### Сборка из исходников

Требуется **.NET 10 SDK**.

```bash
dotnet restore MinimalFences.slnx --locked-mode
dotnet build MinimalFences.slnx -c Release --no-restore
dotnet test tests/MinimalFences.Core.Tests -c Release --no-build
dotnet publish src/MinimalFences.App -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## 🇺🇸 English

**Minimal Fences** is a native Windows app to organize desktop shortcuts, files, and folders into customizable floating groups.

No ads, telemetry, or accounts required. Settings are stored locally; administrator privileges are not needed.

> The application stores references only. Adding or removing items will never move or delete original files on your drive.

### Features

- **Groups:** Create, move, resize, and rename desktop containers.
- **File Support:** Drag and drop `.lnk` shortcuts, `.exe` files, folders, documents, and `.url` web links.
- **Customization:** Background color/opacity, corner radius, borders, and 3 icon sizes.
- **Quick Toggle:** Double-click empty desktop space, use the system tray icon, or assign a global hotkey to show/hide groups.

### Download & Run

1. Go to **[Releases](../../releases)** and download `MinimalFences-win-x64.zip`.
2. Extract the archive (.NET runtime is bundled).
3. Run `MinimalFences.exe`.

### Build from Source

Requires **.NET 10 SDK**.

```bash
dotnet restore MinimalFences.slnx --locked-mode
dotnet build MinimalFences.slnx -c Release --no-restore
dotnet test tests/MinimalFences.Core.Tests -c Release --no-build
dotnet publish src/MinimalFences.App -c Release -r win-x64 --self-contained true -o artifacts/publish
```

