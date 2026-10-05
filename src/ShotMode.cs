using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Skylark
{
    /// <summary>离屏渲染主界面为 PNG，用于开发期的视觉校对。</summary>
    public static class ShotMode
    {
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

        public static int Run(string[] args)
        {
            string output = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "skylark.png");
            string view = args.Length > 2 ? args[2] : "library";
            string theme = args.Length > 3 ? args[3] : "dark";
            double width = args.Length > 4 ? double.Parse(args[4]) : 1180;
            double height = args.Length > 5 ? double.Parse(args[5]) : 740;

            MainWindow.Headless = true;
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Theme.Current = theme;

            string lyricPath = Path.Combine(Path.GetTempPath(), "skylark-demo.lrc");
            System.Text.StringBuilder lrc = new System.Text.StringBuilder();
            lrc.AppendLine("[ti:晴天]");
            lrc.AppendLine("[ar:周杰伦]");
            lrc.AppendLine("[00:00.00]晴天 - 周杰伦");
            string[] demoLines = new string[]
            {
                "故事的小黄花", "从出生那年就飘着", "童年的荡秋千", "随记忆一直晃到现在",
                "Re So So Si Do Si La", "吹着前奏望着天空", "我想起花瓣试着掉落",
                "为你翘课的那一天", "花落的那一天", "教室的那一间", "我怎么看不见",
                "消失的下雨天", "我好想再淋一遍", "没想到失去的勇气我还留着",
                "好想再问一遍", "你会等待还是离开", "刮风这天我试过握着你手",
                "但偏偏雨渐渐大到我看你不见", "还要多久我才能在你身边",
                "等到放晴的那天也许我会比较好一点", "从前从前有个人爱你很久",
                "但偏偏风渐渐把距离吹得好远", "好不容易又能再多爱一天",
                "但故事的最后你好像还是说了拜拜"
            };
            for (int i = 0; i < demoLines.Length; i++)
            {
                lrc.AppendLine("[00:" + (12 + i * 6).ToString("00") + ".00]" + demoLines[i]);
                lrc.AppendLine("[00:" + (15 + i * 6).ToString("00") + ".00]" + demoLines[i] + "（副歌）");
            }
            // 加上一句「同时间戳的译文」，用来验证外语歌的排版（放在靠后的位置，
            // 这样截图时前面几秒显示的是「下一句预览」的排版）
            lrc.AppendLine("[00:33.00]The little yellow flower (chorus)");
            File.WriteAllText(lyricPath, lrc.ToString(), new System.Text.UTF8Encoding(true));

            MainWindow window = new MainWindow();
            Theme.Apply(theme);
            window.NotifySettingsChanged();
            window.LoadDemoForShot(view, lyricPath);
            Pump(0.25);

            FrameworkElement root;
            if (view == "desktop")
            {
                window.Settings.LyricLocked = false;
                DesktopLyricsWindow lyric = new DesktopLyricsWindow(window);
                lyric.ApplySettings();
                lyric.UpdateNow();
                lyric.SimulateHoverForShot();
                root = lyric.Content as FrameworkElement;
                width = 980;
                height = 200;
            }
            else if (view == "desktop-locked")
            {
                window.Settings.LyricLocked = true;
                DesktopLyricsWindow lyric = new DesktopLyricsWindow(window);
                lyric.ApplySettings();
                lyric.UpdateNow();
                lyric.SimulateHoverForShot();
                root = lyric.Content as FrameworkElement;
                width = 980;
                height = 200;
            }
            else if (view == "unlock")
            {
                // 单独看一眼锁定状态下的「解锁」小按钮（它是独立窗口，不会出现在歌词截图里）
                LyricsUnlockWindow unlock = new LyricsUnlockWindow(window);
                FrameworkElement content = unlock.Content as FrameworkElement;
                content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                root = content;
                width = (int)Math.Ceiling(content.DesiredSize.Width) + 1;
                height = (int)Math.Ceiling(content.DesiredSize.Height) + 1;
                Console.WriteLine("unlock button size: " + width + " x " + height
                    + " (含 4 倍放大预览见 unlock-btn-4x.png)");
            }
            else if (view == "library-batch")
            {
                root = window.Content as FrameworkElement;
                window.LibraryViewRef.BatchSelectForTest(3);
            }
            else if (view == "batch-all")
            {
                root = window.Content as FrameworkElement;
                window.LibraryViewRef.BatchSelectForTest(999);   // 全选
            }
            else if (view == "batch-exit")
            {
                // 走一遍「勾了几首 → 点完成」的路径，确认不会抛异常
                root = window.Content as FrameworkElement;
                window.LibraryViewRef.BatchSelectForTest(3);
                window.LibraryViewRef.ExitBatch();
                Console.WriteLine("batch-exit: 退出批量编辑没有抛异常");
            }
            else if (view == "music-search")
            {
                root = window.Content as FrameworkElement;
                window.Settings.CloudToken = "";
                window.Settings.CloudUrl = "https://cloud.tsinghua.edu.cn/d/demo/";
                window.ShowMusicSearch();
            }
            else if (view == "download")
            {
                root = window.Content as FrameworkElement;
                window.DownloadSongs(window.VisibleSongs.GetRange(0, 3));
            }
            else if (view == "playlist")
            {
                root = window.Content as FrameworkElement;
                // 造几个歌单名出来，好看看选歌单的卡片长什么样
                List<Song> demo = window.VisibleSongs;
                string[] names = { "默认歌单", "通勤路上", "睡前循环" };
                for (int i = 0; i < demo.Count; i++) demo[i].Playlist = names[i % names.Length];
                window.AddSelectionToPlaylist(demo.GetRange(0, 2));
            }
            else if (view == "prompt")
            {
                root = window.Content as FrameworkElement;
                window.NewPlaylist();
            }
            else
            {
                root = window.Content as FrameworkElement;
            }
            if (root == null)
            {
                Console.WriteLine("no content");
                return 1;
            }
            root.Width = width;
            root.Height = height;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            Pump(0.25);
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            MainWindow.TraceStep("shot " + view + " activeLyric=" + window.Lyrics.CurrentIndex
                + " lines=" + window.Lyrics.Lines.Count + " | " + window.Lyrics.DebugState());

            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            if (view == "desktop" || view == "desktop-locked")
            {
                // 桌面歌词是半透明浮窗，先铺一层桌面背景方便观察效果
                DrawingVisual background = new DrawingVisual();
                using (DrawingContext dc = background.RenderOpen())
                {
                    LinearGradientBrush brush = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#1B3A5C"),
                        (Color)ColorConverter.ConvertFromString("#3E2A4F"),
                        new Point(0, 0), new Point(1, 1));
                    dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
                }
                bitmap.Render(background);
            }
            bitmap.Render(root);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = new FileStream(output, FileMode.Create))
            {
                encoder.Save(stream);
            }
            Console.WriteLine("shot: " + output);
            return 0;
        }
    }
}
