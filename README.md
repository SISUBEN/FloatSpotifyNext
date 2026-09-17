# FloatSpotify Next

Windows 桌面悬浮歌词组件。透明、置顶、可拖动的悬浮层，实时显示当前播放歌曲的同步歌词——不用切窗口就能跟着唱。

<!-- 换成你自己的截图 -->
<!-- <img width="960" alt="FloatSpotify Next" src="docs/screenshot.png"> -->

## 功能

- **悬浮歌词**：透明背景、始终置顶，可自由拖动到屏幕任意位置
- **逐字同步**：有真实词/音节时间轴时平滑扫亮当前词，未唱部分弱化；没有词级数据时回退逐行同步，可选显示下一行
- **双播放源**：Spotify 与 YouTube Music，设置里一键切换
- **宽度可调**：160–1600 px，拖动悬浮层右侧竖条即可实时调整
- **外观自定义**：字号 22–96、RGB 取色、发光效果、透明度
- **位置预设**：自由拖动 / 顶部居中 / 屏幕中央 / 底部居中 / 四角
- **锁定模式**：锁定后窗口鼠标穿透，完全不挡操作
- **歌词偏移**：按歌曲单独记忆偏移量（±5 秒），个别歌词对不上时手动校准
- **单实例**：重复启动会提示，托盘图标可随时唤回歌词

## 播放源

### Spotify

走官方 Web API（PKCE 授权，无需 Client Secret）。需要你自己提供一个 Spotify Client ID，步骤见下方「配置 Spotify」。

播放控制（上一首 / 暂停 / 下一首）通过 API 实现。

### YouTube Music

**不需要任何 Google 授权**。读取 Windows 系统媒体会话（GSMTC），所以浏览器里放的 YouTube Music 也能识别。支持 Chrome、Edge、Firefox、Brave、Opera、Vivaldi，以及已安装的 YouTube Music PWA。

播放控制同样走系统媒体会话，无需额外配置。

> 如果浏览器同时播放多个媒体标签页，请暂停其他标签页，确保应用选中正在播放音乐的那个会话。

## 歌词源与降级

