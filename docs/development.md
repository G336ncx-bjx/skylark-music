# 开发说明

面向要改代码 / 自己构建的人。使用说明看 [README](../README.md)。

## 目录结构

```
build.ps1                    电脑版构建（系统自带 csc.exe，把 XAML 作为资源内嵌进 exe）
scripts/make-icon.ps1        生成 Windows 图标（assets/app.ico）
scripts/make-android-icon.ps1 生成安卓图标（圆角方形 + 自适应图标前景）
scripts/icon-artwork.ps1     两个平台共用的图标绘制代码
scripts/install.ps1          构建 + 创建桌面 / 开始菜单快捷方式
src/Program.cs               入口：单实例、自检 / 截图 / 冒烟 / 各种命令行工具
src/MainWindow.cs            主窗口外壳：字段、播放控制、托盘、全局热键、设置持久化
src/MainWindow.Ui.cs         主窗口界面构建（顶栏 / 侧栏 / 播放条 / 提示条）
src/MainWindow.Library.cs    曲库：扫描、过滤、排序、下载、删除、歌单
src/MainWindow.Playback.cs   播放调度：打开曲目、云盘缓存、进度与歌词同步
src/MainWindow.Upload.cs     上传到云盘
src/MainWindow.MusicImport.cs 在线搜索和入库界面、后台调度
src/MusicSource.cs           独立 Edge 会话、音乐网站 API、文件校验与入库重试
src/Modal.cs                 应用内弹窗卡片（下载 / 加入歌单 / 新建歌单…）
src/LibraryView.cs           音乐库视图（列表 / 排序 / 右键菜单）
src/QueueView.cs             播放队列视图（增删排序 / M3U 导入导出）
src/LyricsView.cs            软件内歌词页（逐行高亮 + 平滑滚动）
src/DesktopLyricsWindow.cs   桌面歌词浮窗（置顶 / 拖动 / 鼠标穿透锁定）
src/SettingsView.cs          设置页
src/PlayerEngine.cs          基于 WPF MediaPlayer 的播放内核（含平滑进度估算）
src/Services.cs              目录探测、LRC 解析、时长解析、扫描、配置、M3U
src/CloudClient.cs           云盘 API（分享链接 / 资料库令牌、上传、删除）
src/CloudLibrary.cs          云盘曲库扫描与本地缓存
src/Ffmpeg.cs                ffmpeg 定位与转码（ogg / opus / ape / wv）
src/Models.cs                数据模型与设置项
src/Theme.cs                 配色、矢量图标、控件工厂
android/build.ps1            安卓构建（aapt2 → javac → d8 → zipalign → apksigner）
android/AndroidManifest.xml  清单（权限、前台播放服务、分享上传入口）
android/src/…                安卓源码（界面 / 前台服务 / 云盘 / 歌词 / 设置）
android/src/…/AppShell.java        安卓基础层：调色板、控件工厂、通用刷新
android/src/…/LibraryScreen.java   安卓音乐库层：歌单、多选、下载、扫描、上传
android/src/…/PlayerScreen.java    安卓播放层：播放条、播放队列、歌词页
android/src/…/SettingsScreen.java  安卓设置层：设置页、应用内更新
android/src/…/MusicImportDialog.java 安卓在线搜索、WebView 验证、入库调度
android/src/…/MusicImportFiles.java  安卓入库确认、重试和清理（纯 Java 可自检）
android/src/…/MainActivity.java    安卓入口：生命周期与页面组装（继承上面四层）
android/src/…/SongAdapter.java     音乐库 / 队列共用的列表适配器
android/tools/SelfTest.java  安卓端纯 Java 逻辑自检
```

## 构建

两个平台都不需要 Gradle / 第三方库。

```powershell
# 电脑版：用系统自带的 .NET Framework 编译器，产物 dist\Skylark.exe
powershell -ExecutionPolicy Bypass -File build.ps1

# 程序正在运行（exe 被占用）时，可以先编到别处
powershell -ExecutionPolicy Bypass -File build.ps1 -OutDir "$env:TEMP\skylark-dev"

# 安卓版：需要 JDK + Android SDK（仓库里的 .toolchain、或 ANDROID_HOME）
# 先跑纯 Java 自检，再打包签名，产物 dist\Skylark-android.apk
powershell -ExecutionPolicy Bypass -File android\build.ps1
```

安卓构建有两个坑，脚本里已经处理：

- `aapt2` / `zipalign` 是原生程序，读不了中文路径，所以编译在 `%TEMP%\skylark-android-build` 里进行，
  最后把 APK 复制回 `dist`；
- 类文件多起来会超过 cmd 的命令行长度上限，所以先打成 jar 再交给 d8。

