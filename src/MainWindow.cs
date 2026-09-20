using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Skylark
{
    public partial class MainWindow : Window
    {
        public const string AppName = "云雀";
        public const string AppVersion = "3.3.23";

        /// <summary>桌面歌词的预设颜色（浅色背景建议用后面的深色）。</summary>
        public static readonly string[] LyricColorPresets = new string[]
        {
            "#FFFFFF", "#FFE066", "#7CE7FF", "#FF9CC8", "#A8F0A0", "#C9B6FF",
            "#111111", "#4B5563", "#1E3A8A", "#7F1D1D"
        };

        private readonly AppSettings settings;
        private readonly PlayerEngine engine = new PlayerEngine();

        private List<Song> library = new List<Song>();
        private List<Song> queue = new List<Song>();
        private List<Song> visible = new List<Song>();

        /// <summary>当前歌单筛选：空串＝全部歌单。</summary>
        private string playlistFilter = "";
        private readonly StackPanel playlistNav = new StackPanel();
        /** 扫描到的歌单名（＝文件夹名，含空歌单），和歌曲里的歌单取并集。 */
        private readonly List<string> folderPlaylists = new List<string>();
        private string playlistSignature = "";
        private int queueIndex = -1;
        private Song currentSong;
        private string searchText = string.Empty;
        private bool scanning;
        private bool uploading;
        private string cloudRepoName = string.Empty;
        private string cloudRepoId = string.Empty;

        private DispatcherTimer timer;
        private Forms.NotifyIcon tray;
        private bool trayTipShown;
        private bool reallyExit;

        private Grid contentHost;
        /** 应用内弹窗（下载 / 加入歌单 / 新建歌单…）的遮罩层。 */
        private Grid modalHost;
        private bool modalOpen;
        private readonly Dictionary<string, ToggleButton> navToggles = new Dictionary<string, ToggleButton>();
        private LibraryView libraryView;
        private QueueView queueView;
        private LyricsView lyricsView;
        private SettingsView settingsView;
        private DesktopLyricsWindow desktopLyrics;

        private TextBox searchBox;
        private TextBlock searchPlaceholder;
        private TextBlock titleText;
        private TextBlock artistText;
        private TextBlock positionText;
        private TextBlock durationText;
        private Slider progressSlider;
        private ProgressBar bufferingBar;
        private Slider volumeSlider;
        private Button playButton;
        private Button modeButton;
        private Button queueButton;
        private ToggleButton lyricsToggle;
        private ToggleButton lockToggle;
        private TextBlock libraryCountText;
        private TextBlock queueCountText;
        private TextBlock statusText;
        private TextBlock dirLabel;
        private Border toast;
        private TextBlock toastText;
        private bool draggingProgress;

        public MainWindow()
        {
            settings = SettingsStore.Load();
            Theme.Apply(settings.Theme);
            Theme.EnsureStyles();
            TrimCloudCacheOnStart();

            Title = AppName;
            Width = 1180;
            Height = 740;
            MinWidth = 940;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = (Brush)Application.Current.Resources["Window"];
            FontFamily = Ui.Font;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            RestoreWindowBounds();

            engine.Volume = settings.Volume;
            engine.IsMuted = settings.Muted;
            engine.TrackEnded += delegate { Dispatcher.BeginInvoke((Action)delegate { Advance(false); }); };
            engine.Opened += delegate
            {
                Dispatcher.BeginInvoke((Action)delegate
                {
                    TraceStep("engine opened: duration=" + engine.Duration.ToString("0.0")
                        + " playing=" + engine.IsPlaying);
                    OnTrackOpened();
                });
            };
            engine.Failed += delegate(object s, EventArgs e)
            {
                PlayerErrorArgs args = e as PlayerErrorArgs;
                string message = args != null ? args.Message : "播放失败";
                TraceStep("engine failed: " + message);
                Dispatcher.BeginInvoke((Action)delegate { ShowToast("播放失败：" + message); });
            };

            BuildUi();
            InitTray();

            Loaded += OnLoaded;
            Closing += OnClosing;
            Closed += OnClosed;
            StateChanged += OnStateChanged;
            PreviewKeyDown += OnPreviewKeyDown;
            AllowDrop = true;
            Drop += OnDropFiles;
        }

        #region 公开接口（供各视图调用）

        public AppSettings Settings { get { return settings; } }
        public PlayerEngine Engine { get { return engine; } }
        public LyricsView Lyrics { get { return lyricsView; } }
        public List<Song> Library { get { return library; } }
        public List<Song> Queue { get { return queue; } }
        public List<Song> VisibleSongs { get { return visible; } }

        /// <summary>音乐库视图（截图 / 自检用）。</summary>
        public LibraryView LibraryViewRef { get { return libraryView; } }
        public string PlaylistFilter { get { return playlistFilter; } }

        /// <summary>切换歌单筛选（空串＝全部）。</summary>
        public void SetPlaylistFilter(string name)
        {
            playlistFilter = name == null ? "" : name;
            ApplyFilter();
            RefreshPlaylists();
        }
        public int QueueIndex { get { return queueIndex; } }
        public Song CurrentSong { get { return currentSong; } }
        public string SearchText { get { return searchText; } }

        public event EventHandler LibraryChanged;
        public event EventHandler QueueChanged;
        public event EventHandler SongChanged;
        public event EventHandler PlaybackStateChanged;
        public event EventHandler SearchChanged;
        public event EventHandler SettingsChanged;

        public void SetSearch(string text)
        {
            searchText = text == null ? string.Empty : text;
            ApplyFilter();
            Raise(SearchChanged);
        }

        public void PlayFrom(List<Song> context, int index)
        {
            if (context == null || index < 0 || index >= context.Count) return;
            queue = new List<Song>(context);
            queueIndex = index;
            Raise(QueueChanged);
            UpdateQueueState();
            PlayCurrent(true);
        }

        /// <summary>
        /// 点歌名：直接播放这一首，并把它加入播放列表（已经在列表里就只播放，不重复添加）。
        /// </summary>
        public void PlaySong(Song song)
        {
            if (song == null) return;
            int idx = queue.IndexOf(song);
            if (idx < 0)
            {
                queue.Add(song);
                idx = queue.Count - 1;
                Raise(QueueChanged);
                UpdateQueueState();
            }
            queueIndex = idx;
            PlayCurrent(true);
        }

        public void Enqueue(Song song, bool playNext)
        {
            if (song == null) return;

            int existing = queue.IndexOf(song);
            if (existing >= 0)
            {
                if (playNext)
                {
                    Song moved = queue[existing];
                    queue.RemoveAt(existing);
                    if (existing < queueIndex) queueIndex--;
                    int at = queueIndex + 1;
                    if (at > queue.Count) at = queue.Count;
                    if (at < 0) at = 0;
                    queue.Insert(at, moved);
                    Raise(QueueChanged);
                    UpdateQueueState();
                    ShowToast("已把「" + song.Title + "」调整到下一首");
                }
                else
                {
                    ShowToast("播放列表里已经有「" + song.Title + "」了");
                }
                return;
            }

            if (queue.Count == 0)
            {
                queue.Add(song);
                queueIndex = 0;
            }
            else if (playNext)
            {
                int at = queueIndex + 1;
                if (at < 0) at = 0;
                if (at > queue.Count) at = queue.Count;
                queue.Insert(at, song);
            }
            else
            {
                queue.Add(song);
            }
            Raise(QueueChanged);
            UpdateQueueState();
            ShowToast(playNext ? "已设为下一首播放：" + song.Title : "已加入播放队列：" + song.Title);
        }

        public void RemoveFromQueue(Song song)
        {
            int idx = queue.IndexOf(song);
            if (idx < 0) return;
            queue.RemoveAt(idx);
            if (idx < queueIndex) queueIndex--;
            else if (idx == queueIndex)
            {
                if (queue.Count == 0)
                {
                    queueIndex = -1;
                    StopAndClear();
                }
                else
                {
                    if (queueIndex >= queue.Count) queueIndex = 0;
                    PlayCurrent(true);
                }
            }
            Raise(QueueChanged);
            UpdateQueueState();
        }

        public void MoveInQueue(int from, int to)
        {
            if (from < 0 || from >= queue.Count) return;
            if (to < 0 || to >= queue.Count) return;
            Song song = queue[from];
            queue.RemoveAt(from);
            queue.Insert(to, song);
            if (queueIndex == from) queueIndex = to;
            else if (from < queueIndex && to >= queueIndex) queueIndex--;
            else if (from > queueIndex && to <= queueIndex) queueIndex++;
            Raise(QueueChanged);
            UpdateQueueState();
        }

        public void ClearQueue()
        {
            queue.Clear();
            queueIndex = -1;
            Raise(QueueChanged);
            UpdateQueueState();
            StopAndClear();
            ShowToast("已清空播放队列");
        }

        public void RemoveFromLibrary(Song song)
        {
            if (song == null) return;
            if (!settings.Hidden.Contains(song.Path)) settings.Hidden.Add(song.Path);
            library.Remove(song);
            if (queue.Contains(song)) queue.Remove(song);
            SaveSettings();
            ApplyFilter();
            Raise(QueueChanged);
            Raise(LibraryChanged);
            ShowToast("已从音乐库移除：" + song.Title);
        }

        public void PlayNextInQueue(Song song)
        {
            Enqueue(song, true);
        }

        public void Next()
        {
            Advance(true);
        }

        public void Previous()
        {
            if (queue.Count == 0) return;
            if (engine.GetPosition() > 4)
            {
                engine.Seek(0);
                return;
            }
            queueIndex--;
            if (queueIndex < 0)
                queueIndex = settings.Mode == PlayMode.Sequential ? 0 : queue.Count - 1;
            PlayCurrent(true);
        }

        public void TogglePlay()
        {
            if (currentSong == null)
            {
                if (queue.Count > 0)
                {
                    queueIndex = queueIndex >= 0 ? queueIndex : 0;
                    PlayCurrent(true);
                }
                else if (visible.Count > 0)
                {
                    PlayFrom(visible, 0);
                }
                else
                {
                    ShowToast("音乐库里还没有歌曲");
                }
                return;
            }
            engine.TogglePlay();
            UpdatePlayButton();
            Raise(PlaybackStateChanged);
        }

        public void SeekTo(double seconds)
        {
            engine.Seek(seconds);
            UpdateProgress(true);
        }

        public void SetMode(PlayMode mode)
        {
            PlayMode previous = settings.Mode;
            settings.Mode = mode;
            // 随机播放＝点的时候把列表真的打乱一次，之后按顺序往下放；
            // 关掉随机就把原来的顺序还原回来。
            if (mode == PlayMode.Shuffle && previous != PlayMode.Shuffle) EnterShuffle();
            else if (mode != PlayMode.Shuffle && previous == PlayMode.Shuffle) ExitShuffle();
            UpdateModeButton();
            SaveSettings();
            ShowToast("播放模式：" + ModeName(mode));
            Raise(PlaybackStateChanged);
        }

        /// <summary>
        /// 点「随机播放」：把播放列表打乱一次（当前这首放在最前，不打断正在听的），
        /// 之后就和列表循环一样按顺序播。抽签只发生在这一刻，不会每切一首再抽一次。
        /// </summary>
        private void EnterShuffle()
        {
            if (queue.Count <= 1) return;
            if (settings.ShuffleRestore == null || settings.ShuffleRestore.Count == 0)
            {
                settings.ShuffleRestore = new List<string>();
                foreach (Song s in queue) settings.ShuffleRestore.Add(s.Path);
            }
            Song current = queueIndex >= 0 && queueIndex < queue.Count ? queue[queueIndex] : null;
            List<Song> rest = new List<Song>();
            foreach (Song s in queue) if (s != current) rest.Add(s);
            Random random = new Random();
            for (int i = rest.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Song tmp = rest[i];
                rest[i] = rest[j];
                rest[j] = tmp;
            }
            queue.Clear();
            if (current != null)
            {
                queue.Add(current);
                queueIndex = 0;
            }
            queue.AddRange(rest);
            if (current == null) queueIndex = 0;
            UpdateQueueState();
        }

        /// <summary>关掉随机播放：按进入随机前记下的顺序把列表还原（当前这首继续放）。</summary>
        private void ExitShuffle()
        {
            if (settings.ShuffleRestore == null || settings.ShuffleRestore.Count == 0) return;
            Song current = queueIndex >= 0 && queueIndex < queue.Count ? queue[queueIndex] : null;
            List<Song> ordered = new List<Song>();
            foreach (string path in settings.ShuffleRestore)
            {
                Song found = queue.Find(delegate(Song s) { return s.Path == path; });
                if (found != null && !ordered.Contains(found)) ordered.Add(found);
            }
            // 随机之后新加进来的歌，按添加顺序接在后面
            foreach (Song s in queue) if (!ordered.Contains(s)) ordered.Add(s);
            queue.Clear();
            queue.AddRange(ordered);
            queueIndex = current == null ? -1 : queue.IndexOf(current);
            settings.ShuffleRestore = null;
            UpdateQueueState();
        }

        public void CycleMode()
        {
            PlayMode next = (PlayMode)(((int)settings.Mode + 1) % 4);
            SetMode(next);
        }

        public void ShowDesktopLyrics(bool on)
        {
            settings.DesktopLyricsOn = on;
            if (lyricsToggle != null) lyricsToggle.IsChecked = on;
            if (on)
            {
                if (desktopLyrics == null)
                {
                    desktopLyrics = new DesktopLyricsWindow(this);
                    desktopLyrics.Closed += delegate { desktopLyrics = null; if (lyricsToggle != null) lyricsToggle.IsChecked = false; };
                }
                desktopLyrics.Show();
                desktopLyrics.UpdateNow();
            }
            else if (desktopLyrics != null)
            {
                desktopLyrics.Close();
                desktopLyrics = null;
            }
            SaveSettings();
            Raise(PlaybackStateChanged);
        }

        /// <summary>锁定 / 解锁桌面歌词（锁定时鼠标穿透，不干扰其它操作）。</summary>
        public void SetLyricLocked(bool locked, bool notify)
        {
            settings.LyricLocked = locked;
            if (lockToggle != null) lockToggle.IsChecked = locked;
            if (desktopLyrics != null)
            {
                desktopLyrics.ApplySettings();
                desktopLyrics.FlashHint(locked
                    ? "已锁定：鼠标可穿透（把鼠标移到右上角可解锁）"
                    : "已解锁：可拖动、可右键");
            }
            SaveSettings();
            if (notify)
            {
                ShowToast(locked
                    ? "桌面歌词已锁定（鼠标穿透）· 鼠标移到右上角、或按 Ctrl+Alt+L 解锁"
                    : "桌面歌词已解锁，可自由拖动");
                Raise(SettingsChanged);
            }
        }

        public void ToggleLyricLock()
        {
            SetLyricLocked(!settings.LyricLocked, true);
        }

        public void ShowView(string name)
        {
            if (contentHost == null) return;
            // 换页就退出批量编辑：音乐库和播放队列共用同一批 Song 对象
            if (libraryView != null) libraryView.ExitBatch();
            if (queueView != null) queueView.ExitBatch();
            UIElement target = libraryView;
            if (name == "queue") target = queueView;
            else if (name == "lyrics") target = lyricsView;
            else if (name == "settings") target = settingsView;
            foreach (UIElement child in contentHost.Children) child.Visibility = Visibility.Collapsed;
            target.Visibility = Visibility.Visible;
            settings.LastView = name;
            foreach (KeyValuePair<string, ToggleButton> pair in navToggles)
            {
                pair.Value.IsChecked = pair.Key == name;
            }
            if (target is LibraryView) ((LibraryView)target).FocusList();
        }

        /// <summary>
        /// 应用内弹窗：卡片跟主界面同一套配色和按钮，不再弹系统窗口
        /// （那个又丑又跟主题打架，用户已经吐槽过一轮）。
        /// </summary>
        public void ShowModal(string title, string hint, UIElement body, List<ModalAction> actions)
        {
            ShowModal(title, hint, body, actions, 440);
        }

        public void ShowModal(string title, string hint, UIElement body, List<ModalAction> actions, double width)
        {
            if (modalHost == null) return;
            CloseModal();

            StackPanel card = new StackPanel();
            card.Children.Add(Ui.Text(title, 17, "Text", FontWeights.SemiBold));
            if (!string.IsNullOrEmpty(hint))
            {
                TextBlock hintText = Ui.Text(hint, 12.5, "TextMuted");
                hintText.TextWrapping = TextWrapping.Wrap;
                hintText.Margin = new Thickness(0, 8, 0, 0);
                card.Children.Add(hintText);
            }
            if (body != null)
            {
                body.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 18, 0, 0));
                card.Children.Add(body);
            }
            if (actions != null && actions.Count > 0)
            {
                StackPanel row = new StackPanel();
                row.Orientation = Orientation.Horizontal;
                row.HorizontalAlignment = HorizontalAlignment.Right;
                row.Margin = new Thickness(0, 20, 0, 0);
                for (int i = 0; i < actions.Count; i++)
                {
                    ModalAction action = actions[i];
                    Button button = Ui.Button(action.Label, action.StyleKey, delegate
                    {
                        if (action.AutoClose) CloseModal();
                        if (action.OnClick != null) action.OnClick();
                    });
                    if (i > 0) button.Margin = new Thickness(8, 0, 0, 0);
                    row.Children.Add(button);
                }
                card.Children.Add(row);
            }

            Border box = new Border();
            box.Style = (Style)Application.Current.Resources["CardBox"];
            Ui.Bind(box, Border.BackgroundProperty, "Panel");
            box.CornerRadius = new CornerRadius(16);
            box.Padding = new Thickness(22, 20, 22, 20);
            box.Width = width;
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Child = card;
            box.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 30,
                ShadowDepth = 8,
                Opacity = 0.3,
                Color = Colors.Black
            };

            Border mask = new Border();
            mask.Background = new SolidColorBrush(Color.FromArgb(145, 0, 0, 0));
            mask.Child = box;
            mask.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                // 点卡片外面的空白处关掉
                if (e.OriginalSource == mask) CloseModal();
            };

            modalHost.Children.Clear();
            modalHost.Children.Add(mask);
            modalHost.Visibility = Visibility.Visible;
            modalOpen = true;

            TextBox focus = FindTextBox(card);
            if (focus != null)
            {
                TextBox target = focus;
                Dispatcher.BeginInvoke((Action)delegate
                {
                    target.Focus();
                    target.SelectAll();
                });
            }
        }

        public void CloseModal()
        {
            if (modalHost == null) return;
            modalHost.Children.Clear();
            modalHost.Visibility = Visibility.Collapsed;
            modalOpen = false;
        }

        public bool ModalOpen
        {
            get { return modalOpen; }
        }

        private static TextBox FindTextBox(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                TextBox box = child as TextBox;
                if (box != null) return box;
                DependencyObject node = child as DependencyObject;
                if (node == null) continue;
                TextBox nested = FindTextBox(node);
                if (nested != null) return nested;
            }
            return null;
        }

        public void ShowToast(string message)
        {
            if (toast == null) return;
            toastText.Text = message;
            toast.Visibility = Visibility.Visible;
            toast.BeginAnimation(UIElement.OpacityProperty, null);
            DoubleAnimation fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350));
            fadeOut.BeginTime = TimeSpan.FromMilliseconds(1900);
            fadeOut.Completed += delegate { toast.Visibility = Visibility.Collapsed; };
            Storyboard sb = new Storyboard();
            sb.Children.Add(fadeIn);
            sb.Children.Add(fadeOut);
            Storyboard.SetTarget(sb, toast);
            Storyboard.SetTargetProperty(sb, new PropertyPath(UIElement.OpacityProperty));
            sb.Begin();
        }

        public void ChooseMusicDir()
        {
            Forms.FolderBrowserDialog dialog = new Forms.FolderBrowserDialog();
            dialog.Description = "选择音乐文件夹（歌词 .lrc 与歌曲放在一起）";
            dialog.ShowNewFolderButton = false;
            string current = settings.MusicDir;
            if (!string.IsNullOrEmpty(current) && Directory.Exists(current)) dialog.SelectedPath = current;
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
            {
                SetMusicDir(dialog.SelectedPath);
            }
        }

        public void SetMusicDir(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            settings.MusicDir = dir;
            settings.Source = "local";
            SaveSettings();
            Raise(SettingsChanged);
            Rescan();
        }

        public void Rescan()
        {
            if (scanning) return;
            if (settings.Source == "cloud")
            {
                if (string.IsNullOrEmpty(CloudEndpoint))
                {
                    ShowToast("请先在设置里填写云盘分享链接或 API 令牌");
                    return;
                }
                scanning = true;
                RescanCloud();
                return;
            }
            scanning = true;
            string dir = settings.MusicDir;
            bool recursive = settings.Recursive;
            List<DurationEntry> cache = settings.Durations;
            List<string> hidden = settings.Hidden;
            ShowToast("正在扫描音乐文件夹…");
            if (statusText != null) statusText.Text = "正在扫描…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                ScanResult result = LibraryScanner.Scan(dir, recursive, cache, hidden);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    ApplyScan(result);
                });
            });
        }

        /// <summary>云盘连接方式：填了 API 令牌就用令牌，否则用分享链接。</summary>
        public string CloudEndpoint
        {
            get
            {
                if (!string.IsNullOrEmpty(settings.CloudToken)) return settings.CloudToken.Trim();
                return settings.CloudUrl == null ? string.Empty : settings.CloudUrl.Trim();
            }
        }

        /// <summary>是否可以使用 API 令牌删除云端文件。</summary>
        public bool CanDeleteCloud
        {
            get { return CloudClient.IsApiToken(CloudEndpoint); }
        }

        public bool IsCloudSource
        {
            get { return !string.IsNullOrEmpty(CloudEndpoint); }
        }

        private void RescanCloud()
        {
            string url = CloudEndpoint;
            List<DurationEntry> cache = settings.Durations;
            List<string> hidden = settings.Hidden;
            ShowToast("正在读取云盘文件列表…");
            if (statusText != null) statusText.Text = "正在读取云盘…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    FetchRepoInfo(url);
                    ScanResult result = CloudLibrary.Scan(url, cache, hidden);
                    Dispatcher.BeginInvoke((Action)delegate { ApplyScan(result); });
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        scanning = false;
                        ShowToast("无法读取云盘：" + ex.Message);
                        if (statusText != null) statusText.Text = "云盘读取失败";
                    });
                }
            });
        }

        /// <summary>令牌模式下取一次资料库名称与 ID（用于界面显示与「打开云盘」）。</summary>
        private void FetchRepoInfo(string endpoint)
        {
            if (!CloudClient.IsApiToken(endpoint)) return;
            try
            {
                CloudRepoInfo info = CloudClient.GetRepoInfo(endpoint);
                cloudRepoName = info.Name;
                cloudRepoId = info.RepoId;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>后台补齐云盘歌曲的歌词与时长。</summary>
        private void FillCloudDetails()
        {
            if (!IsCloudSource || library.Count == 0) return;
            string url = CloudEndpoint;
            List<Song> songs = new List<Song>(library);
            ThreadPool.QueueUserWorkItem(delegate
            {
                CloudLibrary.FillDetails(url, songs, delegate(Song song)
                {
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        if (song == currentSong && lyricsView != null) lyricsView.Load(song);
                        if (song == currentSong && desktopLyrics != null) desktopLyrics.UpdateNow();
                    });
                }, 0);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    SaveSettings();
                    UpdateStatusText();
                });
            });
        }

        public void ToggleTheme()
        {
            Theme.Toggle();
            settings.Theme = Theme.Mode;
            Background = (Brush)Application.Current.Resources["Window"];
            ApplyDarkTitleBar();
            SaveSettings();
            Raise(SettingsChanged);
        }

        /// <summary>设置主题模式：system / dark / light。</summary>
        public void SetThemeMode(string mode)
        {
            Theme.Apply(mode);
            settings.Theme = Theme.Mode;
            Background = (Brush)Application.Current.Resources["Window"];
            ApplyDarkTitleBar();
            SaveSettings();
            Raise(SettingsChanged);
        }

        public void OpenMusicFolder()
        {
            OpenMusicDir();
        }

        public void RefreshMediaKeys()
        {
            RegisterMediaKeys();
        }

        public void NotifySettingsChanged()
        {
            Raise(SettingsChanged);
        }

        public void ShowFromTrayPublic()
        {
            ShowFromTray();
        }

        /// <summary>重新构建歌词页面（翻译开关等变化后调用）。</summary>
        public void RefreshLyricsView()
        {
            if (lyricsView == null) return;
            lyricsView.Load(currentSong);
            if (desktopLyrics != null) desktopLyrics.UpdateNow();
        }

        public void SaveSettings()
        {
            settings.Volume = engine.Volume;
            settings.Muted = engine.IsMuted;
            // 播放列表：只有「已经恢复过上次的列表」或「列表里确实有歌」时才写回。
            // 否则启动早期（云盘还没扫完、内存列表还是空的）的那次自动保存
            // 会把配置文件里保存的播放列表清空，下次打开就什么都没了。
            if (restoreAttempted || queue.Count > 0)
            {
                settings.Queue = new List<string>();
                foreach (Song s in queue) settings.Queue.Add(s.Path);
                settings.QueueIndex = queueIndex;
            }
            SettingsStore.Save(settings);
        }

        public void RevealInExplorer(Song song)
        {
            if (song == null || !File.Exists(song.Path)) return;
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + song.Path + "\"");
            }
            catch (Exception)
            {
            }
        }

        public DesktopLyricsWindow DesktopLyrics
        {
            get { return desktopLyrics; }
        }

        #endregion


        #region 托盘 / 热键 / 窗口

        private static Drawing.Icon AppIcon()
        {
            try
            {
                return Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void ApplyWindowIcon()
        {
            Drawing.Icon icon = AppIcon();
            if (icon == null) return;
            ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            Icon = source;
        }

        private void InitTray()
        {
            ApplyWindowIcon();
            if (MainWindow.Headless) return;
            tray = new Forms.NotifyIcon();
            Drawing.Icon icon = AppIcon();
            if (icon != null) tray.Icon = icon;
            tray.Text = AppName;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowFromTray(); };

            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示主界面", null, delegate { ShowFromTray(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("播放 / 暂停", null, delegate { Dispatcher.BeginInvoke((Action)delegate { TogglePlay(); }); });
            menu.Items.Add("上一首", null, delegate { Dispatcher.BeginInvoke((Action)delegate { Previous(); }); });
            menu.Items.Add("下一首", null, delegate { Dispatcher.BeginInvoke((Action)delegate { Next(); }); });
            menu.Items.Add("桌面歌词", null, delegate
            {
                Dispatcher.BeginInvoke((Action)delegate { ShowDesktopLyrics(!settings.DesktopLyricsOn); });
            });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Dispatcher.BeginInvoke((Action)delegate { ExitApp(); }); });
            tray.ContextMenuStrip = menu;
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
        }

        private void ExitApp()
        {
            reallyExit = true;
            Close();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            TraceStep("closing");
            if (!reallyExit && settings.CloseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            reallyExit = true;
            // 退出时不必手动注销热键：进程结束系统会自动释放，
            // 在窗口关闭过程中调用 UnregisterHotKey 反而可能卡住消息循环。
            TraceStep("hotkeys skipped");
            SaveStateBeforeExit();
            TraceStep("state saved");
        }

        private void SaveStateBeforeExit()
        {
            try
            {
                if (currentSong != null)
                {
                    settings.LastSongPath = currentSong.Path;
                    settings.LastPosition = engine.GetPosition();
                }
                if (WindowState == WindowState.Normal)
                {
                    settings.WindowWidth = Width;
                    settings.WindowHeight = Height;
                    settings.WindowX = Left;
                    settings.WindowY = Top;
                }
                settings.WindowMaximized = WindowState == WindowState.Maximized;
                if (desktopLyrics != null)
                {
                    settings.LyricX = desktopLyrics.Left;
                    settings.LyricY = desktopLyrics.Top;
                }
                SaveSettings();
            }
            catch (Exception)
            {
            }
        }

        private void OnClosed(object sender, EventArgs e)
        {
            TraceStep("closed: stop timer");
            if (timer != null) timer.Stop();
            TraceStep("closed: engine");
            engine.Close();
            // 不开缓存时，退出也只留「正在听的那一首 + 下一首」，多余的清掉
            if (settings.CloudCacheMode != 2) PruneCloudCache(currentSong, PeekNextSong());
            TraceStep("closed: tray");
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
            TraceStep("closed: lyrics");
            if (desktopLyrics != null) desktopLyrics.Close();
            TraceStep("closed: shutdown");
            Application.Current.Shutdown();
            TraceStep("closed: done");
        }

        /// <summary>仅在自检 / 冒烟模式下写步骤日志，方便定位卡死。</summary>
        internal static void TraceStep(string step)
        {
            if (!Headless) return;
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skylark-smoke.log");
                using (System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Append,
                    System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(fs, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  [win] " + step);
                }
            }
            catch (Exception)
            {
            }
        }

        private void HideToTray()
        {
            Hide();
            if (!trayTipShown)
            {
                trayTipShown = true;
                if (tray != null) tray.ShowBalloonTip(2000, AppName, "程序已最小化到托盘，双击图标可以恢复。", Forms.ToolTipIcon.Info);
            }
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && settings.MinimizeToTray)
            {
                HideToTray();
            }
        }

        private void RestoreWindowBounds()
        {
            if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
            {
                Width = settings.WindowWidth;
                Height = settings.WindowHeight;
            }
            if (!double.IsNaN(settings.WindowX) && !double.IsNaN(settings.WindowY)
                && settings.WindowX > AppSettings.Unset + 1 && settings.WindowY > AppSettings.Unset + 1)
            {
                double vLeft = SystemParameters.VirtualScreenLeft;
                double vTop = SystemParameters.VirtualScreenTop;
                double vRight = vLeft + SystemParameters.VirtualScreenWidth;
                double vBottom = vTop + SystemParameters.VirtualScreenHeight;
                if (settings.WindowX >= vLeft - 50 && settings.WindowX < vRight - 100
                    && settings.WindowY >= vTop - 20 && settings.WindowY < vBottom - 80)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = settings.WindowX;
                    Top = settings.WindowY;
                }
            }
            if (settings.WindowMaximized) WindowState = WindowState.Maximized;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (modalOpen)
            {
                // 弹窗开着的时候只认 Esc，别让空格 / 方向键之类去操作播放
                if (e.Key == Key.Escape)
                {
                    CloseModal();
                    e.Handled = true;
                }
                return;
            }
            bool inTextBox = Keyboard.FocusedElement is TextBox;
            if (e.Key == Key.Escape && inTextBox)
            {
                searchBox.Text = string.Empty;
                e.Handled = true;
                return;
            }
            if (inTextBox) return;

            ModifierKeys mods = Keyboard.Modifiers;
            switch (e.Key)
            {
                case Key.Space:
                    TogglePlay();
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (mods == ModifierKeys.Control) Previous();
                    else SeekTo(Math.Max(0, engine.GetPosition() - 5));
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (mods == ModifierKeys.Control) Next();
                    else SeekTo(engine.GetPosition() + 5);
                    e.Handled = true;
                    break;
                case Key.Up:
                    engine.Volume = Math.Min(1, engine.Volume + 0.05);
                    UpdateVolumeSlider();
                    SaveSettingsDebounced();
                    e.Handled = true;
                    break;
                case Key.Down:
                    engine.Volume = Math.Max(0, engine.Volume - 0.05);
                    UpdateVolumeSlider();
                    SaveSettingsDebounced();
                    e.Handled = true;
                    break;
                case Key.F:
                    FocusSearch();
                    e.Handled = true;
                    break;
                case Key.L:
                    ShowView("lyrics");
                    e.Handled = true;
                    break;
                case Key.Q:
                    ShowView("queue");
                    e.Handled = true;
                    break;
                case Key.D:
                    ShowDesktopLyrics(!settings.DesktopLyricsOn);
                    e.Handled = true;
                    break;
                case Key.M:
                    engine.IsMuted = !engine.IsMuted;
                    SettingsChangedSafe();
                    break;
            }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int HotkeyPlay = 1;
        private const int HotkeyPrev = 2;
        private const int HotkeyNext = 3;
        private const int HotkeyLockLyrics = 4;
        private const int HotkeyToggleLyrics = 5;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint VkMediaPlayPause = 0xB3;
        private const uint VkMediaPrev = 0xB1;
        private const uint VkMediaNext = 0xB0;

        private HwndSource hotkeySource;
        private bool mediaKeysRegistered;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hotkeySource = (HwndSource)PresentationSource.FromVisual(this);
            if (hotkeySource != null) hotkeySource.AddHook(WndProc);
            RegisterMediaKeys();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SETTINGCHANGE && Theme.Mode == "system")
            {
                string before = Theme.Current;
                Theme.Apply("system");
                if (Theme.Current != before)
                {
                    Background = (Brush)Application.Current.Resources["Window"];
                    ApplyDarkTitleBar();
                    Raise(SettingsChanged);
                }
            }
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HotkeyPlay)
                {
                    TogglePlay();
                    handled = true;
                }
                else if (id == HotkeyPrev)
                {
                    Previous();
                    handled = true;
                }
                else if (id == HotkeyNext)
                {
                    Next();
                    handled = true;
                }
                else if (id == HotkeyLockLyrics)
                {
                    ToggleLyricLock();
                    handled = true;
                }
                else if (id == HotkeyToggleLyrics)
                {
                    ShowDesktopLyrics(!settings.DesktopLyricsOn);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void RegisterMediaKeys()
        {
            if (hotkeySource == null) return;
            UnregisterMediaKeys();
            if (!settings.MediaKeys) return;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            bool any = false;
            if (RegisterHotKey(handle, HotkeyPlay, 0, VkMediaPlayPause)) any = true;
            if (RegisterHotKey(handle, HotkeyPrev, 0, VkMediaPrev)) any = true;
            if (RegisterHotKey(handle, HotkeyNext, 0, VkMediaNext)) any = true;

            uint mods = ModControl | ModAlt;
            if (RegisterHotKey(handle, HotkeyLockLyrics, mods, (uint)KeyInterop.VirtualKeyFromKey(Key.L))) any = true;
            if (RegisterHotKey(handle, HotkeyToggleLyrics, mods, (uint)KeyInterop.VirtualKeyFromKey(Key.D))) any = true;
            mediaKeysRegistered = any;
        }

        private void UnregisterMediaKeys()
        {
            if (!mediaKeysRegistered) return;
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                UnregisterHotKey(handle, HotkeyPlay);
                UnregisterHotKey(handle, HotkeyPrev);
                UnregisterHotKey(handle, HotkeyNext);
                UnregisterHotKey(handle, HotkeyLockLyrics);
                UnregisterHotKey(handle, HotkeyToggleLyrics);
            }
            catch (Exception)
            {
            }
            mediaKeysRegistered = false;
        }

        private void ApplyDarkTitleBar()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                int value = Theme.Current == "dark" ? 1 : 0;
                if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
            }
            catch (Exception)
            {
            }
        }

        public void RefreshTitleBar()
        {
            ApplyDarkTitleBar();
        }

        #endregion

        private static Color ParseColor(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
    }
}



