# ChestLink

**ChestLink** 是一款《星露谷物语》（Stardew Valley）的 SMAPI Mod，通过可放置的古代仓储终端管理世界各处的箱子与冰箱。

**ChestLink** is a Stardew Valley mod for SMAPI. It adds a placeable ancient storage terminal that lets you browse and transfer items across chests and fridges around the world. It also supports quick-storing from nearby terminals or chests, locked backpack items, multiplayer updates, and the game's current language.

## 模组简介 / Mod descriptions

| 语言 / Language | 简介 / Description |
| --- | --- |
| English | Browse and transfer items across chests and fridges using an ancient storage terminal. |
| Deutsch | Durchsuche Truhen und Kühlschränke und verschiebe Gegenstände über ein antikes Lagerterminal. |
| Español | Consulta y mueve objetos entre cofres y neveras con un antiguo terminal de almacenamiento. |
| Français | Consultez et transférez des objets entre coffres et réfrigérateurs grâce à un ancien terminal de stockage. |
| Magyar | Ősi raktárterminállal böngészheted és áthelyezheted a ládákban és hűtőkben tárolt tárgyakat. |
| Italiano | Consulta e trasferisci oggetti tra bauli e frigoriferi con un antico terminale di deposito. |
| 日本語 | 古代の収納端末で各地のチェストや冷蔵庫のアイテムを確認・移動できます。 |
| 한국어 | 고대 보관 단말기로 여러 상자와 냉장고의 물건을 확인하고 옮길 수 있습니다. |
| Português (Brasil) | Veja e transfira itens entre baús e geladeiras usando um antigo terminal de armazenamento. |
| Русский | Просматривайте и переносите предметы между сундуками и холодильниками через древний складской терминал. |
| Türkçe | Kadim bir depolama terminaliyle sandık ve buzdolaplarındaki eşyaları görüntüleyip taşıyın. |
| 简体中文 | 使用古代仓储终端查看和转移各处箱子与冰箱中的物品。 |

游戏内文字自动跟随游戏语言。SMAPI 的 `manifest.json` 描述在启动时读取，不能自动切换；发布脚本可指定 `-ManifestLocale` 为本机安装写入所选语言的描述。仓库中的默认发布包使用英文描述。

In-game text follows the game language automatically. SMAPI reads the manifest description at startup, so use `-ManifestLocale` when publishing a local install. The default release package keeps the English description.

## 中文说明

### 功能

- 放置并右键使用古代仓储终端，可查看农场世界中木箱、石箱、大箱子、迷你冰箱和房屋冰箱的物品。
- 支持搜索、分类、分页、整理，以及整组、单件、半组和保留一件的取物操作。
- 在终端附近按 **K**，可把背包物品存入已有相同物品堆叠的箱子。
- 在支持的箱子或冰箱附近按 **L**，可把背包物品存入附近容器中已有的相同物品堆叠。
- 锁定背包物品后，快捷存入会跳过它；锁图标也会显示在背包栏中。
- 多人游戏中同步仓库操作和打开中的仓库界面。所有玩家都需要安装 ChestLink。
- 界面文字和终端配方名称会跟随游戏语言；缺少翻译时使用英文。

仓库左侧的 **全部容器 / 箱子 / 冰箱** 页签与顶部物品分类组合使用。例如“箱子 + 农作物”只显示箱子里的农作物；“全部容器 + 农作物”显示箱子和冰箱里的农作物。出货箱不在仓库范围内。

### 安装

