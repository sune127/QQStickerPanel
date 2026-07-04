# QQStickerPanel

[中文](README.zh-CN.md)

QQStickerPanel is a Windows desktop companion panel for managing and sending QQ stickers. It detects QQ chat windows, docks next to them, and provides categorized browsing, copy, drag-in import, drag-out send, recent stickers, favorites, tags, and basic management tools.

## Features

- Detects QQ / QQNT chat windows and docks to the left, right, top, bottom, or inner side.
- Hides to tray automatically when QQ is not running, not in the foreground, or the chat window is closed.
- Supports silent startup for Windows autostart.
- Builds categories from folders and supports drag sorting plus context-menu management.
- Provides Recent, Favorites, Uncategorized, Tag, and All views.
- Click to copy a sticker; double-click to try pasting it into QQ.
- Imports images from Explorer, QQ, clipboard image data, text paths, and virtual file drops.
- Drags stickers out to QQ or Explorer.
- Supports rename, move, category merge, recycle-bin delete, batch rename, and duplicate cleanup.
- Loads cached sticker indexes first, then refreshes changes incrementally in the background.

## Runtime Requirements

The default publish output is a framework-dependent single-file executable. Target machines need:

- Windows 10/11 x64
- Microsoft .NET 8 Windows Desktop Runtime x64

Use the self-contained publish mode if target machines cannot preinstall the runtime.

## Development Requirements

- .NET SDK 8.0 or later
- Windows Desktop / WPF support
- Windows x64

Build:

```powershell
dotnet build .\QQStickerPanel.sln --configuration Release
```

Publish a framework-dependent single-file build:

```powershell
.\scripts\publish-win-x64.ps1
```

Output:

```text
artifacts\QQStickerPanel-win-x64-framework-dependent
```

Publish a self-contained single-file build:

```powershell
.\scripts\publish-win-x64.ps1 -DeploymentMode SelfContainedSingleFile
```

Output:

```text
artifacts\QQStickerPanel-win-x64-self-contained
```

## Usage

1. Start the app.
2. Pick a sticker root folder in Settings.
3. Put images in the root folder or subfolders; subfolders become categories.
4. Open a QQ chat window and the panel will dock automatically.
5. Click a sticker to copy it; double-click to try pasting it into QQ.
6. Drag images into the panel to import them; drag stickers out to send or copy them.

Common shortcuts:

- `Ctrl + Alt + Q`: hide or restore the panel
- `F1`: shortcut help
- `Ctrl + Tab` / `Ctrl + Shift + Tab`: switch categories
- `Ctrl + C`: copy selected stickers
- `Ctrl + V`: import from clipboard
- `Delete`: delete selected stickers
- `Esc`: clear selection

## Data Location

Settings, recent usage, favorites, tags, and sticker indexes are stored under:

```text
%AppData%\QQStickerPanel
```

Main files:

- `settings.json`
- `recent.json`
- `favorites.json`
- `metadata.db`
- `sticker-index.db`
