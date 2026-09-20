using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace Skylark
{
    /// <summary>
    /// 桌面歌词浮窗。
    /// 状态：
    ///   锁定     —— 完全透明（无背景、无阴影），鼠标穿透，仅鼠标移到窗口上时浮出小工具栏；
    ///   未锁定   —— 鼠标不在上面时透明，移到上面时出现浅色底与阴影，可拖动，并显示工具栏。
    /// </summary>
    public class DesktopLyricsWindow : Window
    {
        private readonly MainWindow main;
        private readonly Border frame = new Border();
        private readonly TextBlock currentText = new TextBlock();
        private readonly TextBlock translationText = new TextBlock();
        private readonly Border toolbar = new Border();
        private readonly TextBlock hintText = new TextBlock();
        private readonly Button lockButton = new Button();
        private readonly StackPanel panel = new StackPanel();

        private bool locked;
        private bool clickThrough;
        private bool hovering;
        private bool overToolbar;
        private bool toolbarShown;
        private bool dragging;
        private bool unlockVisible;
        private DateTime hoverStarted = DateTime.MinValue;
        private DispatcherTimer pollTimer;
        private DispatcherTimer hintTimer;
        private DispatcherTimer unlockHideTimer;
        private DispatcherTimer unlockSafetyTimer;
        private LyricsUnlockWindow unlockButton;
        /** 自愈体检：防止 Win+D / 换显示器 / 别的程序抢置顶之后，歌词窗再也回不来。 */
        private DispatcherTimer healthTimer;
        private bool closing;
        private DateTime minimizedAt = DateTime.MinValue;
        /** 诊断用：一共自愈了多少次。 */
        public int HealthFixes;

        public DesktopLyricsWindow(MainWindow owner)
        {
            main = owner;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.Height;
            MinHeight = 96;
            FontFamily = Ui.Font;

            // 宽度只在这里决定一次，之后不再变化，避免开关/锁定时画面跳变
            Rect area = SystemParameters.WorkArea;
            Width = Math.Min(1080, Math.Max(620, area.Width * 0.68));

            frame.CornerRadius = new CornerRadius(16);
            frame.Padding = new Thickness(30, 14, 30, 16);
            frame.Margin = new Thickness(26);
            frame.Background = Brushes.Transparent;

            // 当前句用 Bold、第二行用 SemiBold：两句一样亮，靠字重和位置区分
            Configure(currentText, 34, FontWeights.SemiBold);
            Configure(translationText, 18, FontWeights.SemiBold);
            translationText.Margin = new Thickness(0, 2, 0, 0);

            panel.Children.Add(currentText);
            panel.Children.Add(translationText);

            // 顶部固定高度的工具栏行 + 居中歌词 + 底部等高空行，
            // 这样工具栏淡入淡出不会改变窗口大小，歌词也始终垂直居中。
            panel.Margin = new Thickness(0, 0, 0, 0);
            Grid content = new Grid();
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions[0].Height = Ui.Px(ToolbarHeight);
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            content.RowDefinitions.Add(new RowDefinition());
            content.RowDefinitions[2].Height = Ui.Px(ToolbarHeight);

            Border toolbarRow = new Border();
            toolbarRow.Child = BuildToolbar();
            content.Children.Add(toolbarRow);
            Grid.SetRow(panel, 1);
            content.Children.Add(panel);
            frame.Child = content;
            Content = frame;

            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (locked) return;
                if (overToolbar) return;
                if (e.ButtonState != MouseButtonState.Pressed) return;
                dragging = true;
                try
                {
                    DragMove();
                }
                catch (Exception)
                {
                }
                finally
                {
                    dragging = false;
                    overToolbar = false;
                    ApplyVisualState();
                }
            };
            MouseRightButtonUp += delegate { if (!locked) ShowMenu(); };

            SourceInitialized += delegate { ApplyClickThrough(); };
            Loaded += delegate
            {
                PlaceWindow();
                ApplySettings();
                UpdateNow();
                StartPolling();
                StartHealthCheck();
            };
            Closed += delegate
            {
                closing = true;
                if (healthTimer != null) healthTimer.Stop();
                if (pollTimer != null) pollTimer.Stop();
                if (hintTimer != null) hintTimer.Stop();
                if (unlockSafetyTimer != null) unlockSafetyTimer.Stop();
                DisposeUnlockButton();
            };
        }

        private static void Configure(TextBlock block, double size, FontWeight weight)
        {
            block.FontSize = size;
            block.FontWeight = weight;
            block.TextAlignment = TextAlignment.Center;
            block.TextWrapping = TextWrapping.Wrap;
            block.Foreground = Brushes.White;
            // 描边（阴影）强度在 ApplySettings → ApplyTextShadow 里按设置决定
        }

        #region 工具栏

        private const double ToolbarHeight = 30;

        private UIElement BuildToolbar()
        {
            lockButton.Style = (Style)Application.Current.Resources["TinyIconButton"];
            lockButton.Content = Icons.Create("lock", 15, "TextDim");
            lockButton.ToolTip = "锁定 / 解锁（锁定后鼠标穿透）Ctrl+Alt+L";
            lockButton.Click += delegate { main.ToggleLyricLock(); };

            Button smaller = ToolButton("minimize", "减小字号", delegate { ChangeFont(-3); });
            Button bigger = ToolButton("plus", "增大字号", delegate { ChangeFont(3); });
            Button home = ToolButton("monitor", "回到主界面", delegate { main.ShowFromTrayPublic(); });
            Button close = ToolButton("close", "关闭桌面歌词", delegate { main.ShowDesktopLyrics(false); });

            hintText.FontSize = 11.5;
            hintText.VerticalAlignment = VerticalAlignment.Center;
            hintText.Margin = new Thickness(0, 0, 10, 0);
            hintText.Visibility = Visibility.Collapsed;
            hintText.SetResourceReference(TextBlock.ForegroundProperty, "TextDim");

            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(hintText);
            row.Children.Add(lockButton);
            row.Children.Add(smaller);
            row.Children.Add(bigger);
            row.Children.Add(home);
            row.Children.Add(close);

            toolbar.Background = new SolidColorBrush(Color.FromArgb(200, 18, 21, 28));
            toolbar.CornerRadius = new CornerRadius(10);
            toolbar.Padding = new Thickness(8, 2, 8, 2);
            toolbar.Height = ToolbarHeight;
            toolbar.HorizontalAlignment = HorizontalAlignment.Right;
            toolbar.VerticalAlignment = VerticalAlignment.Center;
            toolbar.Margin = new Thickness(0, 0, 44, 0);
            toolbar.Child = row;
            toolbar.Opacity = 0;
            toolbar.IsHitTestVisible = false;
            return toolbar;
        }

        private static Button ToolButton(string icon, string tooltip, RoutedEventHandler click)
        {
            Button button = new Button();
            button.Style = (Style)Application.Current.Resources["TinyIconButton"];
            button.Content = Icons.Create(icon, 15, "TextDim");
            button.ToolTip = tooltip;
            button.Click += click;
            return button;
        }

        #endregion

        /// <summary>应用设置中的字号、颜色、透明度与锁定状态。</summary>
        public void ApplySettings()
        {
            AppSettings s = main.Settings;
            currentText.FontSize = s.LyricFontSize;
            // 译文/下一句的字号：这里也要按当前设置算好，
            // 否则解锁（会重新 ApplySettings）时第二行会先缩一下、等下次刷新才恢复
            ApplySecondaryFontSize();

            Color color;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(s.LyricColor);
            }
            catch (Exception)
            {
                color = Colors.White;
            }
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            currentText.Foreground = brush;
            translationText.Foreground = brush;
            // 只让歌词文字半透明，工具栏始终清晰可见
            panel.Opacity = Math.Max(0.2, Math.Min(1, s.LyricOpacity));
            ApplyTextShadow();

            locked = s.LyricLocked;
            UpdateLockVisual();
            ApplyVisualState();
            if (locked) ShowUnlockButton();
        }

        /// <summary>
        /// 第二行（译文 / 下一句）：**只比当前句小**（差得足够看出来，但不是小到看不清），
        /// 字重、颜色、亮度都和当前句一致。
        /// 不用压暗、也不用额外标记——一眼就能看出上面那行是正在唱的那句。
        /// </summary>
        private void ApplySecondaryFontSize()
        {
            translationText.FontSize = Math.Max(16, currentText.FontSize * 0.74);
            translationText.FontWeight = currentText.FontWeight;
            translationText.Opacity = 1.0;
        }

        /// <summary>文字描边（阴影）：0 关 / 1 弱 / 2 强。锁定时没有底色，全靠它保证看得清。</summary>
        private void ApplyTextShadow()
        {
            int mode = main.Settings.LyricShadowMode;
            if (mode <= 0)
            {
                currentText.Effect = null;
                translationText.Effect = null;
                return;
            }
            double blur = mode == 2 ? 4.5 : 2.5;
            double opacity = mode == 2 ? 0.95 : 0.6;
            currentText.Effect = CreateShadow(blur, opacity);
            translationText.Effect = CreateShadow(blur, opacity);
        }

        private static DropShadowEffect CreateShadow(double blur, double opacity)
        {
            DropShadowEffect effect = new DropShadowEffect();
            effect.BlurRadius = blur;
            effect.ShadowDepth = 0;
            effect.Opacity = opacity;
            effect.Color = Colors.Black;
            effect.Freeze();
            return effect;
        }

        private void UpdateLockVisual()
        {
            lockButton.Content = Icons.Create(locked ? "lock" : "unlock", 15, "TextDim");
            lockButton.ToolTip = locked
                ? "已锁定：鼠标穿透，点此解锁（Ctrl+Alt+L）"
                : "锁定：锁定后鼠标穿透，完全不影响操作电脑";
        }

        /// <summary>刷新歌词显示。</summary>
        public void UpdateNow()
        {
            Song song = main.CurrentSong;
            if (song == null)
            {
                SetCurrentText("未在播放");
                translationText.Text = " ";
                translationText.Visibility = Visibility.Visible;
                return;
            }

            LyricsView view = main.Lyrics;
            List<LyricLine> lines = view.Lines;
            int index = view.CurrentIndex;

            if (lines == null || lines.Count == 0)
            {
                SetCurrentText(song.Title);
                translationText.Text = " ";
                translationText.Visibility = Visibility.Visible;
                return;
            }

            if (index < 0)
            {
                SetCurrentText(song.Title);
                translationText.Text = " ";
                translationText.Visibility = Visibility.Visible;
                return;
            }

            SetCurrentText(lines[index].Text);
            if (!string.IsNullOrEmpty(lines[index].Translation) && main.Settings.LyricShowTranslation)
            {
                // 有译文：只显示这一句，译文比原文小一点
                translationText.Text = lines[index].Translation;
                ApplySecondaryFontSize();
                translationText.Visibility = Visibility.Visible;
            }
            else
            {
                // 没有译文：显示下一句作为预览（比当前句小一点，其它完全一致）
                if (index + 1 >= lines.Count)
                {
                    translationText.Visibility = Visibility.Collapsed;
                    return;
                }
                translationText.Text = lines[index + 1].Text;
                ApplySecondaryFontSize();
                translationText.Visibility = Visibility.Visible;
            }
        }

        private void SetCurrentText(string text)
        {
            if (currentText.Text == text) return;
            currentText.Text = text;
        }

        /// <summary>短暂提示（例如「已锁定」）。</summary>
        public void FlashHint(string message)
        {
            hintText.Text = message;
            hintText.Visibility = Visibility.Visible;
            if (hintTimer == null)
            {
                hintTimer = new DispatcherTimer();
                hintTimer.Interval = TimeSpan.FromMilliseconds(2800);
                hintTimer.Tick += delegate
                {
                    hintTimer.Stop();
                    hintText.Visibility = Visibility.Collapsed;
                    ApplyVisualState();
                };
            }
            hintTimer.Stop();
            hintTimer.Start();
            ApplyVisualState();
        }

        /// <summary>仅用于离屏截图：模拟鼠标悬停状态。</summary>
        public void SimulateHoverForShot()
        {
            hovering = true;
            overToolbar = false;
            toolbarShown = true;
            hoverStarted = DateTime.Now;
            ApplyVisualState();
            // 截图模式：锁定状态下把「解锁」小按钮也画出来，方便看效果
            if (locked) ShowUnlockButton();
        }

        /// <summary>锁定状态下浮出的「解锁」按钮（供自检使用）。</summary>
        public LyricsUnlockWindow UnlockButton
        {
            get { return unlockButton; }
        }

        /// <summary>诊断用：轮询次数 / 自动隐藏次数 / 最后一次算出的解锁按钮矩形。</summary>
        public int PollTicks;
        public int UnlockHideCalls;
        public string LastUnlockRect = "(未算过)";

        private void ChangeFont(double delta)
        {
            double size = main.Settings.LyricFontSize + delta;
            if (size < 18) size = 18;
            if (size > 72) size = 72;
            main.Settings.LyricFontSize = size;
            main.SaveSettings();
            ApplySettings();
            main.NotifySettingsChanged();
        }

        #region 状态与鼠标穿透

        private void StartHealthCheck()
        {
            if (healthTimer != null) return;
            healthTimer = new DispatcherTimer();
            healthTimer.Interval = TimeSpan.FromMilliseconds(1500);
            healthTimer.Tick += delegate { HealthCheck(); };
            healthTimer.Start();
        }

        /// <summary>
        /// 体检 + 自愈。
        /// 这个浮窗是「无边框 + 置顶 + 不在任务栏 + 工具窗口」，这几种情况下系统会把它弄丢：
        ///   Win+D（显示桌面）会把它最小化，而任务栏没有它的按钮，用户没法还原；
        ///   某些全屏程序 / 桌面整理软件会顶掉它的置顶样式，于是被盖住；
        ///   拔掉外接显示器或改分辨率后，它可能整个留在屏幕外面。
        /// 表现都是「用着用着歌词自己没了」。这里定期检查并拉回来。
        /// </summary>
        private void HealthCheck()
        {
            if (closing || !IsLoaded) return;
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;

                // 1) 被最小化：等一会儿再恢复，免得按 Win+D 想看桌面时立刻弹回来
                if (WindowState == WindowState.Minimized)
                {
                    if (minimizedAt == DateTime.MinValue) minimizedAt = DateTime.Now;
                    else if ((DateTime.Now - minimizedAt).TotalSeconds >= 1.5)
                    {
                        minimizedAt = DateTime.MinValue;
                        WindowState = WindowState.Normal;
                        ShowWindow(handle, SW_SHOWNOACTIVATE);
                        ForceTopmost(handle);
                        HealthFixes++;
                        MainWindow.TraceStep("desktop lyrics: 从最小化状态恢复（Win+D 之类）");
                    }
                    return;
                }
                minimizedAt = DateTime.MinValue;

                // 2) 被藏起来（不是关闭，是 Visibility 被改）
                if (!IsVisible)
                {
                    Show();
                    ShowWindow(handle, SW_SHOWNOACTIVATE);
                    ForceTopmost(handle);
                    HealthFixes++;
                    MainWindow.TraceStep("desktop lyrics: 窗口不可见，已重新显示");
                    return;
                }

                // 3) 置顶样式丢了：会被别的窗口盖住
                int ex = GetWindowLong(handle, GWL_EXSTYLE);
                if ((ex & WS_EX_TOPMOST) == 0)
                {
                    ForceTopmost(handle);
                    HealthFixes++;
                    MainWindow.TraceStep("desktop lyrics: 置顶丢失，已重新置顶");
                }

                // 4) 整个窗口在屏幕外（拔显示器 / 改分辨率）
                if (IsOffScreen(handle))
                {
                    PullBackOnScreen();
                    ForceTopmost(handle);
                    HealthFixes++;
                    MainWindow.TraceStep("desktop lyrics: 窗口跑到屏幕外，已拉回主屏");
                }
            }
            catch (Exception)
            {
                // 体检失败不影响播放，下一次再试
            }
        }

        private void ForceTopmost(IntPtr handle)
        {
            if (!Topmost) Topmost = true;
            SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private bool IsOffScreen(IntPtr handle)
        {
            RECT rect;
            if (!GetWindowRect(handle, out rect)) return false;
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                System.Drawing.Rectangle bounds = screen.Bounds;
                bool overlaps = rect.Left < bounds.Right && rect.Right > bounds.Left
                             && rect.Top < bounds.Bottom && rect.Bottom > bounds.Top;
                if (overlaps) return false;
            }
            return true;
        }

        private void PullBackOnScreen()
        {
            Rect area = SystemParameters.WorkArea;
            Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
            Top = Math.Max(area.Top + 40, area.Bottom - Height - 80);
        }

        private void StartPolling()
        {
            if (pollTimer != null) return;
            pollTimer = new DispatcherTimer();
            pollTimer.Interval = TimeSpan.FromMilliseconds(150);
            pollTimer.Tick += delegate { PollCursor(); };
            pollTimer.Start();
        }

        /// <summary>锁定状态下窗口收不到鼠标事件，因此轮询光标位置来判断悬停。</summary>
        private void PollCursor()
        {
            PollTicks++;
            if (!IsVisible) { LastUnlockRect = "轮询：窗口不可见"; return; }
            if (dragging) { LastUnlockRect = "轮询：正在拖动"; return; }
            POINT cursor;
            if (!GetCursorPos(out cursor)) { LastUnlockRect = "轮询：取光标失败"; return; }

            RECT rect;
            if (!GetWindowRect(new WindowInteropHelper(this).Handle, out rect))
            {
                LastUnlockRect = "轮询：取窗口矩形失败";
                return;
            }
            rect.Left += 26;
            rect.Top += 26;
            rect.Right -= 26;
            rect.Bottom -= 26;

            bool inside = cursor.X >= rect.Left && cursor.X <= rect.Right
                       && cursor.Y >= rect.Top && cursor.Y <= rect.Bottom;

            if (locked)
            {
                // 只有鼠标靠近「解锁按钮」那一小块地方才浮出来（不是放在歌词上就出现），
                // 离开一会儿再隐藏
                RECT button = UnlockButtonScreenRect();
                int margin = 34;
                bool nearButton = cursor.X >= button.Left - margin && cursor.X <= button.Right + margin
                               && cursor.Y >= button.Top - margin && cursor.Y <= button.Bottom + margin;
                LastUnlockRect = "光标 " + cursor.X + "," + cursor.Y + " / 按钮 " + button.Left + "," + button.Top
                    + ".." + button.Right + "," + button.Bottom + " / 靠近=" + nearButton;
                if (nearButton) ShowUnlockButton();
                else ScheduleHideUnlockButton();
            }

            bool onToolbar = false;
            if (inside)
            {
                double scaleX = 1;
                double scaleY = 1;
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }
                try
                {
                    Point origin = toolbar.PointToScreen(new Point(0, 0));
                    double width = Math.Max(60, toolbar.ActualWidth) * scaleX;
                    double height = Math.Max(24, toolbar.ActualHeight) * scaleY;
                    // 四周留一点余量，鼠标稍微偏一点也能点到
                    onToolbar = cursor.X >= origin.X - 6 && cursor.X <= origin.X + width + 6
                             && cursor.Y >= origin.Y - 6 && cursor.Y <= origin.Y + height + 6;
                }
                catch (Exception)
                {
                }
            }

            bool wasInside = hovering;
            hovering = inside;
            overToolbar = onToolbar;
            if (inside && !wasInside) hoverStarted = DateTime.Now;

            bool hintVisible = hintText.Visibility == Visibility.Visible;
            bool wantToolbar = inside && (hintVisible || (DateTime.Now - hoverStarted).TotalMilliseconds > 260);
            if (inside != wasInside || onToolbar != overToolbar || wantToolbar != toolbarShown)
            {
                toolbarShown = wantToolbar;
                ApplyVisualState();
            }
        }

        /// <summary>按锁定 / 悬停状态刷新外观与鼠标穿透。</summary>
        private void ApplyVisualState()
        {
            bool interactive = !locked || overToolbar;
            SetClickThrough(!interactive);

            // 锁定状态只保留一个「解锁」按钮（独立窗口，始终可点），其余按钮全部隐藏
            bool showToolbar = toolbarShown && !locked;
            toolbar.Opacity = showToolbar ? 1 : 0;
            toolbar.IsHitTestVisible = interactive && showToolbar;
            if (locked) PollCursor();   // 由「鼠标是否靠近」决定解锁按钮显示
            else
            {
                CancelUnlockHide();
                HideUnlockButtonSilently();
            }

            bool showBackground = !locked && hovering;
            if (showBackground)
            {
                frame.Background = new SolidColorBrush(Color.FromArgb(150, 12, 14, 18));
                frame.Effect = new DropShadowEffect
                {
                    BlurRadius = 26,
                    ShadowDepth = 4,
                    Opacity = 0.5,
                    Color = Colors.Black
                };
            }
            else
            {
                frame.Background = Brushes.Transparent;
                frame.Effect = null;
            }
            Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        }

        /// <summary>
        /// 解锁按钮在屏幕上的矩形（物理像素），算法与 LyricsUnlockWindow.PlaceNear 保持一致。
        /// 按钮还没创建时也能算出来 —— 「鼠标靠近才显示」才有判断依据。
        /// </summary>
        private RECT UnlockButtonScreenRect()
        {
            RECT rect = new RECT();
            try
            {
                double width = unlockButton != null && unlockButton.ActualWidth > 1 ? unlockButton.ActualWidth : 23;
                double height = unlockButton != null && unlockButton.ActualHeight > 1 ? unlockButton.ActualHeight : 21;
                double left = Left + ActualWidth - width - 34;
                double top = Top + 6;

                Rect area = SystemParameters.WorkArea;
                if (left < area.Left) left = area.Left + 8;
                if (left + width > area.Right) left = area.Right - width - 8;
                if (top < area.Top) top = area.Top + 8;
                if (top + height > area.Bottom) top = area.Bottom - height - 8;

                double scaleX = 1, scaleY = 1;
                PresentationSource source = PresentationSource.FromVisual(this);
                if (source != null && source.CompositionTarget != null)
                {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }
                Point screen = PointToScreen(new Point(left - Left, top - Top));
                rect.Left = (int)Math.Round(screen.X);
                rect.Top = (int)Math.Round(screen.Y);
                rect.Right = (int)Math.Round(screen.X + width * scaleX);
                rect.Bottom = (int)Math.Round(screen.Y + height * scaleY);
            }
            catch (Exception)
            {
                // 算不出来就丢到屏幕外，等价于「不显示」
                rect.Left = -20000;
                rect.Top = -20000;
                rect.Right = -19000;
                rect.Bottom = -19000;
            }
            return rect;
        }

        private void ShowUnlockButton()
        {
            if (!locked) return;
            if (unlockButton == null)
            {
                unlockButton = new LyricsUnlockWindow(main);
                // 鼠标真的移到小按钮上时，取消「兜底自动隐藏」，避免正在点的时候消失
                unlockButton.OnHover = delegate { CancelUnlockHide(); ArmUnlockSafetyHide(); };
                unlockButton.Show();
                unlockVisible = true;
            }
            unlockButton.PlaceNear(this);
            if (!unlockVisible)
            {
                unlockButton.Show();
                unlockVisible = true;
            }
            CancelUnlockHide();
            ArmUnlockSafetyHide();
        }

        /// <summary>
        /// 兜底：万一读不到鼠标位置（实测在受限环境里 GetCursorPos 会直接失败），
        /// 也不能让这个小按钮永远挂在屏幕上。显示后一段时间内没人再刷新就自己收起来。
        /// </summary>
        private void ArmUnlockSafetyHide()
        {
            if (unlockSafetyTimer == null)
            {
                unlockSafetyTimer = new DispatcherTimer();
                unlockSafetyTimer.Interval = TimeSpan.FromSeconds(4.5);
                unlockSafetyTimer.Tick += delegate
                {
                    unlockSafetyTimer.Stop();
                    if (locked) HideUnlockButtonSilently();
                };
            }
            // 每次「显示」都重新计时：只要轮询还在不停地把按钮刷出来（说明鼠标就在附近），
            // 它就永远不会超时；一旦没人刷新了，最多 4.5 秒就自己收起来。
            unlockSafetyTimer.Stop();
            unlockSafetyTimer.Start();
        }

        /// <summary>只是隐藏（保留窗口实例，避免反复创建）。</summary>
        private void HideUnlockButtonSilently()
        {
            UnlockHideCalls++;
            if (unlockButton == null) return;
            unlockButton.Hide();
            unlockVisible = false;
        }

        private void DisposeUnlockButton()
        {
            CancelUnlockHide();
            if (unlockButton == null) return;
            unlockButton.Close();
            unlockButton = null;
            unlockVisible = false;
        }

        /// <summary>鼠标离开后延迟一会儿再隐藏，避免一闪一闪。</summary>
        private void ScheduleHideUnlockButton()
        {
            if (unlockButton == null || !unlockVisible) return;
            if (unlockHideTimer == null)
            {
                unlockHideTimer = new DispatcherTimer();
                unlockHideTimer.Interval = TimeSpan.FromMilliseconds(900);
                unlockHideTimer.Tick += delegate
                {
                    unlockHideTimer.Stop();
                    if (locked) HideUnlockButtonSilently();
                };
            }
            // 注意：不要每次都重启，否则每 150ms 的轮询会让它永远到不了触发时间
            if (!unlockHideTimer.IsEnabled) unlockHideTimer.Start();
        }

        private void CancelUnlockHide()
        {
            if (unlockHideTimer != null) unlockHideTimer.Stop();
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int SW_SHOWNOACTIVATE = 4;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private void SetClickThrough(bool through)
        {
            if (clickThrough == through) return;
            clickThrough = through;
            ApplyClickThrough();
        }

        private void ApplyClickThrough()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero) return;
                int style = GetWindowLong(handle, GWL_EXSTYLE);
                style |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                if (clickThrough) style |= WS_EX_TRANSPARENT;
                else style &= ~WS_EX_TRANSPARENT;
                SetWindowLong(handle, GWL_EXSTYLE, style);
                SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            }
            catch (Exception)
            {
            }
        }

        #endregion

        private void PlaceWindow()
        {
            Rect area = SystemParameters.WorkArea;
            double left = main.Settings.LyricX;
            double top = main.Settings.LyricY;
            if (double.IsNaN(left) || double.IsNaN(top)
                || left < AppSettings.Unset + 1 || top < AppSettings.Unset + 1)
            {
                left = area.Left + (area.Width - Width) / 2;
                top = area.Bottom - 220;
            }
            else
            {
                if (left < area.Left - Width + 80) left = area.Left;
                if (left > area.Right - 80) left = area.Right - Width;
                if (top < area.Top - 10) top = area.Top;
                if (top > area.Bottom - 40) top = area.Bottom - 140;
            }
            Left = left;
            Top = top;
        }

        private void ShowMenu()
        {
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(Item(locked ? "解锁（可拖动）" : "锁定（鼠标穿透）", delegate
            {
                main.ToggleLyricLock();
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("增大字号", delegate { ChangeFont(3); }));
            menu.Items.Add(Item("减小字号", delegate { ChangeFont(-3); }));
            menu.Items.Add(BuildColorMenu());
            menu.Items.Add(Item(main.Settings.LyricShowTranslation ? "隐藏翻译" : "显示翻译", delegate
            {
                main.Settings.LyricShowTranslation = !main.Settings.LyricShowTranslation;
                main.SaveSettings();
                ApplySettings();
                main.RefreshLyricsView();
            }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("回到主界面", delegate { main.ShowFromTrayPublic(); }));
            menu.Items.Add(Item("关闭桌面歌词", delegate { main.ShowDesktopLyrics(false); }));
            menu.PlacementTarget = this;
            menu.IsOpen = true;
        }

        private MenuItem BuildColorMenu()
        {
            MenuItem root = new MenuItem();
            root.Header = "歌词颜色";
            foreach (string color in MainWindow.LyricColorPresets)
            {
                MenuItem item = new MenuItem();
                item.Header = ColorLabel(color);
                string captured = color;
                item.Click += delegate { SetColor(captured); };
                root.Items.Add(item);
            }
            root.Items.Add(new Separator());
            root.Items.Add(Item("自定义颜色…", delegate { PickColor(); }));
            return root;
        }

        private static string ColorLabel(string hex)
        {
            switch (hex)
            {
                case "#FFFFFF": return "白色";
                case "#FFE066": return "暖黄";
                case "#7CE7FF": return "天蓝";
                case "#FF9CC8": return "粉红";
                case "#A8F0A0": return "浅绿";
                case "#C9B6FF": return "淡紫";
                case "#111111": return "黑色";
                case "#4B5563": return "深灰";
                case "#1E3A8A": return "深蓝";
                case "#7F1D1D": return "深红";
                default: return hex;
            }
        }

        private void SetColor(string color)
        {
            main.Settings.LyricColor = color;
            main.SaveSettings();
            ApplySettings();
            main.NotifySettingsChanged();
        }

        private void PickColor()
        {
            using (System.Windows.Forms.ColorDialog dialog = new System.Windows.Forms.ColorDialog())
            {
                dialog.FullOpen = true;
                dialog.AnyColor = true;
                dialog.AnyColor = true;
                try
                {
                    if (!string.IsNullOrEmpty(main.Settings.LyricColor))
                        dialog.Color = System.Drawing.ColorTranslator.FromHtml(main.Settings.LyricColor);
                }
                catch (Exception)
                {
                }
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                SetColor(string.Format("#{0:X2}{1:X2}{2:X2}", dialog.Color.R, dialog.Color.G, dialog.Color.B));
            }
        }

        private static MenuItem Item(string text, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem();
            item.Header = text;
            item.Click += handler;
            return item;
        }
    }
}
