namespace FloatSpotify.Localization;

/// <summary>
/// 全部界面文案。**故意不用 .resx**：resx 会为每种语言生成一个卫星程序集
/// （zh-Hans/FloatSpotify.Next.resources.dll），和本项目「零依赖 + 单文件发布 +
/// <c>SatelliteResourceLanguages=en</c> 瘦身」的做法冲突，还得改 csproj 才能带上中文资源。
/// 一张普通字典就够，而且两种语言写在同一行，漏翻一眼就能看出来。
/// <para>
/// 约定：key 用 <c>区域_用途</c> 的 ASCII 命名；带占位符的用 <c>{0}</c>，取的时候走 <see cref="Loc.F"/>。
/// 新增文案时**必须同时填中文和英文**，缺失会回退成 key 本身（界面上会直接露出来）。
/// </para>
/// </summary>
internal static class Strings
{
    internal static readonly Dictionary<string, (string ChineseSimplified, string English)> Table =
        new(StringComparer.Ordinal)
        {
            // ── 通用 ──────────────────────────────────────────────
            ["App_Title"] = ("FloatSpotify", "FloatSpotify"),
            ["App_AlreadyRunning_Message"] =
                ("FloatSpotify Next 已在运行。请用托盘图标把歌词调出来。",
                 "FloatSpotify Next is already running. Use its tray icon to bring the lyrics back."),

            // ── 托盘菜单 ──────────────────────────────────────────
            ["Tray_ShowLyrics"] = ("显示歌词", "Show lyrics"),
            ["Tray_OpenSettings"] = ("打开设置", "Settings"),
            ["Tray_UnlockLyrics"] = ("解锁歌词", "Unlock lyrics"),
            ["Tray_ReauthorizeSpotify"] = ("重新授权 Spotify", "Re-authorize Spotify"),
            ["Tray_Exit"] = ("退出", "Exit"),

            // ── 语言选择 ──────────────────────────────────────────
            ["Language_Label"] = ("语言", "Language"),
            ["Language_System"] = ("跟随系统", "System default"),
            ["Language_ChineseSimplified"] = ("简体中文", "简体中文"),
            ["Language_English"] = ("English", "English"),

            // ── 控制条 ────────────────────────────────────────────
            ["Controls_Title"] = ("FloatSpotify 控制条", "FloatSpotify Controls"),
            ["Controls_Previous"] = ("上一首", "Previous track"),
            ["Controls_PlayPause"] = ("播放或暂停", "Play or pause"),
            ["Controls_Next"] = ("下一首", "Next track"),
            ["Controls_Settings"] = ("设置", "Settings"),
            ["Controls_LockToggle"] = ("锁定或解锁歌词", "Lock or unlock the lyrics"),
            ["Controls_Hide"] = ("隐藏歌词", "Hide the lyrics"),
            ["Controls_PlaybackSource"] = ("播放源", "Playback source"),
            ["Controls_ClientId"] = ("Client ID", "Client ID"),
            ["Controls_ClientId_Tooltip"] =
                ("保存到用户环境变量 FLOATSPOTIFY_SPOTIFY_CLIENT_ID",
                 "Saved to the user environment variable FLOATSPOTIFY_SPOTIFY_CLIENT_ID"),
            ["Controls_ClientId_Help"] = ("如何获取 Spotify Client ID", "How to get a Spotify Client ID"),
            ["Controls_FontSize"] = ("字号", "Font size"),
            ["Controls_LyricWidth"] = ("歌词宽度", "Lyric width"),
            ["Controls_ScreenPosition"] = ("屏幕位置", "Screen position"),
            ["Controls_Font"] = ("字体", "Font"),
            ["Controls_Opacity"] = ("透明度", "Opacity"),
            ["Controls_TextColor"] = ("文字颜色", "Text color"),
            ["Controls_LyricOffset"] = ("歌词偏移", "Lyric offset"),
            ["Controls_LyricOffset_Reset"] =
                ("重置当前歌曲的歌词偏移", "Reset the lyric offset for the current track"),
            ["Controls_LyricSources"] = ("歌词源", "Lyric sources"),
            ["Controls_LyricSources_MoveUp"] = ("提高优先级", "Raise priority"),
            ["Controls_LyricSources_MoveDown"] = ("降低优先级", "Lower priority"),
            ["Controls_LyricSources_Hint"] =
                ("列表顺序即优先级。所有已启用的源会同时发出请求，谁先给出可用的歌词就用谁；" +
                 "拿到逐字歌词后不再等其余源。全部关闭则不取词。",
                 "List order is priority. Every enabled source is queried at the same time and the first " +
                 "usable result wins; once word-level lyrics arrive the rest are dropped. With all sources " +
                 "off, no lyrics are fetched."),
            ["Controls_ShowNextLine"] = ("下一句", "Next line"),
            ["Controls_Glow"] = ("描边阴影", "Outline shadow"),
            ["Controls_AlwaysOnTop"] = ("始终置顶", "Always on top"),
            ["Controls_Authorize"] =
                ("使用此 Client ID 授权 Spotify", "Authorize Spotify with this Client ID"),
            ["Controls_Authorize_Tooltip"] =
                ("使用上方 Client ID 在浏览器中登录 Spotify",
                 "Sign in to Spotify in your browser using the Client ID above"),

            // ── 屏幕位置预设 ──────────────────────────────────────
            ["Placement_Custom"] = ("自由拖动", "Free drag"),
            ["Placement_Top"] = ("顶部居中", "Top center"),
            ["Placement_Center"] = ("屏幕中央", "Center"),
            ["Placement_Bottom"] = ("底部居中", "Bottom center"),
            ["Placement_TopLeft"] = ("左上角", "Top left"),
            ["Placement_TopRight"] = ("右上角", "Top right"),
            ["Placement_BottomLeft"] = ("左下角", "Bottom left"),
            ["Placement_BottomRight"] = ("右下角", "Bottom right"),

            // ── 字体下拉框的显示名（Tag 里是真实字体名，不翻译）────
            ["Font_SegoeUi"] = ("Segoe UI Variable", "Segoe UI Variable"),
            ["Font_MicrosoftYaHei"] = ("微软雅黑", "Microsoft YaHei"),
            ["Font_YuGothic"] = ("游ゴシック", "Yu Gothic"),
            ["Font_MalgunGothic"] = ("맑은 고딕", "Malgun Gothic"),
            ["Font_Cascadia"] = ("等宽 Cascadia", "Cascadia Mono"),

            // ── 文字颜色预设 ──────────────────────────────────────
            ["Color_White"] = ("白色", "White"),
            ["Color_SpotifyGreen"] = ("Spotify 绿", "Spotify green"),
            ["Color_Cyan"] = ("青色", "Cyan"),
            ["Color_Gold"] = ("金色", "Gold"),
            ["Color_Rose"] = ("玫红", "Rose"),

            // ── 歌词源名称 / 标签 / 说明 ──────────────────────────
            ["LyricsSource_Lrclib_Name"] = ("LRCLIB", "LRCLIB"),
            ["LyricsSource_Karalyr_Name"] = ("Karalyr · 逐字", "Karalyr · word-level"),
            ["LyricsSource_BetterLyrics_Name"] = ("Better Lyrics · 逐字", "Better Lyrics · word-level"),
            ["LyricsSource_NetEase_Name"] = ("网易云音乐", "NetEase Cloud Music"),
            ["LyricsSource_Kugou_Name"] = ("酷狗音乐 · 逐字", "Kugou Music · word-level"),
            ["LyricsSource_Official"] = ("公开接口", "Public API"),
            ["LyricsSource_Unofficial"] = ("非官方接口", "Unofficial API"),
            ["LyricsSource_Lrclib_Description"] =
                ("lrclib.net。免费、无需鉴权的社区歌词库，欧美曲库覆盖好。",
                 "lrclib.net. A free community lyric library with no authentication and good Western coverage."),
            ["LyricsSource_Karalyr_Description"] =
                ("公开 Enhanced LRC 逐字源；未命中、超时或限流时自动回退。",
                 "Public word-level Enhanced LRC source; falls back automatically on a miss, timeout or rate limit."),
            ["LyricsSource_BetterLyrics_Description"] =
                ("TTML 逐字/音节源；仅使用匿名缓存，需密钥或低置信度时自动回退。",
                 "Word/syllable-level TTML source; only the anonymous cache is used, and it falls back " +
                 "when a key is required or confidence is low."),
            ["LyricsSource_NetEase_Description"] =
                ("中文曲库覆盖明显更好。用的是非公开接口，可能随时失效，请自行判断是否使用。",
                 "Much better coverage of Chinese-language catalogues. It uses an unofficial API that may " +
                 "break at any time, so enable it at your own discretion."),
            ["LyricsSource_Kugou_Description"] =
                ("中文曲库覆盖好，且返回 KRC 逐字歌词。用的是非公开接口，可能随时失效，请自行判断是否使用。",
                 "Good coverage of Chinese-language catalogues and returns word-level KRC lyrics. It uses an " +
                 "unofficial API that may break at any time, so enable it at your own discretion."),

            // ── 悬浮歌词本身的兜底文案 ────────────────────────────
            ["Overlay_Connecting"] = ("正在连接播放状态…", "Connecting to playback…"),
            ["Overlay_NoLyrics_Hint"] =
                ("暂无歌词 · 点击打开控制条", "No lyrics yet · click to open the controls"),
            ["Overlay_PlainText_Tooltip"] =
                ("未同步歌词：滚动查看全文，不自动逐字高亮",
                 "Unsynced lyrics: scroll to read the full text, with no automatic highlighting"),
            ["Overlay_UnsyncedScroll"] = ("未同步歌词 · 滚动查看全文", "Unsynced lyrics · scroll to read"),

            // ── 取词过程中的提示（两个引擎共用）──────────────────
            ["Lyrics_Matching"] = ("正在匹配同步歌词…", "Matching synced lyrics…"),
            ["Lyrics_NotFound_Retrying"] = ("暂未找到歌词，将自动重试", "No lyrics found yet, retrying automatically"),

            // ── Spotify 引擎 ──────────────────────────────────────
            ["Spotify_NeedsAuth"] = ("需要授权", "Authorization required"),
            ["Spotify_ReauthorizeHint"] =
                ("点击歌词打开设置后可重新授权", "Click the lyrics, open settings, then re-authorize"),
            ["Spotify_Connecting"] = ("正在连接", "Connecting"),
            ["Spotify_ConnectingDetail"] = ("正在连接 Spotify…", "Connecting to Spotify…"),
            ["Spotify_FirstRunHint"] =
                ("首次运行会在浏览器中请求授权", "The first run asks for authorization in your browser"),
            ["Spotify_Idle"] = ("未播放", "Not playing"),
            ["Spotify_IdleDetail"] = ("Spotify 当前没有播放内容", "Spotify is not playing anything"),
            ["Spotify_IdleHint"] = ("请在任一设备开始播放", "Start playback on any device"),
            ["Spotify_Status_TokenExpired"] =
                ("Spotify 授权已过期，正在刷新…", "Spotify authorization expired, refreshing…"),
            ["Spotify_Status_PremiumRequired"] =
                ("播放控制需要 Spotify Premium 和重新授权权限",
                 "Playback control requires Spotify Premium and re-authorization permission"),
            ["Spotify_Status_ControlFailed"] =
                ("Spotify 控制失败 ({0})", "Spotify control failed ({0})"),
            ["Spotify_Status_StateFailed"] =
                ("Spotify 状态读取失败 ({0})", "Reading the Spotify state failed ({0})"),
            ["Spotify_Status_Offline"] =
                ("无法连接 Spotify，稍后会自动重试", "Can't reach Spotify; it will retry automatically"),
            ["Spotify_Status_NetworkRetry"] =
                ("Spotify 网络暂时不可用，正在重试", "Spotify network is temporarily unavailable, retrying"),
            ["Spotify_Status_UnknownState"] =
                ("Spotify 返回了无法识别的播放状态", "Spotify returned an unrecognized playback state"),
            ["Spotify_Auth_NetworkError"] =
                ("无法连接 Spotify 授权服务，请检查网络后重新授权。",
                 "Can't reach the Spotify authorization service. Check your network and authorize again."),
            ["Spotify_Auth_ReauthorizeFailed"] =
                ("重新授权失败，现有 Spotify 会话仍然可用",
                 "Re-authorization failed; the existing Spotify session still works"),
            ["Spotify_Retry_DateFormat"] = ("M月d日 HH:mm", "MMM d HH:mm"),
            ["Spotify_Retry_Hours"] =
                ("Spotify 将于 {0} 自动重试（约 {1} 小时）", "Spotify will retry at {0} (in about {1} h)"),
            ["Spotify_Retry_HoursMinutes"] =
                ("Spotify 将于 {0} 自动重试（约 {1} 小时 {2} 分钟）",
                 "Spotify will retry at {0} (in about {1} h {2} min)"),
            ["Spotify_Retry_Minutes"] =
                ("Spotify 将于 {0} 自动重试（约 {1} 分钟）", "Spotify will retry at {0} (in about {1} min)"),
            ["Spotify_Retry_Seconds"] =
                ("Spotify 将于 {0} 自动重试（{1} 秒）", "Spotify will retry at {0} (in {1} s)"),

            // ── Spotify 授权流程（含浏览器里显示的那几页）─────────
            ["SpotifyAuth_PortInUse"] =
                ("授权端口 8888 被占用。请先退出旧版 FloatSpotify，再从托盘选择“重新授权 Spotify”。",
                 "Authorization port 8888 is already in use. Quit the older FloatSpotify first, then pick " +
                 "\"Re-authorize Spotify\" from the tray icon."),
            ["SpotifyAuth_Browser_StateMismatch"] =
                ("授权校验失败，可以关闭此页面。", "Authorization check failed. You can close this page."),
            ["SpotifyAuth_StateMismatch"] =
                ("Spotify 授权校验失败，请重新授权。", "The Spotify authorization check failed. Please authorize again."),
            ["SpotifyAuth_Browser_Cancelled"] =
                ("Spotify 授权未完成，可以关闭此页面。",
                 "Spotify authorization did not complete. You can close this page."),
            ["SpotifyAuth_Cancelled"] = ("Spotify 授权被取消。", "Spotify authorization was cancelled."),
            ["SpotifyAuth_Browser_Success"] =
                ("Spotify 授权成功，可以关闭此页面。",
                 "Spotify authorization succeeded. You can close this page."),
            ["SpotifyAuth_Expired"] =
                ("Spotify 授权已失效，需要重新授权。",
                 "The Spotify authorization has expired and must be renewed."),
            ["SpotifyAuth_NoAccessToken"] =
                ("Spotify 未返回可用的访问令牌。", "Spotify did not return a usable access token."),
            ["SpotifyAuth_MissingClientId"] =
                ("请先打开设置，填写你自己的 32 位 Spotify Client ID。",
                 "Open settings first and enter your own 32-character Spotify Client ID."),

            // ── YouTube Music 引擎 ────────────────────────────────
            ["Ytm_SessionUnavailable"] = ("媒体会话不可用", "Media session unavailable"),
            ["Ytm_SessionUnavailableHint"] =
                ("请确认系统为 Windows 10 1809 或更高版本", "Requires Windows 10 1809 or later"),
            ["Ytm_Connecting"] = ("正在连接", "Connecting"),
            ["Ytm_ConnectingDetail"] =
                ("正在连接 Windows 媒体会话…", "Connecting to the Windows media session…"),
            ["Ytm_Idle"] = ("未播放", "Not playing"),
            ["Ytm_IdleDetail"] =
                ("YouTube Music 当前没有播放内容", "YouTube Music is not playing anything"),
            ["Ytm_IdleHint"] =
                ("请在浏览器或 PWA 中开始播放", "Start playback in the browser or the PWA"),
            ["Ytm_ManagerError"] = ("无法读取 Windows 媒体会话", "Can't read the Windows media session"),
            ["Ytm_Status_NoSession"] =
                ("未找到 YouTube Music 媒体会话", "No YouTube Music media session found"),
            ["Ytm_Status_CommandRejected"] =
                ("浏览器未接受播放控制命令", "The browser did not accept the playback command"),
            ["Ytm_Status_SessionExpired"] =
                ("YouTube Music 媒体会话已失效，正在重新连接",
                 "The YouTube Music media session expired, reconnecting"),
            ["Ytm_Status_Unavailable"] =
                ("YouTube Music 媒体状态暂时不可用", "YouTube Music media state is temporarily unavailable"),

            // ── Spotify Client ID 配置窗口 ────────────────────────
            ["Setup_Title"] = ("配置 Spotify Client ID", "Set up your Spotify Client ID"),
            ["Setup_Step1"] =
                ("1. 打开 Spotify Developer Dashboard，登录后创建 App；如果页面询问 API，请选择 Web API。",
                 "1. Open the Spotify Developer Dashboard, sign in and create an app. If it asks which API " +
                 "you need, choose Web API."),
            ["Setup_Step2"] =
                ("2. 打开 App Settings，在 Redirect URIs 中加入下面的地址并保存。",
                 "2. Open the app settings, add the address below to Redirect URIs and save."),
            ["Setup_Step3"] =
                ("3. 回到 App 页面复制 Client ID。不要复制 Client Secret。",
                 "3. Go back to the app page and copy the Client ID. Do not copy the Client Secret."),
            ["Setup_Step4"] =
                ("4. 把 32 位 Client ID 粘贴到 FloatSpotify 设置，然后点击授权按钮。",
                 "4. Paste the 32-character Client ID into the FloatSpotify settings and click the " +
                 "authorize button."),
            ["Setup_CopyCallback"] = ("复制回调地址", "Copy callback URL"),
            ["Setup_Copied"] = ("已复制", "Copied"),
            ["Setup_ClipboardError"] =
                ("无法访问剪贴板，请手动复制回调地址。",
                 "Can't access the clipboard. Please copy the callback URL manually."),
            ["Setup_SaveNote"] =
                ("Client ID 会保存在当前 Windows 用户的环境变量中；应用更新不会删除现有 Spotify 会话。",
                 "The Client ID is stored in an environment variable for the current Windows user; updating " +
                 "the app will not delete your existing Spotify session."),
            ["Setup_OpenDashboard"] = ("打开 Spotify Dashboard", "Open the Spotify Dashboard"),
            ["Setup_Close"] = ("关闭", "Close"),
            ["Setup_BrowserError_Title"] = ("无法打开浏览器", "Can't open the browser")
        };

    internal static string? Lookup(string key, AppLanguage language)
    {
        if (!Table.TryGetValue(key, out var entry))
            return null;

        return language == AppLanguage.English ? entry.English : entry.ChineseSimplified;
    }
}