## 签名与发布

Android 后续版本要能覆盖安装，必须用同一个密钥签名，所以密钥不进仓库：

- 本机：`android\skylark.jks` + `android\keystore.pass`（都在 `.gitignore` 里，第一次构建没有会自动生成）；
- CI / Release：从仓库 Secrets 读 `ANDROID_KEYSTORE_BASE64` 与 `ANDROID_KEYSTORE_PASS`，
  这样发布出去的 APK 和本机编的签名一致，可以互相覆盖安装。

发版流程：打 `v*` 标签并推送 → GitHub Actions 自动跑两端自检、构建 exe + APK，
生成 `SHA256SUMS.txt` 并发布 Release。

```powershell
git tag -a v3.3.0 -m "云雀 v3.3.0：……"
git push origin v3.3.0
```

## 命令行工具

```powershell
# 无界面自检：歌词解析（含双语配对）、文件名解析、时长、扫描、配置、M3U、真实播放
dist\Skylark.exe --selftest

# 在线找歌弹窗的离屏截图（演示配置，不访问真实云盘）
dist\Skylark.exe --shot out.png music-search dark

# 离屏渲染界面截图，便于检查排版（library / lyrics / queue / settings / desktop / desktop-locked）
dist\Skylark.exe --shot out.png lyrics dark

# 真实启动界面 5 秒后自动退出（冒烟测试）；加 play 会真的播一首
dist\Skylark.exe --smoke
dist\Skylark.exe --smoke play 关键词

# 验证桌面歌词“锁定”是否真的鼠标穿透（用 WindowFromPoint 做命中测试）
dist\Skylark.exe --lockcheck

# 核对双语歌词的配对：打印成 [时间] 原文 || 译文
dist\Skylark.exe --lyricdump "某个歌词.lrc"

# 整库校验：逐首拉文件头解析时长 + 统计歌词覆盖率
dist\Skylark.exe --cloudtest <令牌或分享链接>

# 把本地文件夹一次性补齐到云端（缺的上传、同名同大小跳过、大小不同覆盖）
dist\Skylark.exe --uploadall <令牌或分享链接> "D:\某个文件夹"

# 删除云端文件
dist\Skylark.exe --clouddelete <令牌> <云盘路径>
```

> 自检 / 截图 / 冒烟等模式都使用**临时数据目录**，不会碰你 `%APPDATA%\Skylark` 里的真实配置和播放列表。

## 一些实现细节

- **零依赖**：电脑版直接用 Windows 自带的 .NET Framework 4.x + WPF，用 `csc.exe` 编译，
  产物是单个 exe；安卓版只用系统 API（`MediaPlayer` + 前台服务 + `MediaSession`），
  APK 里只有一个 `classes.dex`，没有 Gradle、没有 AndroidX、没有原生 .so。
- **在线入库**：Windows 通过 .NET Framework 的 WebSocket 调用独立 Edge 的 DevTools 协议，
  仅绑定本机回环端口，使用独立 `music-browser` 配置目录，退出播放器时关闭该会话。
  Android 通过 `WebView.evaluateJavascript` 在音乐网站同源页面调用公开接口，不暴露 JavaScript native bridge。
  两端仅在网站会话中传递歌曲 ID、平台和网站签名，云盘令牌仅用于原有云盘客户端。
  音频和歌词都落在独立的 `music-import/<任务哈希>` 目录；云端核对失败时保留本地文件，全部确认后才清理。
  自检覆盖同名冲突、上传中断重试、云端大小不符和成功清理，不需要真实云盘凭据。
- **播放**：两端都先把歌取到本机再交给系统播放内核（原因见 README 的「本地占用」），
  取到的同时会预取下一首，所以切歌几乎不用等。
- **歌词解析**：自动识别 UTF-8 / UTF-16 / GBK；支持一行多时间戳、`offset` 偏移。
  双语歌词按**文件顺序**配对（译文紧跟它自己的原文），哪种文字是原文由 `[ti:标题]` 的语言决定，
  其次看第一行，最后才看多数派——只用「谁多」会判反（日语歌的中文译文常多出「词：/曲：」信息行）。
- **时长**：界面上不再显示时长，因此扫描时也不再去拉每个文件的前 512KB 解析（省流量、扫描更快）。
- **配置位置**：电脑版 `%APPDATA%\Skylark\settings.json`（不可写时退回 exe 同级 `data`）；
  安卓版存在应用私有目录。缓存默认只留两首，可在设置里改成全部保留。
- **进度平滑**：电脑版的 `MediaPlayer` 位置更新较粗糙，用锚点 + 秒表插值让进度条与歌词滚动更顺。