1. 安装 [SMAPI](https://smapi.io/)，并启动一次游戏。
2. 将发布包中的 `ChestLink` 文件夹放进游戏目录的 `Mods` 文件夹。
3. 多人游戏时，请让所有玩家都安装相同版本。

本仓库的可安装包位于 [`dist/ChestLink`](dist/ChestLink)。也可以在 Releases 下载发布包。

### 默认操作

仓库界面中的点击操作：

| 操作 | 键鼠 | 手柄 |
| --- | --- | --- |
| 取出整组 | 左键 | A |
| 取出整组并保留一件 | 左 Shift + 左键 | RB + A |
| 取出一个 | 右键 | X |
| 取出一半 | 左 Shift + 右键 | RB + X |
| 锁定/解锁背包物品 | 左 Ctrl + 左键 | LB + A |

其他快捷键：

| 操作 | 默认键 | 默认范围 |
| --- | --- | --- |
| 终端附近快捷存入 | K | 距终端 5 格内 |
| 箱子或冰箱附近快捷存入 | L | 距支持的容器 5 格内 |

可在设置菜单中调整快捷键和快捷存入距离。设置界面需要可选依赖 [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098)；不安装时仍可使用默认键位。

终端配方为 **20 木材、5 金锭、5 精炼石英**，每个存档只能制作一次。配方会自动解锁。

### 开发构建

需要 .NET 6 SDK，以及本机 Stardew Valley/SMAPI 安装供 ModBuildConfig 使用：

```powershell
dotnet build src/ChestLink/ChestLink.csproj -c Release
```

构建输出位于 `src/ChestLink/bin/Release/net6.0`。

发布或更新本机安装时，使用以下脚本同步 DLL 与资源，并删除旧版残留的 `ChestLink.pdb`（发布包不需要调试符号）：

```powershell
.\scripts\Publish-Local.ps1 -ModsPath 'E:\SteamLibrary\steamapps\common\Stardew Valley\Mods' -ManifestLocale zh-CN
```

只更新 `dist/ChestLink` 时，省略 `-ModsPath`。`-ManifestLocale` 可使用 `i18n` 文件名（不含 `.json`），例如 `de`、`ja`、`pt-BR`、`zh-CN`。运行安装命令前请先关闭游戏。

## English

### Features

- Place and right-click the Ancient Storage Terminal to browse wooden, stone, and large chests, mini-fridges, and farmhouse fridges across the world.
- Search, filter by category, page through items, organize storage, and withdraw full stacks, one item, half a stack, or all but one.
- Press **K** near a terminal to store backpack items in chests that already contain a matching stack.
- Press **L** near a supported chest or fridge to quick-store items into matching stacks in nearby containers.
- Lock backpack items to skip them during quick storage. Lock icons also appear in the toolbar.
- Multiplayer storage actions and open storage views are synchronized. Every player should install ChestLink.
- The mod follows the game's language for its UI and recipe name, with English as the fallback.

The **All containers / Chests / Fridges** tabs on the left combine with the item categories along the top. For example, Chests + Crops shows crops stored in chests; All containers + Crops includes chests and fridges. Shipping bins are excluded.

### Installation

1. Install [SMAPI](https://smapi.io/) and launch the game once.
2. Copy the `ChestLink` folder from the release package into the game's `Mods` folder.
3. For multiplayer, all players should use the same mod version.

The installable package in this repository is [`dist/ChestLink`](dist/ChestLink). Release packages may also be downloaded from Releases.

For local publishing, run `scripts/Publish-Local.ps1` with `-ModsPath` set to your game's `Mods` folder and `-ManifestLocale` set to an `i18n` filename without `.json` (for example `de`, `ja`, or `zh-CN`). The script builds the mod, updates `dist/ChestLink` and the local installation, and removes any stale `ChestLink.pdb` from both folders. Omit `-ModsPath` to update only `dist/ChestLink`. Close the game before installing.

### Default controls

Storage menu clicks:

| Action | Keyboard and mouse | Controller |
| --- | --- | --- |
| Take a full stack | Left-click | A |
| Take a full stack and leave one | Left Shift + left-click | RB + A |
| Take one item | Right-click | X |
| Take half a stack | Left Shift + right-click | RB + X |
| Lock/unlock a backpack item | Left Ctrl + left-click | LB + A |

Other shortcuts:

| Action | Default key | Default range |
| --- | --- | --- |
| Quick-store near a terminal | K | Within 5 tiles of a terminal |
| Quick-store near chests or fridges | L | Within 5 tiles of a supported container |

Shortcuts and quick-store range can be changed in the settings menu. The settings UI requires the optional [Generic Mod Config Menu](https://www.nexusmods.com/stardewvalley/mods/5098); default controls still work without it.

The terminal recipe costs **20 Wood, 5 Gold Bars, and 5 Refined Quartz**. Each save can craft one terminal, and the recipe is learned automatically.

### Build from source

Requires the .NET 6 SDK and a local Stardew Valley/SMAPI installation for ModBuildConfig:

```powershell
dotnet build src/ChestLink/ChestLink.csproj -c Release
```

Build output: `src/ChestLink/bin/Release/net6.0`.
