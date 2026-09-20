using System;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Skylark
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                LogCrash(e.ExceptionObject as Exception);
            };

            if (args.Length > 0 && args[0] == "--selftest")
            {
                AttachConsole();
                AppPaths.UseIsolatedDataDir("selftest");
                Environment.Exit(SelfTest.Run());
                return;
            }
            if (args.Length > 0 && args[0] == "--shot")
            {
                AttachConsole();
                AppPaths.UseIsolatedDataDir("shot");
                int code = 1;
                try
                {
                    code = ShotMode.Run(args);
                }
                catch (Exception ex)
                {
                    LogCrash(ex);
                    Console.WriteLine("shot failed: " + ex.Message);
                }
                Environment.Exit(code);
                return;
            }
            if (args.Length > 0 && args[0] == "--smoke")
            {
                AttachConsole();
                AppPaths.UseIsolatedDataDir("smoke");
                Environment.Exit(SmokeRun(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--lockcheck")
            {
                AttachConsole();
                AppPaths.UseIsolatedDataDir("lockcheck");
                Environment.Exit(LockCheck());
                return;
            }
            if (args.Length > 0 && args[0] == "--streamtest")
            {
                AttachConsole();
                Environment.Exit(StreamTest(args.Length > 1 ? args[1] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--lyricdump")
            {
                AttachConsole();
                Environment.Exit(LyricDump(args.Length > 1 ? args[1] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--lyricfix")
            {
                AttachConsole();
                Environment.Exit(LyricFix(args.Length > 1 ? args[1] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--lyricshift")
            {
                AttachConsole();
                double shift;
                if (args.Length < 3 || !double.TryParse(args[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out shift))
                {
                    Console.WriteLine("usage: Skylark.exe --lyricshift <歌词文件或目录> <秒数，可为负>");
                    Environment.Exit(1);
                    return;
                }
                Environment.Exit(LyricFix(args[1], shift));
                return;
            }
            if (args.Length > 0 && args[0] == "--cloudtest")
            {
                AttachConsole();
                Environment.Exit(CloudTest(args.Length > 1 ? args[1] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--uploadtest")
            {
                AttachConsole();
                Environment.Exit(UploadTest(args.Length > 1 ? args[1] : null,
                    args.Length > 2 ? args[2] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--clouddelete")
            {
                AttachConsole();
                Environment.Exit(CloudDelete(args.Length > 1 ? args[1] : null,
                    args.Length > 2 ? args[2] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--cloudupload")
            {
                AttachConsole();
                Environment.Exit(CloudUpload(args.Length > 1 ? args[1] : null,
                    args.Length > 2 ? args[2] : null, args.Length > 3 ? args[3] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--cloudmove")
            {
                AttachConsole();
                Environment.Exit(CloudMove(args));
                return;
            }
            if (args.Length > 0 && args[0] == "--cloudmkdir")
            {
                AttachConsole();
                Environment.Exit(CloudMkdir(args.Length > 1 ? args[1] : null,
                    args.Length > 2 ? args[2] : null));
                return;
            }
            if (args.Length > 0 && args[0] == "--uploadall")
            {
                AttachConsole();
                Environment.Exit(UploadAll(args.Length > 1 ? args[1] : null,
                    args.Length > 2 ? args[2] : null));
                return;
            }

            bool createdNew;
            Mutex mutex = new Mutex(true, "SkylarkPlayer_SingleInstance", out createdNew);
            if (!createdNew)
            {
                FocusRunningInstance();
                return;
            }

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                LogCrash(e.Exception);
            };
            Theme.EnsureStyles();

            MainWindow window = new MainWindow();
            app.MainWindow = window;
            app.Run(window);

            GC.KeepAlive(mutex);
        }

        private static void LogCrash(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Skylark-error.log");
                System.IO.File.AppendAllText(path,
                    DateTime.Now.ToString("s") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine,
                    System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE_LOCAL = -20;
        private const int WS_EX_TRANSPARENT_LOCAL = 0x00000020;
        private const int WS_EX_TOOLWINDOW_LOCAL = 0x00000080;
        private const int WS_EX_NOACTIVATE_LOCAL = 0x08000000;
        private const int WS_EX_TOPMOST_LOCAL = 0x00000008;
        private const int SW_MINIMIZE_LOCAL = 6;
        private static readonly IntPtr HWND_NOTOPMOST_LOCAL = new IntPtr(-2);
        private const uint SWP_NOMOVE_LOCAL = 0x0002;
        private const uint SWP_NOSIZE_LOCAL = 0x0001;
        private const uint SWP_NOACTIVATE_LOCAL = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        /// <summary>窗口当前的缩放系数（150% 显示 = 1.5）。</summary>
        private static double ScaleOf(System.Windows.DependencyObject visual)
        {
            try
            {
                PresentationSource source = PresentationSource.FromVisual(visual as System.Windows.Media.Visual);
                if (source != null && source.CompositionTarget != null)
                {
                    return source.CompositionTarget.TransformToDevice.M11;
                }
            }
            catch (Exception)
            {
            }
            return 0;
        }

        /// <summary>
        /// 验证桌面歌词「锁定」是否真的鼠标穿透：
        /// 用 WindowFromPoint 检查窗口中心那一像素究竟命中了哪个窗口。
        /// </summary>
        private static int LockCheck()
        {
            StringBuilder report = new StringBuilder();
            try
            {
                MainWindow.Headless = true;
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                Theme.EnsureStyles();

                MainWindow main = new MainWindow();
                bool original = main.Settings.LyricLocked;
                main.SetLyricLocked(false, false);
                main.ShowDesktopLyrics(true);
                DesktopLyricsWindow lyrics = main.DesktopLyrics;
                if (lyrics == null)
                {
                    Report(report, "lockcheck failed: desktop lyrics window not created");
                    return 1;
                }
                Pump(0.8);

                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(lyrics).Handle;
                if (handle == IntPtr.Zero)
                {
                    Report(report, "lockcheck failed: no window handle");
                    return 1;
                }

                bool hitUnlocked = HitTest(handle);
                int styleUnlocked = GetWindowLong(handle, GWL_EXSTYLE_LOCAL);

                main.SetLyricLocked(true, false);
                Pump(0.5);
                bool hitLocked = HitTest(handle);
                int styleLocked = GetWindowLong(handle, GWL_EXSTYLE_LOCAL);

                // 锁定时应出现一个始终可点的「解锁」按钮窗口
                bool unlockClickable = false;
                bool unlockVisible = false;
                LyricsUnlockWindow unlock = lyrics.UnlockButton;
                if (unlock != null)
                {
                    IntPtr unlockHandle = new System.Windows.Interop.WindowInteropHelper(unlock).Handle;
                    int unlockStyle = GetWindowLong(unlockHandle, GWL_EXSTYLE_LOCAL);
                    unlockClickable = (unlockStyle & WS_EX_TRANSPARENT_LOCAL) == 0 && HitTest(unlockHandle);
                    unlockVisible = unlock.IsVisible;

                    // 实际渲染尺寸（排查「按钮看起来比代码里大」这类问题）
                    RECT unlockRect;
                    if (GetWindowRect(unlockHandle, out unlockRect))
                    {
                        System.Windows.FrameworkElement body = unlock.Content as System.Windows.FrameworkElement;
                        string inner = body == null ? "?" : (Math.Round(body.ActualWidth, 1) + "x"
                            + Math.Round(body.ActualHeight, 1) + " DIP, 期望 "
                            + Math.Round(body.DesiredSize.Width, 1) + "x"
                            + Math.Round(body.DesiredSize.Height, 1));
                        Report(report, "解锁按钮实际尺寸 = "
                            + (unlockRect.Right - unlockRect.Left) + "x" + (unlockRect.Bottom - unlockRect.Top)
                            + " 物理像素 / " + Math.Round(unlock.ActualWidth, 1) + "x"
                            + Math.Round(unlock.ActualHeight, 1) + " DIP"
                            + "（本窗口缩放 " + Math.Round(ScaleOf(unlock), 2) + "x"
                            + "，桌面歌词窗口 " + Math.Round(ScaleOf(lyrics), 2) + "x）");
                        Report(report, "解锁按钮内容 = " + inner + "，SizeToContent=" + unlock.SizeToContent);
                        Report(report, "解锁按钮 Width/Height 属性 = " + unlock.Width + " / " + unlock.Height
                            + "，WindowState=" + unlock.WindowState
                            + "，Left/Top=" + Math.Round(unlock.Left, 1) + "," + Math.Round(unlock.Top, 1));
                    }
                }

                // 鼠标靠近才显示 / 离开后自动隐藏
                POINT origin;
                GetCursorPos(out origin);
                RECT lyricRect;
                GetWindowRect(handle, out lyricRect);
                SetCursorPos((lyricRect.Left + lyricRect.Right) / 2, (lyricRect.Top + lyricRect.Bottom) / 2);
                Pump(0.8);
                bool shownWhenNear = lyrics.UnlockButton != null && lyrics.UnlockButton.IsVisible;
                SetCursorPos(4, 4);
                Pump(2.2);
                bool hiddenWhenFar = lyrics.UnlockButton == null || !lyrics.UnlockButton.IsVisible;
                Report(report, "轮询次数 = " + lyrics.PollTicks + "，自动隐藏次数 = " + lyrics.UnlockHideCalls
                    + "，最后一次判定 = " + lyrics.LastUnlockRect);
                SetCursorPos(origin.X, origin.Y);
                Pump(0.3);

                // ---- 自愈测试：模拟 Win+D 把它最小化、以及别的程序抢走置顶 ----
                main.SetLyricLocked(false, false);
                Pump(0.3);
                ShowWindow(handle, SW_MINIMIZE_LOCAL);
                Pump(0.5);
                bool minimized = IsIconic(handle);
                Pump(4.5);
                bool restored = !IsIconic(handle) && lyrics.WindowState == WindowState.Normal;
                bool topmostBack = (GetWindowLong(handle, GWL_EXSTYLE_LOCAL) & WS_EX_TOPMOST_LOCAL) != 0;
                Report(report, "模拟 Win+D 把它最小化        = " + minimized + "（期望 True）");
                Report(report, "自愈后自己恢复显示           = " + restored + "（期望 True）");
                Report(report, "自愈后置顶样式还在           = " + topmostBack + "（期望 True）");

                SetWindowPos(handle, HWND_NOTOPMOST_LOCAL, 0, 0, 0, 0,
                    SWP_NOMOVE_LOCAL | SWP_NOSIZE_LOCAL | SWP_NOACTIVATE_LOCAL);
                Pump(3.2);
                bool topmostRestored = (GetWindowLong(handle, GWL_EXSTYLE_LOCAL) & WS_EX_TOPMOST_LOCAL) != 0;
                Report(report, "置顶被抢走后自动恢复         = " + topmostRestored + "（期望 True）");
                Report(report, "自愈次数                     = " + lyrics.HealthFixes);

                main.SetLyricLocked(original, false);
                main.ShowDesktopLyrics(false);

                bool transparent = (styleLocked & WS_EX_TRANSPARENT_LOCAL) != 0;
                bool transparentBefore = (styleUnlocked & WS_EX_TRANSPARENT_LOCAL) != 0;
                bool noActivate = (styleLocked & WS_EX_NOACTIVATE_LOCAL) != 0;
                bool toolWindow = (styleLocked & WS_EX_TOOLWINDOW_LOCAL) != 0;

                Report(report, "解锁时窗口中心命中桌面歌词 = " + hitUnlocked + "（期望 True）");
                Report(report, "锁定后窗口中心命中桌面歌词 = " + hitLocked + "（期望 False）");
                Report(report, "解锁时 WS_EX_TRANSPARENT    = " + transparentBefore + "（期望 False）");
                Report(report, "锁定后 WS_EX_TRANSPARENT    = " + transparent + "（期望 True）");
                Report(report, "锁定后 WS_EX_NOACTIVATE     = " + noActivate + "（期望 True，不抢焦点）");
                Report(report, "锁定后 WS_EX_TOOLWINDOW     = " + toolWindow + "（期望 True，不占 Alt+Tab）");
                Report(report, "锁定后解锁按钮可见可点     = " + (unlockVisible && unlockClickable) + "（期望 True）");
                // 受限环境下 GetCursorPos 会直接失败（沙箱、非交互会话），那时这两项没法判定
                bool noCursorAccess = lyrics.LastUnlockRect != null
                    && lyrics.LastUnlockRect.IndexOf("取光标失败", StringComparison.Ordinal) >= 0;
                Report(report, "鼠标靠近时显示解锁按钮     = "
                    + (noCursorAccess ? "跳过（当前进程读不到鼠标位置）" : shownWhenNear.ToString() + "（期望 True）"));
                Report(report, "鼠标离开后自动隐藏         = "
                    + (noCursorAccess ? "跳过（当前进程读不到鼠标位置）" : hiddenWhenFar.ToString() + "（期望 True）"));

                bool ok = hitUnlocked && !hitLocked && !transparentBefore && transparent && noActivate
                    && toolWindow && unlockVisible && unlockClickable
                    && (noCursorAccess || (shownWhenNear && hiddenWhenFar))
                    && minimized && restored && topmostBack && topmostRestored;
                Report(report, ok ? "LOCKCHECK OK" : "LOCKCHECK FAILED");
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Report(report, "lockcheck failed: " + ex.Message);
                return 1;
            }
        }

        private static void Report(StringBuilder report, string line)
        {
            Console.WriteLine(line);
            report.AppendLine(line);
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skylark-lockcheck.log"),
                    report.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
            }
        }

        private static bool HitTest(IntPtr handle)
        {
            RECT rect;
            if (!GetWindowRect(handle, out rect)) return false;
            POINT point = new POINT();
            point.X = (rect.Left + rect.Right) / 2;
            point.Y = (rect.Top + rect.Bottom) / 2;
            return WindowFromPoint(point) == handle;
        }

        private static void Pump(double seconds)
        {
            System.Windows.Threading.DispatcherFrame frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(seconds);
            timer.Tick += delegate
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        /// <summary>真实启动一次界面（显示窗口若干秒后自动退出），用于冒烟测试。</summary>
        /// <summary>
        /// 把歌词文件按 App 内解析器解析的结果打印出来，用于核对双语配对：
        /// 每行格式 [时间] 原文 || 译文
        /// </summary>
        private static int LyricDump(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("usage: Skylark.exe --lyricdump <歌词文件路径>");
                return 1;
            }
            try
            {
                LyricDocument doc = LrcParser.Load(path);
                if (!doc.Found)
                {
                    Console.WriteLine("解析失败：" + doc.Message);
                    return 1;
                }
                Console.WriteLine("文件：" + path);
                Console.WriteLine("同步：" + doc.Synced + "，共 " + doc.Lines.Count + " 行");
                foreach (LyricLine line in doc.Lines)
                {
                    Console.WriteLine("[" + line.Time.ToString("0.00").PadLeft(7) + "] "
                        + line.Text + (string.IsNullOrEmpty(line.Translation) ? "" : "   ||   " + line.Translation));
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("lyricdump failed: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// 把双语歌词规范化成「译文与原文同一时间戳」的写法（就地改写）。
        /// 可以传单个 .lrc，也可以传目录（递归处理）。
        /// </summary>
        private static int LyricFix(string path)
        {
            return LyricFix(path, 0);
        }

        private static int LyricFix(string path, double shiftSeconds)
        {
            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("usage: Skylark.exe --lyricfix <歌词文件或目录>");
                return 1;
            }
            List<string> files = new List<string>();
            if (System.IO.Directory.Exists(path))
            {
                files.AddRange(System.IO.Directory.GetFiles(path, "*.lrc", System.IO.SearchOption.AllDirectories));
            }
            else if (System.IO.File.Exists(path))
            {
                files.Add(path);
            }
            else
            {
                Console.WriteLine("找不到：" + path);
                return 1;
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);

            int changed = 0;
            foreach (string file in files)
            {
                try
                {
                    string original = TextUtil.ReadAllTextSmart(file);
                    string fixedText = LrcParser.Normalize(original, shiftSeconds);
                    if (string.Equals(original, fixedText, StringComparison.Ordinal)) continue;
                    System.IO.File.WriteAllText(file, fixedText, new UTF8Encoding(false));
                    changed++;
                    Console.WriteLine(shiftSeconds == 0
                        ? "  已规范化：" + System.IO.Path.GetFileName(file)
                        : "  已平移 " + shiftSeconds.ToString("+0.0;-0.0") + " 秒："
                          + System.IO.Path.GetFileName(file));
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  失败 " + System.IO.Path.GetFileName(file) + "：" + ex.Message);
                }
            }
            Console.WriteLine("共 " + files.Count + " 个歌词文件，改写了 " + changed + " 个");
            return 0;
        }

        private static int StreamTest(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("usage: Skylark.exe --streamtest <url>");
                return 1;
            }

            StringBuilder report = new StringBuilder();
            try
            {
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                PlayerEngine engine = new PlayerEngine();
                bool opened = false;
                string error = null;
                engine.Opened += delegate { opened = true; };
                engine.Failed += delegate(object sender, EventArgs e)
                {
                    PlayerErrorArgs args = e as PlayerErrorArgs;
                    error = args == null ? "unknown" : args.Message;
                };
                engine.Volume = 0;

                Song song = new Song();
                song.Path = url;
                song.Title = System.IO.Path.GetFileName(url);
                Report(report, "file = " + song.Title);
                engine.Open(song, true, 0);
                Pump(7);

                Report(report, "opened=" + opened
                    + " playing=" + engine.IsPlaying + " duration=" + engine.Duration.ToString("0.0") + "s"
                    + " pos=" + engine.GetPosition().ToString("0.0") + "s"
                    + (error == null ? "" : " error=" + error));

                if (opened)
                {
                    engine.Seek(60);
                    Pump(2.5);
                    Report(report, "seek to 60s -> " + engine.GetPosition().ToString("0.0") + "s");
                }
                engine.Close();
                Report(report, opened ? "STREAMTEST OK" : "STREAMTEST FAILED");
                return opened ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Report(report, "streamtest failed: " + ex.Message);
                return 1;
            }
        }

        /// <summary>真实启动一次界面（显示窗口若干秒后自动退出），用于冒烟测试。</summary>
        /// <summary>上传自检：把本地文件传到云盘分享目录，并列出结果确认。</summary>
        /// <summary>用 API 令牌删除云盘上的文件（同时验证删除接口）。</summary>
        /// <summary>
        /// 把本地文件夹补齐到云端：云端没有的上传；同名且大小相同的跳过；
        /// 同名但大小不同的覆盖。用于「传一次，然后本地就可以删了」的场景。
        /// </summary>
        private static int UploadAll(string endpoint, string localFolder)
        {
            StringBuilder report = new StringBuilder();
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(localFolder))
            {
                Console.WriteLine("usage: Skylark.exe --uploadall <endpoint> <local-folder>");
                return 1;
            }

            List<string> files = new List<string>();
            foreach (string file in System.IO.Directory.GetFiles(localFolder, "*.*",
                System.IO.SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                // .apk 是给「更新专用仓库」用的（安卓应用内更新从这里取包）
                if (ext == ".lrc" || ext == ".apk" || Array.IndexOf(LibraryScanner.Extensions, ext) >= 0)
                    files.Add(file);
            }
            report.AppendLine("本地待处理文件: " + files.Count);

            Dictionary<string, long> cloud = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (CloudEntry entry in CloudClient.ListAllFiles(endpoint, 3))
            {
                if (!entry.IsDirectory) cloud[entry.Name] = entry.Size;
            }
            report.AppendLine("云端已有文件: " + cloud.Count);

            int uploaded = 0;
            int skipped = 0;
            int replaced = 0;
            int failed = 0;
            foreach (string file in files)
            {
                string name = System.IO.Path.GetFileName(file);
                long size = new System.IO.FileInfo(file).Length;
                bool exists = cloud.ContainsKey(name);
                if (exists && cloud[name] == size)
                {
                    skipped++;
                    continue;
                }
                try
                {
                    bool wasReplaced;
                    CloudClient.Upload(endpoint, file, "/", null, out wasReplaced);
                    uploaded++;
                    if (exists) replaced++;
                    report.AppendLine("  ↑ " + name + " (" + size + " 字节)" + (exists ? " [覆盖]" : ""));
                }
                catch (Exception ex)
                {
                    failed++;
                    report.AppendLine("  ✗ " + name + " 上传失败: " + ex.Message);
                }
            }

            report.AppendLine("上传 " + uploaded + " 个（其中覆盖 " + replaced + "），跳过 " + skipped
                + " 个，失败 " + failed + " 个");
            Console.WriteLine(report.ToString());
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    "skylark-lockcheck.log"), report.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception)
            {
            }
            return failed == 0 ? 0 : 1;
        }

        /// <summary>用 API 令牌删除云盘上的文件（同时验证删除接口）。</summary>
        private static int CloudDelete(string endpoint, string cloudPath)
        {
            StringBuilder report = new StringBuilder();
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(cloudPath))
            {
                Console.WriteLine("usage: Skylark.exe --clouddelete <token> <path-in-library>");
                return 1;
            }
            try
            {
                report.AppendLine("is api token = " + CloudClient.IsApiToken(endpoint));
                CloudRepoInfo info = CloudClient.GetRepoInfo(endpoint);
                report.AppendLine("repo = " + info.Name + " files=" + info.FileCount);

                string parent = "/";
                int slash = cloudPath.LastIndexOf('/');
                if (slash > 0) parent = cloudPath.Substring(0, slash);
                string name = cloudPath.Substring(slash + 1);
                List<string> names = new List<string>();
                names.Add(name);
                CloudClient.DeleteFiles(endpoint, parent, names);
                report.AppendLine("deleted: " + cloudPath);

                List<CloudEntry> entries = CloudClient.List(endpoint, string.Empty);
                bool stillThere = false;
                foreach (CloudEntry entry in entries)
                {
                    if (entry.Name == name) stillThere = true;
                }
                report.AppendLine("还在列表里 = " + stillThere + "（共 " + entries.Count + " 个文件）");
                report.AppendLine(stillThere ? "DELETETEST FAILED" : "DELETETEST OK");
                Console.WriteLine(report.ToString());
                try
                {
                    System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                        "skylark-lockcheck.log"), report.ToString(), System.Text.Encoding.UTF8);
                }
                catch (Exception)
                {
                }
                return stillThere ? 1 : 0;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Console.WriteLine("delete failed: " + ex.Message);
                return 1;
            }
        }

        /// <summary>把本地文件传到云盘指定目录（目标目录不存在时会尝试建出来）。</summary>
        private static int CloudUpload(string endpoint, string localFile, string cloudDir)
        {
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(localFile) || string.IsNullOrEmpty(cloudDir))
            {
                Console.WriteLine("usage: Skylark.exe --cloudupload <endpoint> <local-file> <cloud-dir>");
                return 1;
            }
            try
            {
                bool replaced;
                CloudClient.Upload(endpoint, localFile, cloudDir, null, out replaced);
                Console.WriteLine("uploaded " + System.IO.Path.GetFileName(localFile) + " -> " + cloudDir
                    + (replaced ? " [替换]" : ""));
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("上传失败: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// 批量移动：--cloudmove &lt;endpoint&gt; &lt;源目录&gt; &lt;目标目录&gt; &lt;名字1&gt; [名字2 ...]
        /// 用服务端接口搬，不重传数据（歌单就是靠这个把歌搬进搬出）。
        /// </summary>
        /// <summary>在云盘上建目录：--cloudmkdir &lt;endpoint&gt; &lt;cloud-dir&gt;</summary>
        private static int CloudMkdir(string endpoint, string dirPath)
        {
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(dirPath))
            {
                Console.WriteLine("usage: Skylark.exe --cloudmkdir <endpoint> <cloud-dir>");
                return 1;
            }
            try
            {
                CloudClient.EnsureDir(endpoint, dirPath);
                bool ok = CloudClient.DirExists(endpoint, dirPath);
                Console.WriteLine((ok ? "目录已就绪: " : "建目录似乎没成功: ") + dirPath);
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("建目录失败: " + ex.Message);
                return 1;
            }
        }

        private static int CloudMove(string[] args)
        {
            if (args.Length < 5)
            {
                Console.WriteLine("usage: Skylark.exe --cloudmove <endpoint> <src-dir> <dst-dir> <name1> [name2 ...]");
                return 1;
            }
            string endpoint = args[1];
            string srcDir = args[2];
            string dstDir = args[3];
            List<string> names = new List<string>();
            for (int i = 4; i < args.Length; i++)
            {
                if (!string.IsNullOrEmpty(args[i])) names.Add(args[i]);
            }
            try
            {
                CloudClient.MoveItems(endpoint, srcDir, names, dstDir);
                Console.WriteLine("移动 " + names.Count + " 项：" + srcDir + " -> " + dstDir);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("移动失败: " + ex.Message);
                return 1;
            }
        }

        private static int UploadTest(string url, string localFile)
        {
            StringBuilder report = new StringBuilder();
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(localFile))
            {
                Console.WriteLine("usage: Skylark.exe --uploadtest <share-url> <local-file>");
                return 1;
            }
            try
            {
                Report(report, "upload url = " + CloudClient.GetUploadUrl(url, "/"));
                long last = 0;
                bool replaced;
                CloudClient.Upload(url, localFile, "/", delegate(long done, long total) { last = done; }, out replaced);
                Report(report, "uploaded " + System.IO.Path.GetFileName(localFile)
                    + " (" + last + " bytes, replaced=" + replaced + ")");

                List<CloudEntry> entries = CloudClient.List(url, string.Empty);
                bool found = false;
                foreach (CloudEntry entry in entries)
                {
                    if (entry.Name == System.IO.Path.GetFileName(localFile)) found = true;
                }
                Report(report, "出现在文件列表 = " + found + "（共 " + entries.Count + " 个文件）");
                Report(report, found ? "UPLOADTEST OK" : "UPLOADTEST FAILED");
                return found ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Report(report, "uploadtest failed: " + ex.Message);
                return 1;
            }
        }

        private static int CloudTest(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Console.WriteLine("usage: Skylark.exe --cloudtest <share-url>");
                return 1;
            }

            StringBuilder report = new StringBuilder();
            int failures = 0;
            try
            {
                Report(report, "token = " + CloudClient.ParseToken(url));
                Report(report, "share = " + CloudClient.ShareUrl(url));

                ScanResult scan = CloudLibrary.Scan(url, null, null);
                Report(report, "songs = " + scan.Songs.Count);
                System.Collections.Generic.HashSet<string> distinctIds =
                    new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Song s in scan.Songs) distinctIds.Add(s.Title + "\u0001" + s.Artist);
                Report(report, "distinct songs = " + distinctIds.Count);
                Report(report, "playlists = " + scan.Playlists.Count
                    + (scan.Playlists.Count > 0 ? " [" + string.Join(", ", scan.Playlists.ToArray()) + "]" : ""));
                if (scan.Songs.Count == 0)
                {
                    Report(report, "CLOUDTEST FAILED: 没有扫描到歌曲");
                    return 1;
                }

                int withLyrics = 0;
                foreach (Song song in scan.Songs)
                {
                    if (song.HasLyrics) withLyrics++;
                }
                Report(report, "with lyrics = " + withLyrics + "/" + scan.Songs.Count);

                // 逐首拉 512KB 文件头，确认每个音频都能解析出时长（校验整库完整性）
                int parsed = 0;
                List<string> broken = new List<string>();
                foreach (Song song in scan.Songs)
                {
                    try
                    {
                        int probe = Math.Min(512 * 1024, song.Size > 0 ? (int)song.Size : 512 * 1024);
                        byte[] probeBytes = CloudClient.DownloadHead(url, song.CloudPath, probe);
                        double d = DurationReader.ReadBytes(probeBytes, song.Size,
                            System.IO.Path.GetExtension(song.FileName));
                        if (d > 10 && d < 36000) parsed++;
                        else broken.Add(song.FileName);
                    }
                    catch (Exception ex)
                    {
                        broken.Add(song.FileName + " (" + ex.Message + ")");
                    }
                }
                Report(report, "duration parsed = " + parsed + "/" + scan.Songs.Count
                    + (broken.Count == 0 ? "" : "，异常：" + string.Join("; ", broken.ToArray())));
                if (parsed != scan.Songs.Count) failures++;

                Song first = scan.Songs[0];
                Report(report, "first = " + first.Title + " - " + first.Artist + " (" + first.FileName + ", "
                    + (first.Size / 1024 / 1024.0).ToString("0.0") + " MB)");

                byte[] head = CloudClient.DownloadHead(url, first.CloudPath, 512 * 1024);
                double duration = DurationReader.ReadBytes(head, first.Size,
                    System.IO.Path.GetExtension(first.FileName));
                Report(report, "head bytes = " + head.Length + ", duration = " + duration.ToString("0.0") + "s");
                if (duration < 10 || duration > 3600) failures++;

                string target = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skylark-cloud-test.mp3");
                long lastDone = 0;
                CloudClient.DownloadTo(url, first.CloudPath, target, delegate(long done, long total)
                {
                    lastDone = done;
                });
                long length = new System.IO.FileInfo(target).Length;
                Report(report, "downloaded = " + (length / 1024 / 1024.0).ToString("0.0") + " MB, size match = "
                    + (length == first.Size) + " (progress last = " + lastDone + ")");
                if (length != first.Size) failures++;

                double localDuration = DurationReader.Read(target);
                Report(report, "local parse = " + localDuration.ToString("0.0") + "s, diff = "
                    + Math.Abs(localDuration - duration).ToString("0.0") + "s");
                if (Math.Abs(localDuration - duration) > 1.5) failures++;
                System.IO.File.Delete(target);

                Song lyricSong = null;
                foreach (Song song in scan.Songs)
                {
                    if (song.HasLyrics) { lyricSong = song; break; }
                }
                if (lyricSong != null)
                {
                    string lrcPath = lyricSong.LyricPath.Substring(
                        (CloudLibrary.PseudoScheme + CloudClient.ParseToken(url)).Length);
                    string text = CloudClient.GetText(url, lrcPath);
                    LyricDocument doc = LrcParser.Parse(text);
                    Report(report, "lyric lines = " + doc.Lines.Count + ", synced = " + doc.Synced
                        + ", first = " + (doc.Lines.Count > 0 ? doc.Lines[0].Text : ""));
                    if (doc.Lines.Count == 0) failures++;
                }

                Report(report, failures == 0 ? "CLOUDTEST OK" : "CLOUDTEST FAILED: " + failures);
                return failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Report(report, "cloudtest failed: " + ex.Message);
                return 1;
            }
        }

        /// <summary>真实启动一次界面（显示窗口若干秒后自动退出），用于冒烟测试。</summary>
        private static int SmokeRun(string[] args)
        {
            try
            {
                StringBuilder trace = new StringBuilder();
                Trace(trace, "start");
                bool play = args.Length > 1 && args[1] == "play";
                MainWindow.Headless = true;
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    LogCrash(e.Exception);
                    Trace(trace, "dispatcher exception: " + e.Exception.Message);
                };
                Theme.EnsureStyles();
                Trace(trace, "styles");
                MainWindow window = new MainWindow();
                // 冒烟测试里强制关闭“关闭到托盘”，否则窗口关闭后进程会留在托盘
                window.Settings.CloseToTray = false;
                window.Settings.MinimizeToTray = false;
                app.MainWindow = window;
                window.Show();
                Trace(trace, "shown");

                if (play)
                {
                    System.Windows.Threading.DispatcherTimer starter = new System.Windows.Threading.DispatcherTimer();
                    starter.Interval = TimeSpan.FromSeconds(4);
                    starter.Tick += delegate
                    {
                        starter.Stop();
                        string keyword = args.Length > 2 ? args[2] : null;
                        Trace(trace, "play -> " + window.SmokePlayFirst(keyword));
                    };
                    starter.Start();
                }

                int seconds = play ? 26 : 5;
                System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(seconds);
                timer.Tick += delegate
                {
                    timer.Stop();
                    Trace(trace, "state -> " + window.SmokeState());
                    Trace(trace, "ui -> " + window.SmokeStatusText());
                    Trace(trace, "tick -> close");
                    window.Close();
                };
                timer.Start();
                Trace(trace, "timer started");
                app.Run();
                Trace(trace, "app.Run returned");
                Console.WriteLine("smoke ok");
                return 0;
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                Console.WriteLine("smoke failed: " + ex.Message);
                return 1;
            }
        }

        private static void Trace(StringBuilder trace, string step)
        {
            try
            {
                trace.AppendLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + step);
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skylark-smoke.log");
                using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Append,
                    System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + step);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void FocusRunningInstance()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                Process[] all = Process.GetProcessesByName(current.ProcessName);
                foreach (Process process in all)
                {
                    if (process.Id == current.Id) continue;
                    IntPtr handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;
                    ShowWindow(handle, SW_RESTORE);
                    SetForegroundWindow(handle);
                    break;
                }
            }
            catch (Exception)
            {
            }
        }

        private const int SW_RESTORE = 9;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        private const int ATTACH_PARENT_PROCESS = -1;

        /// <summary>让窗口程序也能把自检输出打印到调用它的控制台。</summary>
        private static void AttachConsole()
        {
            try
            {
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    System.IO.StreamWriter writer = new System.IO.StreamWriter(Console.OpenStandardOutput());
                    writer.AutoFlush = true;
                    Console.SetOut(writer);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}

