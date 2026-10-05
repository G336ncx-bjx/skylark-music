using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Skylark
{
    public partial class MainWindow
    {
        private readonly MusicBrowser musicBrowser = new MusicBrowser();
        private bool musicSearching;
        private TextBox musicSearchInput;
        private StackPanel musicSearchRows;
        private TextBlock musicSearchMessage;

        public void ShowMusicSearch()
        {
            if (!IsCloudSource) { ShowToast("请先在设置里连接云盘"); return; }
            string endpoint = CloudEndpoint;
            string target = playlistFilter.Length > 0 ? PlaylistDir(playlistFilter)
                : PlaylistNames().Contains("默认歌单") ? PlaylistDir("默认歌单") : "/";
            StackPanel body = new StackPanel();
            RadioButton kuwo = new RadioButton { Content = "酷我音乐", GroupName = "MusicSearchPlatform", IsChecked = true,
                Style = (Style)Application.Current.Resources["Segment"] };
            RadioButton wyy = new RadioButton { Content = "网易云音乐", GroupName = "MusicSearchPlatform",
                Style = (Style)Application.Current.Resources["Segment"] };
            Border sources = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(4), Child = Ui.Row(2, kuwo, wyy) };
            Ui.Bind(sources, Border.BackgroundProperty, "Card");
            Grid sourceRow = Ui.Columns(Ui.Stars(1), GridLength.Auto);
            sourceRow.Margin = new Thickness(0, 0, 0, 12);
            sourceRow.Children.Add(sources); sources.HorizontalAlignment = HorizontalAlignment.Left;
            TextBlock quality = Ui.Text("极高音质 320K  ·  LRC 歌词", 11.5, "TextMuted");
            quality.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(quality, 1); sourceRow.Children.Add(quality);

            // 输入框自身不加固定高度或内边距，避免 TextBox 与模板重复计算 Padding 而裁掉文字。
            TextBox keyword = new TextBox { FontSize = 14, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "输入歌名或歌手",
                Style = (Style)Application.Current.Resources["SearchBox"] };
            Grid inputContent = Ui.Columns(Ui.Px(28), Ui.Stars(1));
            inputContent.Margin = new Thickness(12, 0, 12, 0);
            FrameworkElement searchIcon = Icons.Create("search", 16, "TextMuted");
            searchIcon.VerticalAlignment = VerticalAlignment.Center; inputContent.Children.Add(searchIcon);
            Grid inputHost = new Grid(); Grid.SetColumn(inputHost, 1); inputContent.Children.Add(inputHost);
            TextBlock placeholder = Ui.Text("搜索歌名、歌手", 14, "TextMuted");
            placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.IsHitTestVisible = false;
            inputHost.Children.Add(placeholder); inputHost.Children.Add(keyword);
            keyword.TextChanged += delegate { placeholder.Visibility = keyword.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            Border inputBorder = new Border { Height = 44, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = inputContent };
            Ui.Bind(inputBorder, Border.BackgroundProperty, "Card"); Ui.Bind(inputBorder, Border.BorderBrushProperty, "Border");
            keyword.GotKeyboardFocus += delegate { Ui.Bind(inputBorder, Border.BorderBrushProperty, "Accent"); };
            keyword.LostKeyboardFocus += delegate { Ui.Bind(inputBorder, Border.BorderBrushProperty, "Border"); };
            TextBlock state = Ui.Text("输入关键词，选择喜欢的歌曲版本", 12, "TextMuted");
            state.TextWrapping = TextWrapping.Wrap; state.MaxHeight = 48; state.Margin = new Thickness(0, 12, 0, 12);
            StackPanel results = new StackPanel();
            double viewportHeight = ActualHeight > 0 ? ActualHeight : Height;
            ScrollViewer scroll = new ScrollViewer { Content = results, Height = Math.Max(140, Math.Min(280, viewportHeight - 370)),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            musicSearchInput = keyword; musicSearchRows = results; musicSearchMessage = state;
            Button search = null, next = null;
            int page = 1;
            string activeKeyword = "", activePlatform = "";
            Action<int> load = delegate(int requestedPage)
            {
                if (musicSearching || uploading) { state.Text = "还有任务正在进行，请稍候"; return; }
                if (requestedPage == 1)
                {
                    activeKeyword = keyword.Text.Trim(); activePlatform = kuwo.IsChecked == true ? "kuwo" : "wyy";
                    if (activeKeyword.Length == 0) { state.Text = "请输入歌名或歌手"; return; }
                    results.Children.Clear();
                }
                string query = activeKeyword, source = activePlatform;
                musicSearching = true; search.IsEnabled = false; next.IsEnabled = false;
                state.Text = "正在搜索…如出现网站验证，请在音乐网站窗口完成后重试";
                ThreadPool.QueueUserWorkItem(delegate
                {
                    object[] songs = null; string error = null;
                    try
                    {
                        Dictionary<string, object> data = MusicSource.Object(musicBrowser.Call("search", new Dictionary<string, object> {
                            { "platform", source }, { "keyword", query }, { "page", requestedPage }, { "size", 20 } }));
                        object value; songs = data.TryGetValue("list", out value) ? value as object[] : null;
                        if (songs == null) throw new IOException("网站没有返回歌曲列表");
                    }
                    catch (Exception ex) { error = ex.GetBaseException().Message; }
                    Dispatcher.BeginInvoke((Action)delegate
                    {
                        musicSearching = false; search.IsEnabled = true;
                        if (error != null) { state.Text = error; next.IsEnabled = requestedPage > 1; return; }
                        page = requestedPage; next.IsEnabled = songs.Length == 20;
                        state.Text = songs.Length == 0 ? "没有更多结果，可换关键词或平台搜索" : "已找到 " + (results.Children.Count + songs.Length) + " 首  ·  第 " + page + " 页";
                        foreach (object item in songs)
                        {
                            Dictionary<string, object> song = item as Dictionary<string, object>;
                            if (song == null) continue;
                            results.Children.Add(BuildMusicResultRow(song, source, endpoint, target, state));
                        }
                    });
                });
            };
            search = Ui.Button("搜索", "PrimaryButton", delegate { load(1); });
            search.Height = 44; search.MinWidth = 80; search.Content = Ui.Text("搜索", 13, "OnAccent", FontWeights.SemiBold);
            next = Ui.Button("加载更多", "OutlineButton", delegate { load(page + 1); }); next.IsEnabled = false;
            next.HorizontalAlignment = HorizontalAlignment.Center; next.MinWidth = 160; next.Margin = new Thickness(0, 10, 0, 0);
            keyword.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { load(1); e.Handled = true; } };
            Grid searchRow = Ui.Columns(Ui.Stars(1), GridLength.Auto);
            searchRow.Children.Add(inputBorder); Grid.SetColumn(search, 1); search.Margin = new Thickness(10, 0, 0, 0); searchRow.Children.Add(search);
            body.Children.Add(sourceRow); body.Children.Add(searchRow);
            body.Children.Add(state); body.Children.Add(scroll); body.Children.Add(next);
            TextBlock origin = Ui.Text("音乐与歌词来自 flac.music.hi.cn", 11, "TextMuted");
            origin.HorizontalAlignment = HorizontalAlignment.Center; origin.Margin = new Thickness(0, 10, 0, 0); body.Children.Add(origin);
            ShowModal("在线找歌", "上传到「" + (target == "/" ? "云盘根目录" : target.Trim('/')) + "」 · 入库成功后自动清理下载文件", body,
                new List<ModalAction> { new ModalAction("关闭", "OutlineButton", null) }, Math.Min(720, (ActualWidth > 0 ? ActualWidth : Width) - 64));
        }

        private FrameworkElement BuildMusicResultRow(Dictionary<string, object> song, string source, string endpoint, string target, TextBlock state)
        {
            Grid grid = Ui.Columns(Ui.Px(48), Ui.Stars(1), GridLength.Auto);
            Border icon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center, Child = Icons.Create("music", 17, "Accent") };
            Ui.Bind(icon, Border.BackgroundProperty, "AccentSoft"); grid.Children.Add(icon);
            StackPanel details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            string rawTitle = MusicSource.Text(song, "name"), artist = MusicSource.Text(song, "artist"), album = MusicSource.Text(song, "album_name");
            TextBlock title = Ui.Text(MusicSource.CleanTitle(rawTitle), 14, "Text", FontWeights.SemiBold);
            title.ToolTip = rawTitle; details.Children.Add(title);
            TextBlock subtitle = Ui.Text(artist + (album.Length > 0 ? "  ·  " + album : ""), 12, "TextDim");
            subtitle.ToolTip = subtitle.Text; subtitle.Margin = new Thickness(0, 5, 0, 0); details.Children.Add(subtitle);
            Grid.SetColumn(details, 1); grid.Children.Add(details);
            Button add = null;
            add = Ui.Button("入库", "OutlineButton", delegate
            {
                if (uploading || musicSearching) { state.Text = "还有任务正在进行，请稍候"; return; }
                add.IsEnabled = false; add.Content = Ui.Text("处理中", 12, "Text");
                ImportMusic(song, source, endpoint, target, state, delegate(bool success)
                { add.IsEnabled = !success; add.Content = Ui.Text(success ? "已入库" : "重试", 12, success ? "TextMuted" : "Text"); });
            });
            add.Width = 88; add.Height = 34; add.Padding = new Thickness(10, 5, 10, 5); add.VerticalAlignment = VerticalAlignment.Center;
            add.ToolTip = "下载极高音质与歌词，自动上传到当前歌单";
            try { MusicSource.Extreme(song); } catch { add.IsEnabled = false; add.Content = Ui.Text("无 320K", 11, "TextMuted"); }
            Grid.SetColumn(add, 2); grid.Children.Add(add);
            Border row = new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 6, 8), Child = grid };
            Ui.Bind(row, Border.BackgroundProperty, "Card");
            row.MouseEnter += delegate { Ui.Bind(row, Border.BackgroundProperty, "Hover"); };
            row.MouseLeave += delegate { Ui.Bind(row, Border.BackgroundProperty, "Card"); };
            return row;
        }

        internal void PopulateMusicSearchForShot()
        {
            musicSearchInput.Text = "樱花草";
            string[] titles = { "樱花草-《米可，GO！》电视剧主题曲_《星苹果乐园》电视剧插曲", "樱花草（DJ氛围版）", "樱花草（恋人手中樱花草）", "樱花草", "樱花草 (Live)" };
            string[] artists = { "Sweety", "Sixteen&AaHen", "黑豆ado", "黄子弘凡&姚晓棠", "司南" };
            string[] albums = { "花言乔语（精装版）", "樱花草（DJ氛围版）", "樱花草（恋人手中樱花草）", "天赐的声音第七季 第1期", "现场音乐会" };
            for (int i = 0; i < titles.Length; i++)
            {
                Dictionary<string, object> song = new Dictionary<string, object> { { "name", titles[i] }, { "artist", artists[i] }, { "album_name", albums[i] },
                    { "minfo", new object[] { new Dictionary<string, object> { { "format", "mp3" }, { "bitrate", i == 4 ? 128 : 320 } } } } };
                musicSearchRows.Children.Add(BuildMusicResultRow(song, "kuwo", "https://cloud.tsinghua.edu.cn/d/demo/", "/默认歌单", musicSearchMessage));
            }
            musicSearchMessage.Text = "已找到 20 首  ·  第 1 页";
        }

        private void ImportMusic(Dictionary<string, object> song, string platform, string endpoint, string target, TextBlock state, Action<bool> finished)
        {
            if (uploading || musicSearching) { state.Text = "还有任务正在进行，请稍候"; return; }
            uploading = true;
            Action<string> report = delegate(string message)
            {
                Dispatcher.BeginInvoke((Action)delegate { state.Text = message; if (statusText != null) statusText.Text = message; });
            };
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result, directory = null; bool completed = false;
                try
                {
                    string name = MusicSource.FileName(song);
                    string key;
                    using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                        key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(endpoint + "\n" + target + "\n" + platform + "\n" + MusicSource.Text(song, "id")))).Replace("-", "");
                    directory = Path.Combine(AppPaths.DataDir, "music-import", key);
                    Directory.CreateDirectory(directory);
                    string audio = Path.Combine(directory, name + ".mp3"), lyric = Path.Combine(directory, name + ".lrc");
                    Dictionary<string, object> fields = new Dictionary<string, object> { { "platform", platform }, { "songid", MusicSource.Text(song, "id") },
                        { "time", MusicSource.Text(song, "time") }, { "sign", MusicSource.Text(song, "sign") } };
                    Dictionary<string, object> quality = MusicSource.Extreme(song);
                    if (!File.Exists(lyric))
                    {
                        report("正在获取歌词…");
                        string text = Convert.ToString(musicBrowser.Call("getLyric", fields));
                        MusicSource.ValidateLyric(text); File.WriteAllText(lyric, text, new UTF8Encoding(false));
                    }
                    MusicSource.ValidateLyric(File.ReadAllText(lyric));
                    if (!File.Exists(audio))
                    {
                        report("正在解析极高音质…"); fields["format"] = "mp3"; fields["bitrate"] = MusicSource.Text(quality, "bitrate");
                        string url = MusicSource.Text(MusicSource.Object(musicBrowser.Call("getUrl", fields)), "url");
                        int last = -1;
                        MusicSource.Download(url, audio, delegate(long done, long total)
                        {
                            int percent = total > 0 ? (int)(done * 100 / total) : 0;
                            if (percent != last) { last = percent; report("正在下载极高音质：" + percent + "%"); }
                        });
                    }
                    MusicSource.ValidateMp3(audio);
                    report("正在检查云盘目标目录…"); CloudClient.EnsureDir(endpoint, target);
                    MusicSource.UploadPair(new string[] { audio, lyric }, delegate(string fileName)
                    {
                        CloudEntry existing = FindImportFile(endpoint, target, fileName);
                        return existing == null ? (long?)null : existing.Size;
                    }, delegate(string file)
                    {
                        string fileName = Path.GetFileName(file);
                        report("正在上传：" + fileName);
                        CloudClient.UploadNew(endpoint, file, target, delegate(long done, long total)
                        { report("正在上传：" + fileName + " " + (total > 0 ? done * 100 / total : 0) + "%"); });
                    }, report);
                    Directory.Delete(directory);
                    result = "已入库「" + name + "」，本次下载文件已删除";
                    completed = true;
                }
                catch (Exception ex)
                { result = "入库未完成：" + ex.GetBaseException().Message + (directory == null ? "" : "。本地文件保留于 " + directory + "，选择同一首可重试"); }
                Dispatcher.BeginInvoke((Action)delegate
                {
                    uploading = false; state.Text = result; UpdateStatusText(); ShowToast(result); Rescan();
                    finished(completed);
                });
            });
        }

        private static CloudEntry FindImportFile(string endpoint, string target, string name)
        {
            foreach (CloudEntry entry in CloudClient.List(endpoint, target))
                if (!entry.IsDirectory && string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }
    }
}
