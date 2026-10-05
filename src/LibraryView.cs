using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Skylark
{
    /// <summary>音乐库视图（歌曲列表 + 排序 + 右键菜单）。</summary>
    public class LibraryView : UserControl
    {
        private readonly MainWindow main;
        private readonly ListBox list = new ListBox();
        private readonly TextBlock summary = Ui.Text("", 12.5, "TextMuted");
        private readonly Button headIndex = new Button();
        private readonly Button headTitle = new Button();
        private readonly Button headArtist = new Button();
        private readonly Border emptyState = new Border();
        /** 右键点在哪一行上（多选菜单要用它当「主行」）。 */
        private Song menuSong;
        /** 批量编辑模式：行首出现复选框，点一行是勾选而不是播放。 */
        private bool batchMode;
        private readonly TextBlock selCount = Ui.Text("", 12.5, "TextMuted");
        private StackPanel normalActions;
        private StackPanel batchActions;
        private Button batchButton;
        private Button selectAllButton;
        /** 列头所在的那一行：它的右边距要跟列表的实际可视宽度对齐（滚动条会占掉十几个像素）。 */
        private readonly Grid columns = new Grid();

        public LibraryView(MainWindow owner)
        {
            main = owner;
            Padding = new Thickness(22, 16, 22, 4);

            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[0].Height = GridLength.Auto;
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[1].Height = GridLength.Auto;
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);

            // 标题
            TextBlock title = Ui.Text("音乐库", 21, "Text", FontWeights.SemiBold);
            summary.Margin = new Thickness(0, 4, 0, 0);
            StackPanel head = Ui.Column(0, title, summary);
            head.VerticalAlignment = VerticalAlignment.Center;
            head.Margin = new Thickness(2, 0, 0, 12);

            Button playAll = IconTextButton("play", "播放全部", "PrimaryButton", delegate { PlayAll(); });
            Button upload = IconTextButton("upload", "上传", "OutlineButton",
                delegate { main.PickAndUploadFiles(); });
            upload.ToolTip = "把本地歌曲 / 歌词上传到当前歌单（也可以直接把文件拖进窗口）";
            batchButton = IconTextButton("check", "批量编辑", "OutlineButton",
                delegate { SetBatchMode(!batchMode); });
            batchButton.ToolTip = "批量编辑：点一行勾一首，然后批量下载 / 加入歌单 / 删除";
            Button findMusic = IconTextButton("plus", "在线找歌", "OutlineButton", delegate { main.ShowMusicSearch(); });
            normalActions = Ui.Row(8, batchButton, findMusic, upload, playAll);
            normalActions.VerticalAlignment = VerticalAlignment.Center;

            // 批量编辑模式下，标题右边换成批量操作
            selCount.VerticalAlignment = VerticalAlignment.Center;
            selectAllButton = IconTextButton("check", "全选", "OutlineButton", delegate { ToggleSelectAll(); });
            StackPanel selectAll = Ui.Row(8, selCount, selectAllButton,
                IconTextButton("download", "下载", "OutlineButton",
                    delegate { main.DownloadSongs(GetSelectedSongs()); }),
                IconTextButton("plus", "加入歌单", "OutlineButton",
                    delegate { main.AddSelectionToPlaylist(GetSelectedSongs()); }),
                IconTextButton("trash", "删除", "OutlineButton",
                    delegate { main.DeleteSongs(GetSelectedSongs()); }),
                Ui.Button("完成", "PrimaryButton", delegate { SetBatchMode(false); }));
            batchActions = selectAll;
            batchActions.VerticalAlignment = VerticalAlignment.Center;
            batchActions.Visibility = Visibility.Collapsed;

            StackPanel headActions = Ui.Row(8, normalActions, batchActions);
            headActions.VerticalAlignment = VerticalAlignment.Center;

            Grid headRow = new Grid();
            headRow.ColumnDefinitions.Add(new ColumnDefinition());
            headRow.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            headRow.ColumnDefinitions.Add(new ColumnDefinition());
            headRow.ColumnDefinitions[1].Width = GridLength.Auto;
            headRow.Children.Add(head);
            Grid.SetColumn(headActions, 1);
            headRow.Children.Add(headActions);
            headRow.Margin = new Thickness(0, 0, 0, 12);
            root.Children.Add(headRow);

            // 列头
            columns.Margin = new Thickness(10, 0, 22, 2);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[0].Width = Ui.Px(56);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[1].Width = Ui.Stars(1);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[2].Width = Ui.Px(190);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[3].Width = Ui.Px(40);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions[4].Width = Ui.Px(30);

            Style headerStyle = (Style)Application.Current.Resources["ColumnHeader"];
            SetupHeader(headIndex, "序号", headerStyle, SortField.Default);
            SetupHeader(headTitle, "歌曲", headerStyle, SortField.Title);
            SetupHeader(headArtist, "歌手", headerStyle, SortField.Artist);
            headTitle.HorizontalContentAlignment = HorizontalAlignment.Left;
            headArtist.HorizontalContentAlignment = HorizontalAlignment.Left;
            headIndex.HorizontalContentAlignment = HorizontalAlignment.Center;

            columns.Children.Add(headIndex);
            Grid.SetColumn(headTitle, 1);
            columns.Children.Add(headTitle);
            Grid.SetColumn(headArtist, 2);
            columns.Children.Add(headArtist);

            Grid.SetRow(columns, 1);
            root.Children.Add(columns);

            // 列表
            list.Style = (Style)Application.Current.Resources["SongList"];
            list.ItemContainerStyle = (Style)Application.Current.Resources["SongItem"];
            list.ItemTemplate = (DataTemplate)Application.Current.Resources["SongRowTemplate"];
            list.Padding = new Thickness(0, 2, 0, 8);
            list.SelectionMode = SelectionMode.Single;
            list.SelectionChanged += delegate { UpdateBatchCount(); };
            list.MouseDoubleClick += OnDoubleClick;
            list.AddHandler(UIElement.MouseLeftButtonUpEvent,
                new MouseButtonEventHandler(OnItemClick), true);
            list.PreviewMouseRightButtonDown += OnRightDown;
            list.ContextMenuOpening += OnContextMenu;
            list.KeyDown += OnKeyDown;
            Grid.SetRow(list, 2);
            root.Children.Add(list);
            // 列表里有滚动条时可视宽度会窄十几个像素，列头要跟着缩，否则「歌手」会和下面的内容错位
            list.SizeChanged += delegate { SyncHeaderInset(); };
            list.Loaded += delegate { SyncHeaderInset(); };
            list.ItemContainerGenerator.StatusChanged += delegate { SyncHeaderInset(); };

            // 空状态
            emptyState.Visibility = Visibility.Collapsed;
            emptyState.HorizontalAlignment = HorizontalAlignment.Center;
            emptyState.VerticalAlignment = VerticalAlignment.Center;
            StackPanel emptyPanel = new StackPanel();
            emptyPanel.HorizontalAlignment = HorizontalAlignment.Center;
            Canvas emptyIcon = Icons.Create("music", 46, "TextMuted");
            emptyIcon.HorizontalAlignment = HorizontalAlignment.Center;
            TextBlock emptyTitle = Ui.Text("这里还没有歌曲", 16, "Text", FontWeights.SemiBold);
            emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
            emptyTitle.Margin = new Thickness(0, 12, 0, 0);
            TextBlock emptyHint = Ui.Text("把歌曲（歌名 - 歌手.mp3）和同名 .lrc 歌词放进音乐文件夹即可", 12.5, "TextMuted");
            emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
            emptyHint.Margin = new Thickness(0, 6, 0, 0);
            Button choose = Ui.Button("选择音乐文件夹", "PrimaryButton", delegate { main.ChooseMusicDir(); });
            choose.HorizontalAlignment = HorizontalAlignment.Center;
            choose.Margin = new Thickness(0, 16, 0, 0);
            emptyPanel.Children.Add(emptyIcon);
            emptyPanel.Children.Add(emptyTitle);
            emptyPanel.Children.Add(emptyHint);
            emptyPanel.Children.Add(choose);
            emptyState.Child = emptyPanel;
            Grid.SetRow(emptyState, 2);
            root.Children.Add(emptyState);

            Content = root;
        }

        private void SetupHeader(Button button, string text, Style style, SortField field)
        {
            button.Style = style;
            button.Content = Ui.Text(text, 12, "TextMuted");
            button.Tag = field;
            button.Click += delegate { SortBy((SortField)button.Tag); };
        }

        private void SortBy(SortField field)
        {
            if (main.Settings.Sort == field)
                main.Settings.SortAscending = !main.Settings.SortAscending;
            else
            {
                main.Settings.Sort = field;
                main.Settings.SortAscending = true;
            }
            main.ApplyFilter();
            main.SaveSettings();
            UpdateHeaderArrows();
        }

        /// <summary>
        /// 列头对齐列表：列表内部有滚动条时可视宽度会小十几像素，
        /// 列头就按这个差值缩右边距，保证「歌手」和下面每一行的歌手名在同一个 x 上。
        /// </summary>
        private void SyncHeaderInset()
        {
            if (columns == null || list == null || list.ActualWidth <= 0) return;
            ScrollViewer viewer = FindScrollViewer(list);
            double inset = 0;
            if (viewer != null && viewer.ViewportWidth > 0)
                inset = Math.Max(0, list.ActualWidth - viewer.ViewportWidth);
            if (inset <= 0 && list.Items.Count > 0)
            {
                // 视觉树还没建好时退一步：用第一个可见项的实际宽度反推滚动条占了多少
                FrameworkElement first = list.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                if (first != null && first.ActualWidth > 0 && list.ActualWidth > first.ActualWidth)
                    inset = list.ActualWidth - first.ActualWidth;
            }
            columns.Margin = new Thickness(10, 0, 10 + inset, 2);
        }

        private static ScrollViewer FindScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                ScrollViewer viewer = child as ScrollViewer;
                if (viewer != null) return viewer;
                ScrollViewer nested = FindScrollViewer(child);
                if (nested != null) return nested;
            }
            return null;
        }

        private void UpdateHeaderArrows()
        {
            string arrow = main.Settings.SortAscending ? " ▲" : " ▼";
            headIndex.Content = Ui.Text("序号" + (main.Settings.Sort == SortField.Default ? arrow : ""), 12, "TextMuted");
            headTitle.Content = Ui.Text("歌曲" + (main.Settings.Sort == SortField.Title ? arrow : ""), 12, "TextMuted");
            headArtist.Content = Ui.Text("歌手" + (main.Settings.Sort == SortField.Artist ? arrow : ""), 12, "TextMuted");
            int i = 1;
            foreach (Song song in main.VisibleSongs) song.IndexText = (i++).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>重新生成列表（过滤 / 排序 / 扫描后调用）。</summary>
        public void RefreshItems()
        {
            List<Song> songs = main.VisibleSongs;
            int i = 1;
            foreach (Song song in songs)
            {
                song.IndexText = (i++).ToString(CultureInfo.InvariantCulture);
                song.IsCurrent = song == main.CurrentSong;
                song.BatchMode = batchMode;
            }
            list.ItemsSource = null;
            list.ItemsSource = songs;
            UpdateBatchCount();

            string text = songs.Count.ToString(CultureInfo.InvariantCulture) + " 首";
            if (main.PlaylistFilter.Length > 0 && main.DistinctCount() != songs.Count)
                text += "（全部歌曲共 " + main.DistinctCount() + " 首）";
            summary.Text = text;

            bool empty = songs.Count == 0;
            emptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            if (empty)
            {
                TextBlock hint = null;
                StackPanel panel = emptyState.Child as StackPanel;
                if (panel != null && panel.Children.Count > 2) hint = panel.Children[2] as TextBlock;
                if (hint != null)
                {
                    if (main.Library.Count == 0)
                    {
                        hint.Text = main.IsCloudSource
                            ? "把歌曲（歌名 - 歌手.mp3）和同名 .lrc 歌词放进云盘分享文件夹，\n然后在「设置」里点「刷新列表」即可\n云盘：" + main.Settings.CloudUrl
                            : "把歌曲（歌名 - 歌手.mp3）和同名 .lrc 歌词放进音乐文件夹即可\n当前目录：" + main.Settings.MusicDir;
                    }
                    else
                    {
                        hint.Text = main.PlaylistFilter.Length > 0 && main.SearchText.Length == 0
                            ? "这个歌单还是空的。\n在音乐库里点「批量编辑」勾几首，再「加入歌单」放进来。"
                            : "没有匹配「" + main.SearchText + "」的歌曲";
                    }
                }
            }
            UpdateHeaderArrows();
        }

        private static string FormatTotal(double seconds)
        {
            int total = (int)Math.Round(seconds);
            int h = total / 3600;
            int m = (total % 3600) / 60;
            if (h > 0) return h + " 小时 " + m + " 分";
            return Math.Max(m, 1) + " 分钟";
        }

        public void FocusList()
        {
            if (list.Items.Count > 0) list.Focus();
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            if (FindAction(source, "download")) return;
            Song song = SongAt(source);
            if (song != null) main.PlaySong(song);
        }

        /// <summary>
        /// 单击整行：播放这一首（不进播放队列，队列只由「播放全部」和右键「添加到播放队列」维护）。
        /// 批量编辑模式下点一行是勾选；点行尾的下载图标则下载这一首。
        /// </summary>
        private void OnItemClick(object sender, MouseButtonEventArgs e)
        {
            DependencyObject source = e.OriginalSource as DependencyObject;
            if (FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) != null) return;

            ListBoxItem item = FindItem(source);
            if (item == null) return;
            Song song = item.DataContext as Song;
            if (song == null) return;

            if (FindAction(source, "download"))
            {
                List<Song> one = new List<Song>();
                one.Add(song);
                main.DownloadSongs(one);
                return;
            }
            if (batchMode) return;   // 勾选交给 ListBox 自己处理
            main.PlaySong(song);
        }

        /// <summary>进入 / 退出批量编辑：行首换成复选框，标题右边换成批量操作。</summary>
        private void SetBatchMode(bool on)
        {
            // 先清空选择再换回单选模式：WPF 在「单选项里还留着多项」时会抛异常
            if (!on && list.SelectedItems.Count > 0) list.SelectedItems.Clear();
            batchMode = on;
            normalActions.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
            batchActions.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            list.SelectionMode = on ? SelectionMode.Multiple : SelectionMode.Single;
            if (main.Library != null)
                foreach (Song song in main.Library) song.BatchMode = on;
            foreach (Song song in main.VisibleSongs) song.BatchMode = on;
            UpdateBatchCount();
        }

        /// <summary>全选 / 取消全选：已经全勾上了就再点一次全部取消。</summary>
        private void ToggleSelectAll()
        {
            if (list.Items.Count > 0 && list.SelectedItems.Count >= list.Items.Count) list.SelectedItems.Clear();
            else list.SelectAll();
            UpdateBatchCount();
        }

        private void UpdateBatchCount()
        {
            if (selCount == null) return;
            selCount.Text = "已选 " + list.SelectedItems.Count + " 首";
            if (selectAllButton != null)
            {
                bool all = list.Items.Count > 0 && list.SelectedItems.Count >= list.Items.Count;
                TextBlock label = null;
                StackPanel row = selectAllButton.Content as StackPanel;
                if (row != null && row.Children.Count > 1) label = row.Children[1] as TextBlock;
                if (label != null) label.Text = all ? "取消全选" : "全选";
            }
        }

        /// <summary>切页时退出批量编辑（音乐库和队列共用同一批 Song 对象）。</summary>
        public void ExitBatch()
        {
            if (batchMode) SetBatchMode(false);
        }

        /// <summary>截图 / 自检用：进入批量编辑并勾上前 N 首。</summary>
        public void BatchSelectForTest(int count)
        {
            SetBatchMode(true);
            List<Song> songs = main.VisibleSongs;
            for (int i = 0; i < count && i < songs.Count; i++) list.SelectedItems.Add(songs[i]);
            UpdateBatchCount();
        }

        /// <summary>
        /// 播放全部：按「当前播放模式」来播。
        /// 底部模式选随机 → 先把整张列表打乱一次再顺序播；其它模式按列表原顺序播。
        /// 播放顺序只有一个地方说了算，不会再出现两处随机互相打架。
        /// </summary>
        private void PlayAll()
        {
            List<Song> songs = new List<Song>(main.VisibleSongs);
            if (songs.Count == 0)
            {
                main.ShowToast("列表里还没有歌曲");
                return;
            }
            if (main.Settings.Mode == PlayMode.Shuffle) Shuffle(songs);
            main.PlayFrom(songs, 0);
        }

        /// <summary>把列表打乱一次（随机播放模式下「播放全部」用）。</summary>
        private static void Shuffle(List<Song> songs)
        {
            Random random = new Random();
            for (int i = songs.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Song tmp = songs[i];
                songs[i] = songs[j];
                songs[j] = tmp;
            }
        }

        private Button IconTextButton(string icon, string text, string styleKey, RoutedEventHandler click)
        {
            Button button = new Button();
            button.Style = (Style)Application.Current.Resources[styleKey];
            Canvas iconCanvas = Icons.Create(icon, 15, styleKey == "PrimaryButton" ? "OnAccent" : "TextDim");
            TextBlock label = Ui.Text(text, 13, styleKey == "PrimaryButton" ? "OnAccent" : "Text");
            label.Margin = new Thickness(7, 0, 0, 0);
            button.Content = Ui.Row(0, iconCanvas, label);
            button.Click += click;
            return button;
        }

        private static T FindAncestor<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null && !(source is T))
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as T;
        }

        /// <summary>从点击位置往上找带 Tag 的行内小按钮（下载图标等）。</summary>
        private static bool FindAction(DependencyObject source, string tag)
        {
            while (source != null)
            {
                FrameworkElement element = source as FrameworkElement;
                if (element != null && element.Tag != null && object.Equals(element.Tag, tag)) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                list.SelectAll();
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Enter) return;
            Song song = list.SelectedItem as Song;
            if (song != null) main.PlaySong(song);
        }

        /// <summary>当前选中的歌（Ctrl 点选、Shift 连选、Ctrl+A 全选）。</summary>
        public List<Song> GetSelectedSongs()
        {
            List<Song> result = new List<Song>();
            foreach (object entry in list.SelectedItems)
            {
                Song song = entry as Song;
                if (song != null) result.Add(song);
            }
            return result;
        }

        private static bool IsIn(List<Song> songs, Song song)
        {
            for (int i = 0; i < songs.Count; i++) if (songs[i] == song) return true;
            return false;
        }

        /// <summary>右键：点在已选中的行上保留整片选中，点在别处则只选中这一行。</summary>
        private void OnRightDown(object sender, MouseButtonEventArgs e)
        {
            ListBoxItem item = FindItem(e.OriginalSource as DependencyObject);
            if (item == null) return;
            menuSong = item.DataContext as Song;
            if (!item.IsSelected)
            {
                list.SelectedItems.Clear();
                item.IsSelected = true;
            }
        }

        private void OnContextMenu(object sender, ContextMenuEventArgs e)
        {
            List<Song> songs = GetSelectedSongs();
            if (songs.Count == 0 && menuSong == null)
            {
                e.Handled = true;
                return;
            }
            if (songs.Count <= 1)
            {
                Song song = songs.Count == 1 ? songs[0] : menuSong;
                list.ContextMenu = BuildMenu(song);
                return;
            }
            if (menuSong != null && !IsIn(songs, menuSong)) songs.Add(menuSong);
            list.ContextMenu = BuildMenu(songs, menuSong != null ? menuSong : songs[0]);
        }

        /// <summary>单首歌的右键菜单。</summary>
        private ContextMenu BuildMenu(Song song)
        {
            if (song == null) return null;
            List<Song> one = new List<Song>();
            one.Add(song);
            ContextMenu menu = new ContextMenu();
            menu.Items.Add(MenuItemFor("立即播放", delegate { main.PlaySong(song); }));
            menu.Items.Add(MenuItemFor("下一首播放", delegate { main.PlayNextInQueue(song); }));
            menu.Items.Add(MenuItemFor("添加到播放队列", delegate { main.Enqueue(song, false); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("下载…", delegate { main.DownloadSongs(one); }));
            menu.Items.Add(MenuItemFor("加入歌单…", delegate { main.AddSelectionToPlaylist(one); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("在资源管理器中显示", delegate { main.RevealInExplorer(song); }));
            if (main.CanDeleteCloud && song.IsCloud)
            {
                menu.Items.Add(MenuItemFor("从云盘删除…", delegate { main.DeleteSongs(one); }));
            }
            else
            {
                menu.Items.Add(MenuItemFor("从音乐库移除", delegate { main.DeleteSongs(one); }));
            }
            return menu;
        }

        /// <summary>多选时的右键菜单（下载 / 加入歌单 / 删除都对整片选中生效）。</summary>
        private ContextMenu BuildMenu(List<Song> songs, Song anchor)
        {
            ContextMenu menu = new ContextMenu();
            string count = songs.Count.ToString(CultureInfo.InvariantCulture);
            menu.Items.Add(MenuItemFor("播放选中的 " + count + " 首", delegate { main.PlayFrom(songs, 0); }));
            menu.Items.Add(MenuItemFor("添加到播放队列", delegate { AddAllToQueue(songs); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("下载选中的 " + count + " 首…", delegate { main.DownloadSongs(songs); }));
            menu.Items.Add(MenuItemFor("加入歌单…", delegate { main.AddSelectionToPlaylist(songs); }));
            menu.Items.Add(new Separator());
            bool cloud = false;
            for (int i = 0; i < songs.Count; i++) if (songs[i].IsCloud) cloud = true;
            if (main.CanDeleteCloud && cloud)
                menu.Items.Add(MenuItemFor("从云盘删除选中的 " + count + " 首…", delegate { main.DeleteSongs(songs); }));
            else
                menu.Items.Add(MenuItemFor("从音乐库移除选中的 " + count + " 首", delegate { main.DeleteSongs(songs); }));
            return menu;
        }

        private void AddAllToQueue(List<Song> songs)
        {
            foreach (Song song in songs) main.Enqueue(song, false);
            main.ShowToast("已把 " + songs.Count + " 首加到播放队列末尾");
        }

        private static MenuItem MenuItemFor(string text, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem();
            item.Header = text;
            item.Click += handler;
            return item;
        }

        private Song SongAt(DependencyObject source)
        {
            ListBoxItem item = FindItem(source);
            if (item == null) return null;
            return item.DataContext as Song;
        }

        private static ListBoxItem FindItem(DependencyObject source)
        {
            while (source != null && !(source is ListBoxItem))
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as ListBoxItem;
        }
    }
}
