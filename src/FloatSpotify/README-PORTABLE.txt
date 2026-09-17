FloatSpotify Next - Windows x64 便携版 / Windows x64 portable
================================================================

界面语言可在设置里切换（跟随系统 / 简体中文 / English）。
The interface language can be switched in the settings (System default / 简体中文 / English).


--------------------------------------------------------------
【中文】
--------------------------------------------------------------

系统要求：Windows 10 1809 (build 17763) 或更高，x64。

使用方法：
1. 完整解压 ZIP，不能只从压缩包中直接运行 EXE。
2. 运行 FloatSpotify.Next.exe。
3. 点击悬浮歌词打开设置，选择 Spotify 或 YouTube Music。
4. 使用 Spotify 时，填写你自己的 Spotify Client ID，然后点击授权按钮。

不需要安装、不写注册表，删掉文件夹即可卸载。

关于 .NET 运行时，看包内文件就能分辨：
- 只有 FloatSpotify.Next.exe 和本说明 → 自包含便携版，已内置 .NET 8，无需另行安装。
- 除 EXE 外还有一堆 .dll → 框架依赖版，需要你先装好 .NET 8 桌面运行时
  （https://dotnet.microsoft.com/download/dotnet/8.0），否则双击没反应。

如果你更希望走安装流程（开始菜单快捷方式、卸载项、自动补运行时），
请到 Releases 页面下载 setup-online 或 setup-offline 安装包
（文件名形如 FloatSpotifyNext-x86_64-v1.0.0-setup-online.exe）。

悬浮歌词未锁定时，用设置里的“歌词宽度”滑块调整宽度（160–1600 px）。
设置还提供字号、字体、RGB 颜色以及顶部、中央、底部和四角位置预设；选择“自由拖动”可手动放置。
点“锁定”后窗口鼠标穿透，不会挡住任何操作；可从托盘图标解锁。

播放源：
- Spotify：首次使用会打开浏览器，请完成 Spotify 授权。
- YouTube Music：使用 Chrome、Edge、Firefox、Brave、Opera、Vivaldi
  或已安装的 YouTube Music PWA 播放音乐，无需 Google 授权。
- YouTube Music 模式读取 Windows 媒体会话；如果浏览器同时播放多个媒体标签页，
  请暂停其他标签页，确保应用选择到正在播放的音乐。

Spotify 注意事项：
- 初次使用需获取 Client ID（只有 Premium 才有），获取步骤：
  1. 打开 https://developer.spotify.com/dashboard 并登录。
  2. 创建一个 App；如果页面询问使用的 API，请选择 Web API。
  3. 打开 App Settings，在 Redirect URIs 中加入并保存：
     http://127.0.0.1:8888/callback
  4. 复制 App 页面显示的 32 位 Client ID，不要复制 Client Secret。
  5. 把 Client ID 填入 FloatSpotify 设置，点击“使用此 Client ID 授权 Spotify”。
- Client ID 输入框右侧的“?”按钮可随时打开应用内配置指南。
- Client ID 保存在用户环境变量 FLOATSPOTIFY_SPOTIFY_CLIENT_ID 中。
- Spotify Dashboard 中必须配置回调地址：http://127.0.0.1:8888/callback
- 使用自己的 Spotify 应用可避免受到发布者账号白名单的限制。

用户设置、歌词缓存和 Spotify 会话保存在：
%LocalAppData%\FloatSpotify.Next

覆盖更新应用不会删除 Spotify 会话。歌词偏移会按歌曲分别记忆；如果个别歌词仍有偏差，
调整“歌词偏移”只会影响当前歌曲，旁边的重置按钮可恢复为 0 秒。


--------------------------------------------------------------
【English】
--------------------------------------------------------------

Requirements: Windows 10 1809 (build 17763) or later, x64.

Getting started:
1. Extract the whole ZIP first — running the EXE straight from the archive will not work.
2. Run FloatSpotify.Next.exe.
3. Click the floating lyrics to open the settings and pick Spotify or YouTube Music.
4. With Spotify, enter your own Spotify Client ID and click the authorize button.

No installation and no registry entries; deleting the folder uninstalls it.

You can tell which .NET flavour you have by looking at the files in the package:
- Only FloatSpotify.Next.exe and this readme -> self-contained portable build with .NET 8
  bundled; nothing else to install.
- A pile of .dll files next to the EXE -> framework-dependent build; install the
  .NET 8 Desktop Runtime first (https://dotnet.microsoft.com/download/dotnet/8.0),
  otherwise double-clicking does nothing.

If you would rather use an installer (Start menu shortcut, uninstall entry, runtime
installed for you), download setup-online or setup-offline from the Releases page
(file names look like FloatSpotifyNext-x86_64-v1.0.0-setup-online.exe).

While the lyrics are unlocked, adjust their width with the "Lyric width" slider in the
settings (160-1600 px). The settings also cover font size, font family, RGB colour and
presets for the top, centre, bottom and four corners; choose "Free drag" to place them
by hand. Clicking "lock" makes the window click-through so it never blocks anything;
unlock it again from the tray icon.

Playback sources:
- Spotify: the first run opens your browser, so finish the Spotify authorization there.
- YouTube Music: play music in Chrome, Edge, Firefox, Brave, Opera, Vivaldi or an
  installed YouTube Music PWA. No Google authorization is needed.
- The YouTube Music mode reads the Windows media session. If your browser plays several
  media tabs at once, pause the others so the app picks the music you are listening to.

Spotify notes:
- On first use you need your own Client ID (Premium only). Steps:
  1. Open https://developer.spotify.com/dashboard and sign in.
  2. Create an app; if it asks which API you need, choose Web API.
  3. Open the app settings, add the address below to Redirect URIs and save:
     http://127.0.0.1:8888/callback
  4. Copy the 32-character Client ID shown on the app page. Do not copy the Client Secret.
  5. Paste the Client ID into the FloatSpotify settings and click
     "Authorize Spotify with this Client ID".
- The "?" button next to the Client ID field opens the in-app guide at any time.
- The Client ID is stored in the user environment variable FLOATSPOTIFY_SPOTIFY_CLIENT_ID.
- The redirect URI must be configured in the Spotify Dashboard:
  http://127.0.0.1:8888/callback
- Using your own Spotify app avoids the publisher account's allow-list limits.

Settings, the lyrics cache and the Spotify session live in:
%LocalAppData%\FloatSpotify.Next

Updating the app in place will not delete your Spotify session. Lyric offsets are
remembered per song; if an individual song is still off, adjusting "Lyric offset"
affects only the current song, and the reset button next to it restores 0 seconds.
