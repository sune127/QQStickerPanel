# QQStickerPanel

## 中文

QQStickerPanel 是一个 Windows 桌面表情包伴随面板。它会识别 QQ 聊天窗口，自动吸附到聊天窗口侧边，并提供表情包分类浏览、复制、拖入导入、拖出发送、最近使用、收藏、标签和基础管理能力。

### 主要功能

- 自动识别 QQ / QQNT 聊天窗口并吸附到左侧、右侧、上方、下方或窗口内侧。
- QQ 未启动、QQ 不在前台或聊天窗口关闭时，面板可自动隐藏到托盘。
- 支持静默开机启动，启动后默认进入托盘。
- 按目录生成分类，支持分类拖动排序和分类右键管理。
- 支持最近、收藏、未分类、标签和全部视图。
- 点击复制表情，双击可尝试粘贴到 QQ。
- 支持从资源管理器、QQ、剪贴板或虚拟文件拖入导入。
- 支持把表情拖出到 QQ 或资源管理器。
- 支持重命名、移动分类、合并分类、删除到回收站、批量重命名和重复清理。
- 启动时优先读取本地索引缓存，再后台增量扫描表情目录。

### 运行环境

默认发布包是依赖系统运行时的单文件 exe。目标机器需要安装：

- Windows 10/11 x64
- Microsoft .NET 8 Windows Desktop Runtime x64

如果目标机器不能预装运行时，可以使用自包含发布模式。

### 开发环境

- .NET SDK 8.0 或更高版本
- Windows Desktop workload / WPF 支持
- Windows x64

构建：

```powershell
dotnet build .\QQStickerPanel.sln --configuration Release
```

发布依赖系统运行时的单文件版本：

```powershell
.\scripts\publish-win-x64.ps1
```

输出目录：

```text
artifacts\QQStickerPanel-win-x64-framework-dependent
```

发布自包含单文件版本：

```powershell
.\scripts\publish-win-x64.ps1 -DeploymentMode SelfContainedSingleFile
```

输出目录：

```text
artifacts\QQStickerPanel-win-x64-self-contained
```

### 使用说明

1. 启动程序。
2. 在设置中选择表情包根目录。
3. 将图片放在根目录或子目录中；子目录会作为分类显示。
4. 打开 QQ 聊天窗口，面板会自动吸附。
5. 点击表情复制，双击表情尝试粘贴到 QQ。
6. 拖入图片可导入表情，拖出表情可发送或复制到文件夹。

常用快捷键：

- `Ctrl + Alt + Q`：隐藏或恢复面板
- `F1`：快捷键说明
- `Ctrl + Tab` / `Ctrl + Shift + Tab`：切换分类
- `Ctrl + C`：复制选中表情
- `Ctrl + V`：从剪贴板导入
- `Delete`：删除选中表情
- `Esc`：清除选择

### 数据位置

配置、最近使用、收藏、标签和索引缓存默认保存在：

```text
%AppData%\QQStickerPanel
```

主要文件包括：

- `settings.json`
- `recent.json`
- `favorites.json`
- `metadata.db`
- `sticker-index.db`

---

## English

QQStickerPanel is a Windows desktop companion panel for managing and sending QQ stickers. It detects QQ chat windows, docks next to them, and provides categorized browsing, copy, drag-in import, drag-out send, recent stickers, favorites, tags, and basic management tools.

### Features

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

### Runtime Requirements

The default publish output is a framework-dependent single-file executable. Target machines need:

- Windows 10/11 x64
- Microsoft .NET 8 Windows Desktop Runtime x64

Use the self-contained publish mode if target machines cannot preinstall the runtime.

### Development Requirements

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

### Usage

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

### Data Location

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
