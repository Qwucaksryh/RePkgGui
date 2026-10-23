# RePKG 提取工具（RePkgGui）

[![release](https://img.shields.io/github/v/release/Qwucaksryh/RePkgGui?label=release)](https://github.com/Qwucaksryh/RePkgGui/releases/latest)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![platform](https://img.shields.io/badge/platform-Windows%2011-0078D4.svg)](#)

Wallpaper Engine 的 **PKG / MPKG 解包** 与 **TEX 转图片** 图形界面。

> **不想编译？** 直接到 [Releases](https://github.com/Qwucaksryh/RePkgGui/releases/latest) 下载
> `RePkgGui-v1.0.0-win-x64.exe`（63 MB 自包含单文件），双击即用，无需安装 .NET。
解析核心直接复用开源项目 [RePKG](https://github.com/notscuffed/repkg) 的 `RePKG.Core` / `RePKG.Application`，本程序只负责界面与流程编排。

- 框架：WPF (.NET 10) + [WPF-UI](https://github.com/lepoco/wpfui) 4.3.0
- 风格：Windows 11 Fluent Design，Mica 背景、圆角、深浅色自动跟随
- 纯原生渲染，无 WebView2 / Electron，常驻内存约 60–90 MB

> ### 关于本项目的来源
>
> **界面与流程编排代码由 AI（WorkBuddy）生成**，解析核心直接复用开源项目 RePKG，未重写任何解包逻辑。
>
> 代码**实测可用**：自带 43 项端到端断言（现场构造 PKG / MPKG / TEX 样本），
> 并在真实壁纸包上完整跑过解包；但未经长期生产环境验证，请按“可用但不保证无缺陷”对待。
> 遇到问题欢迎提 Issue，附上日志里的报错行即可。
>
> 开源协议与上游一致：**MIT**（见 [LICENSE](LICENSE)）。

---

## 1. 目录结构

```
RePkgGui/
├─ RePkgGui.sln
├─ README.md
├─ publish/                       # 发布产物（单文件 exe）
│  └─ RePkgGui.exe
├─ lib/                           # RePKG 官方源码（vendor，仅保留两个库工程）
│  ├─ RePKG.Core/                 #   netstandard2.0 —— PKG / TEX 数据结构与解析接口
│  └─ RePKG.Application/          #   netstandard2.0 —— PackageReader / TexReader / TexToImageConverter
├─ src/RePkgGui/                  # WPF 主工程
│  ├─ App.xaml(.cs)               # 应用入口、主题字典、共享样式
│  ├─ MainWindow.xaml(.cs)        # 主窗口：顶部栏 / 拖拽区 / 路径 / 进度 / 日志 / 拖放
│  ├─ Assets/app.ico
│  ├─ Models/
│  │  ├─ AppSettings.cs           # 配置模型 + 主题枚举
│  │  ├─ LogEntry.cs              # 日志条目
│  │  └─ RepkgOptions.cs          # 任务参数 / 进度 / 结果统计
│  ├─ Services/
│  │  ├─ RepkgService.cs          # ★ 对 RePKG 的封装（流式解包、TEX 转换、进度回调）
│  │  ├─ SettingsService.cs       # %AppData%\RePkgGui\settings.json 读写
│  │  ├─ ThemeService.cs          # 主题应用 + 系统主题变化监听
│  │  └─ BackdropHelper.cs        # 非 Win11 上关闭 Mica 的降级保护
│  ├─ ViewModels/
│  │  ├─ ObservableObject.cs      # 极简 INotifyPropertyChanged
│  │  ├─ RelayCommand.cs          # 同步 / 异步命令
│  │  └─ MainViewModel.cs         # 主窗口全部状态与交互
│  ├─ Views/
│  │  ├─ SettingsWindow.xaml(.cs) # 设置弹窗
│  │  └─ AboutWindow.xaml(.cs)    # 关于弹窗
│  └─ Converters/
│     └─ InverseBoolToVisibilityConverter.cs
└─ tests/RepkgSmokeTest/          # 端到端冒烟测试（构造 PKG/MPKG/TEX 样本并断言产物）
```

---

## 2. 引用 RePKG 的方式

采用 **ProjectReference 直接引用源码工程**（`lib/` 下是 RePKG 官方仓库的 `RePKG.Core` 与 `RePKG.Application` 原样拷贝，未改业务代码）。

```xml
<ItemGroup>
  <ProjectReference Include="..\..\lib\RePKG.Core\RePKG.Core.csproj" />
  <ProjectReference Include="..\..\lib\RePKG.Application\RePKG.Application.csproj" />
</ItemGroup>
```

两个库都是 `netstandard2.0`，可被 `net10.0-windows` 直接引用，无需额外适配层。

相对上游只做了两处工程化调整（都在 `lib/RePKG.Application/RePKG.Application.csproj`）：

1. `SixLabors.ImageSharp` 由 `2.1.9` 提升到 `2.1.13`（同主版本、API 兼容，修掉已知 CVE）。
2. 移除未被任何代码使用的 `System.Drawing.Common` 引用，减小体积。

如果不想带源码，也可以改成 DLL 引用：

```xml
<Reference Include="RePKG.Core">
  <HintPath>lib\bin\RePKG.Core.dll</HintPath>
</Reference>
<Reference Include="RePKG.Application">
  <HintPath>lib\bin\RePKG.Application.dll</HintPath>
</Reference>
```

---

## 3. 构建与运行

需要 **.NET 10 SDK**（本工程用 `10.0.113` 验证）。

```bash
# 构建
dotnet build RePkgGui.sln -c Release

# 直接运行
dotnet run --project src/RePkgGui/RePkgGui.csproj -c Release

# 跑端到端冒烟测试（会自动生成样本并断言输出）
dotnet run --project tests/RepkgSmokeTest -c Release
```

### 发布

```bash
# 自包含单文件（目标机无需安装 .NET，约 70 MB）
dotnet publish src/RePkgGui/RePkgGui.csproj -c Release -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -o publish
```

产物：`publish/RePkgGui.exe`，双击即用。

若目标机已装 .NET 10 桌面运行时，可用体积更小的框架依赖发布：

```bash
dotnet publish src/RePkgGui/RePkgGui.csproj -c Release -r win-x64 \
  --self-contained false -p:PublishSingleFile=true -o publish-fx
```

> WPF 不支持 IL 裁剪（Trim），请勿添加 `PublishTrimmed`，否则 XAML 反射会失败。

---

## 4. NuGet 包

| 包 | 版本 | 用途 |
| --- | --- | --- |
| WPF-UI | 4.3.0 | Fluent 控件、FluentWindow、主题管理、Snackbar |
| SixLabors.ImageSharp | 2.1.13 | RePKG 解码 DXT / 输出 PNG、GIF |
| K4os.Compression.LZ4 | 1.3.8 | RePKG 解 LZ4 压缩的 mipmap |
| Newtonsoft.Json | 13.0.3 | RePKG 的 TEX 信息导出（库内依赖） |

---

## 5. 行为说明

### 输出目录规则

文件夹名优先取 PKG 同级 `project.json` 里的 `title`（壁纸名称）：

| 输入 | 输出文件夹 |
| --- | --- |
| `431960/3132017530/scene.pkg`，`project.json` 里 `"title":"星空动态壁纸"` | `<输出目录>/星空动态壁纸/` |
| 没有 `project.json`，或里面没有 `title` | 回退成包文件名，`dlc.mpkg` → `<输出目录>/dlc/` |
| 两个壁纸标题重名 | 自动追加 `_2`、`_3` |

- 标题里的 Windows 非法字符（`\ / : * ? " < > |`）会被替换成 `_`，超过 80 字符会被截断
- 输入是文件夹 → 递归查找所有 `*.pkg` / `*.mpkg`，各自按上面规则生成子目录
- 单个 `.tex` → 直接输出到 `<输出目录>/<文件名>.png`
- 日志里会打印 `✔ scene.pkg 完成 → 星空动态壁纸\`，方便对照

### 文件处理

| 包内条目 | 处理方式 |
| --- | --- |
| `.tex`（普通贴图） | 解码为 `.png`（DXT1/3/5 自动解压，GIF 贴图输出 `.gif`） |
| `.tex`（视频贴图） | 输出 `.mp4` |
| `.mp4` / `.webm` / `.avi` / `.mov` / `.mkv` / `.wmv` | **原样导出**，不转码 |
| 其它任意文件（json、bin、ttf…） | 原样导出，保留包内目录结构 |
| `.tex` 解码失败 | 退化为原样导出 `.tex`，并在日志里给出黄色告警 |

### TEX 是什么

`.tex` 是 Wallpaper Engine 自己的**贴图容器**，不是普通图片格式——它把 GPU 直接可用的压缩像素、
mipmap 链、动图帧表打包在一起，外部看图软件打不开。

> 打个比方：PNG 是“一张图”，TEX 是“一个装图的盒子”——里面除了图还有缩放用的多级备份（mipmap）、
> 可能还有动画帧。游戏直接吃这个格式，Windows 却不认识，所以需要转一道手。

本工具调用 RePKG 完成解码，支持范围：

| TEX 内部格式 | 结果 |
| --- | --- |
| 未压缩像素 RGBA8888 / R8 / RG88 | → `.png` |
| 压缩像素 DXT1 / DXT3 / DXT5 | 先解压成 RGBA8888，再 → `.png` |
| 动图贴图（`IsGif` 标记 + 帧表） | → `.gif`（保留每帧延时） |
| 视频贴图（TEXB0004 + FIF_MP4） | 直接输出容器里的 H.264 → `.mp4` |
| LZ4 压缩的 mipmap | 自动解压后再按上表处理 |

界面里「输入路径」右上角的 **支持范围 ⓘ** 与「关于」窗口都内置了这份说明。

- 解包使用「只解析文件头 + 按需 seek」的方式读取条目，**不会把整个 PKG 读进内存**。
- 条目路径做了越界校验，含 `..\` 的恶意条目会被拒绝写入，不会逃出输出目录。
- 默认同时把 PKG 旁边的 `project.json` 和 `preview.*` 复制到输出目录（可在设置里关闭）。

### 配置文件

`%AppData%\RePkgGui\settings.json`

```json
{
  "Theme": "System",
  "OutputDirectory": "C:////Users////<你>////Desktop",
  "OpenOutputWhenDone": false,
  "Overwrite": "Overwrite",
  "NoTexConvert": false,
  "KeepRawTex": false,
  "MediaOnly": false,
  "SkipExtensions": "",
  "FlatOutput": false
}
```

### 设置项

设置窗口分四组：

| 分组 | 选项 | 说明 |
| --- | --- | --- |
| 外观 | 跟随系统 / 浅色 / 深色 | 跟随系统时会监听系统主题变化 |
| 输出 | 默认输出目录 | 主界面输出框的初始值 |
| 输出 | 提取完成后自动打开输出目录 | 完成后直接用资源管理器打开结果目录 |
| 输出 | 已有同名文件时：覆盖 / 跳过 | 统计里分开显示「已存在跳过」与「被规则忽略」 |
| 提取内容 | **不转换 TEX，.tex 原样导出** | 只解包不转图：更快、不多占空间，但需其它工具才能看图 |
| 提取内容 | 转换的同时保留原始 .tex 文件 | 需要二次处理贴图时打开（勾上一项时自动禁用） |
| 提取内容 | **仅保留图片和视频** | 只导出 png/jpg/gif/mp4/webm 等媒体文件，json、bin、字体全部跳过 |

| 提取内容 | 不导出的扩展名 | 逗号分隔，如 `.json, .bin` |
| 输出 | **忽略包内目录结构，所有文件放在同一层** | 不再创建 materials/video 子目录；同名文件自动加 `_2`、`_3` |

---

## 6. 已验证内容

`tests/RepkgSmokeTest` 会现场构造 PKG / MPKG / TEX，覆盖 40 项断言，全部通过：

- RGBA8888 / DXT5 贴图正确转为 8×8 PNG
- 视频贴图（TEXB0004 + FIF_MP4）输出为 mp4，且字节与原数据一致
- 包内 mp4 / webm / json 原样导出
- 中文目录名与中文文件名的转换
- 损坏 TEX 退化为保留原文件并告警
- `..\` 路径穿越被拒绝
- `project.json` / `preview.jpg` 复制
- 单文件 `.tex` 直接转换
- 进度回调、文件计数、图片计数、告警/错误计数
- 不覆盖模式下重跑全部跳过（`Skipped == 10`）
- 忽略 `.json` 后不写出 config.json、其它文件照常导出

---

## 7. 许可

本项目以 **MIT** 发布，**与上游保持一致**。完整文本见 [LICENSE](LICENSE)。

| 组件 | 来源 | 许可 |
| --- | --- | --- |
| 解析核心 `lib/RePKG.Core`、`lib/RePKG.Application` | [RePKG](https://github.com/notscuffed/repkg) © notscuffed | MIT（源码原样 vendor，见 [lib/README.md](lib/README.md)） |
| 界面框架 | [WPF-UI](https://github.com/lepoco/wpfui) © lepo.co | MIT |
| 应用代码、测试 | 本仓库 | MIT |

`LICENSE` 中同时保留了 RePKG 的版权声明（MIT 要求衍生作品保留原始声明）。

> 使用本工具解包的素材，其版权归原作者 / Wallpaper Engine 创意工坊作者所有，
> 请不要将解包得到的素材用于侵权用途。