- 沿用原有 WPF / MVVM、播放引擎、`ILyricsProvider`、源开关和排序，无新增服务器或 UI 框架。
- **有歌词优先**：先查询已启用的基础源（LRCLIB / 可选网易云），拿到歌词立即交给界面；随后查询 Karalyr / Better Lyrics 增强源，失败保留原歌词，不让效果查询阻塞显示。同类源之间遵循用户排序。网易云仍默认关闭，已有开关与顺序保留，全部关闭时不发请求。
- “可用”首先要求版本相符。明确标注 English/Chinese/Japanese/Korean Ver. 的曲目会检查返回标题及歌词文字脚本；错误语言不会覆盖正确语言的低同步级别结果。完整版本标题继续参与缓存键，v5 缓存重新获取旧数据。这是针对明显错配的脚本校验，不是通用语言识别。
- 无歌词/加载状态保留文字和最小拖动区域；未锁定时预览鼠标事件负责拖动、点击，普通歌词内容不截获鼠标。未同步全文的滚动条保留原生操作，锁定仍保持穿透。
- 没有同步歌词时也保留 LRCLIB 的普通文本，可滚动阅读，明确标为未同步。暂停冻结、跳转进度重定位，歌词偏移同时作用于行和词。动画使用 WPF 渲染帧，不增加播放接口轮询频率。
- 每源 3 秒超时；404/401/空结果顺延，429 尊重 `Retry-After`，网络和解析错误隔离并短暂熔断。无歌词的当前歌曲每 30 秒重新检查；仍尊重限流冷却及 5 分钟的缺失结果缓存，不重复请求未命中接口。
- 磁盘缓存按源、歌手、歌名、时长区分，保存词级时间轴；逐字结果 7 天、行级结果 1 天过期，旧 v3 缓存自然失效。当前播放元数据接口没有传递专辑，本次不扩展该接口。
- Enhanced LRC 缺少最后一个词的结束标记时，使用下一行/歌曲结束边界；连边界也缺失时降为行级，不均分整行伪造词时间。
- TTML 支持该服务的绝对时钟时间与带 begin/end 的分组 div、嵌套词 span，保留空格；背景人声/翻译不拼入主唱行。词时间范围异常时保留该行而不丢弃整首；已返回的匹配分低于 80 时不采用。
- 全部源都没有可用文本时显示歌曲信息和自动重试提示。未引入 lyrics.ovh 或新的非官方 KuGou 服务，也不把普通文本伪装成逐字歌词。
- API 依据：[Karalyr 文档](https://www.karalyr.com/docs)、[Better Lyrics 格式](https://lyrics-api-docs.boidu.dev/docs/response-format/)、[匿名缓存与鉴权](https://lyrics-api-docs.boidu.dev/docs/authentication/)。实际歌曲的逐字覆盖取决于上游。

开发回归（无测试框架 NuGet 依赖；包含模拟 HTTP、解析、缓存、设置迁移和 WPF 像素验证）：

```powershell
dotnet run --project tests/FloatSpotify.Lyrics.Tests -c Release -- artifacts/lyrics-word-sync
```

渲染证据使用自建测试歌词，不代表真实歌曲命中或播放器端到端验收。

## 系统要求

- Windows 10 build 17763（1809）或更高版本，x64
- 运行时的要求取决于你选的安装方式，见下表

## 安装

| 方式 | 下载体积 | 需要 .NET 8 桌面运行时 |
|---|---|---|
| 在线安装包 | 约 10–20 MB | 安装程序自动检测，缺失才联网下载（约 55 MB） |
| 离线安装包 | 约 70 MB | 不需要，已内置 |
| 便携版 zip | 约 70 MB | 不需要，已内置 |

### 方式一：在线安装包（推荐）

下载 `FloatSpotify.Next-<版本>-Setup-Online.exe` 并运行。安装程序会检测 .NET 8 桌面运行时，已装则直接安装，未装才联网下载。**安装过程需要联网和管理员权限。**

### 方式二：离线安装包

下载 `FloatSpotify.Next-<版本>-Setup-Offline.exe` 并运行。完全离线可用，不依赖网络，适合无网环境或批量部署。

### 方式三：便携版

下载 `FloatSpotify.Next-<版本>-Portable-win-x64.zip`，**完整解压后**运行里面的 `FloatSpotify.Next.exe`。

> 不能直接在压缩包里双击运行——必须解压。详见包内 `README-PORTABLE.txt`。

三种方式都会在开始菜单创建快捷方式；安装包版本还会注册卸载项，并可选创建桌面快捷方式。

## 使用

- **单击歌词**：打开 / 关闭控制条
- **拖动歌词**：移动悬浮层位置
- **拖右侧竖条**：调整歌词宽度（未锁定时可用，也可用设置里的滑块）
- **锁定按钮**：锁定后鼠标穿透，点不到歌词
- **托盘图标**：右键可显示歌词、打开设置、解锁、重新授权 Spotify、退出

### 数据存放位置

设置、Spotify 会话和歌词缓存都在：

```
%LocalAppData%\FloatSpotify.Next\
├─ settings.json          界面与行为设置
├─ spotify-session.json   Spotify 授权令牌
└─ lyrics\                歌词缓存（按源、歌手、曲名、时长区分）
```

**卸载不会删除这个目录**，所以重装后无需重新授权 Spotify。想彻底清理请手动删除该文件夹。

## 配置 Spotify

发布包**不内置任何人的 Client ID**，需要你自己申请一个（免费，几分钟）：

1. 打开 <https://developer.spotify.com/dashboard> 并登录
2. 点击 **Create app**；如果页面询问要使用的 API，选择 **Web API**
3. 打开该 App 的 **Settings**，在 **Redirect URIs** 中加入并保存：

   ```
   http://127.0.0.1:8888/callback
   ```

4. 复制 App 页面上显示的 **32 位 Client ID**（不要复制 Client Secret，本应用用不到）
5. 打开 FloatSpotify 设置，把 Client ID 粘进输入框，点击「使用此 Client ID 授权 Spotify」
6. 浏览器会打开 Spotify 授权页，登录并同意即可

Client ID 保存在用户环境变量 `FLOATSPOTIFY_SPOTIFY_CLIENT_ID` 中，不会写进程序目录。设置里的「?」按钮可随时打开应用内配置指南。

使用自己的 Spotify 应用还能避开发布者账号白名单的限制。

## 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```sh
dotnet build src/FloatSpotify/FloatSpotify.csproj -c Release
```

或用 Visual Studio 打开 `FloatSpotify.Next.sln`。

### 项目结构

```
src/FloatSpotify/
├─ Playback/      播放源抽象与实现
│  ├─ IPlaybackEngine.cs          播放源接口
│  ├─ PlaybackCoordinator.cs      按播放源路由
│  ├─ SpotifyPlaybackEngine.cs    Spotify 实现
│  ├─ YouTubeMusicPlaybackEngine.cs  YouTube Music 实现（系统媒体会话）
│  ├─ LrclibLyricsProvider.cs     歌词抓取与缓存
│  └─ SpotifyAuthClient.cs        PKCE 授权
├─ ViewModels/    状态与设置绑定
├─ Windows/       悬浮层、控制条、授权引导窗口
└─ Storage/       设置持久化
```

新增播放源的步骤：实现 `IPlaybackEngine`，在 `PlaybackSource` 枚举中加一个值，然后把新引擎接进 `PlaybackCoordinator`。

## 发布新版本

需要 [Inno Setup 6](https://jrsoftware.org/isdl.php)（仅在线/离线安装包需要，`winget install JRSoftware.InnoSetup` 可装）。

```powershell
powershell -ExecutionPolicy Bypass -File build\build-release.ps1
```

脚本会读取 csproj 里的 `<Version>`，产出三个产物到 `artifacts/`：

- `FloatSpotify.Next-<版本>-Setup-Online.exe`
- `FloatSpotify.Next-<版本>-Setup-Offline.exe`
- `FloatSpotify.Next-<版本>-Portable-win-x64.zip`

改版本号只需改 `src/FloatSpotify/FloatSpotify.csproj` 里的 `<Version>`，也可以临时用 `-Version 1.2.0` 覆盖。加 `-SkipInstallers` 可只出便携版。

> 关于体积：自包含发布约 165 MB（已裁掉 13 种本地化卫星程序集）。WPF 不支持 `PublishTrimmed`，所以无法进一步裁剪；体积大头是 .NET 运行时和 `Microsoft.Windows.SDK.NET.dll`（约 21 MB，YouTube Music 所需的 WinRT 投影）。

## 已知限制

- 仅支持 Windows x64
- 无自动更新，升级需重新运行安装包
- 歌词来自 [lrclib.net](https://lrclib.net)，冷门曲目可能没有歌词
- YouTube Music 模式依赖系统媒体会话，同时播放多个媒体源时可能选错会话

## 致谢

本项目最初 fork 自 [BitsJayMehta173/FloatSpotify](https://github.com/BitsJayMehta173/FloatSpotify)（WPF + Python Flask 后端方案）。当前版本是移除 Python 依赖后的完整重写，原方案代码已从仓库移除，仍可在上游仓库查阅。

歌词数据由 [lrclib.net](https://lrclib.net) 提供。
