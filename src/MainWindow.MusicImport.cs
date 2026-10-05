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

        public void ShowMusicSearch()
        {
            if (!IsCloudSource) { ShowToast("请先在设置里连接云盘"); return; }
            string endpoint = CloudEndpoint;
            string target = playlistFilter.Length > 0 ? PlaylistDir(playlistFilter)
                : PlaylistNames().Contains("默认歌单") ? PlaylistDir("默认歌单") : "/";
            StackPanel body = new StackPanel();
            ComboBox platform = new ComboBox { Width = 105, Height = 36, VerticalAlignment = VerticalAlignment.Center,
                ItemsSource = new string[] { "酷我音乐", "网易云音乐" }, SelectedIndex = 0 };
            TextBox keyword = new TextBox { Width = 300, Height = 40, FontSize = 14, Padding = new Thickness(8),
                ToolTip = "输入歌名或歌手", Style = (Style)Application.Current.Resources["InputBox"] };
            TextBlock state = Ui.Text("输入歌名或歌手，搜索后选择要入库的版本", 12, "TextMuted");
            state.TextWrapping = TextWrapping.Wrap; state.Margin = new Thickness(0, 10, 0, 10);
            StackPanel results = new StackPanel();
            ScrollViewer scroll = new ScrollViewer { Content = results, Height = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Button search = null, next = null;
            int page = 1;
            string activeKeyword = "", activePlatform = "";
            Action<int> load = delegate(int requestedPage)
            {
                if (musicSearching || uploading) { state.Text = "还有任务正在进行，请稍候"; return; }
                if (requestedPage == 1)
                {
                    activeKeyword = keyword.Text.Trim(); activePlatform = platform.SelectedIndex == 0 ? "kuwo" : "wyy";
                    if (activeKeyword.Length == 0) { state.Text = "请输入歌名或歌手"; return; }
                    results.Children.Clear();
                }
                string query = activeKeyword, source = activePlatform;
                musicSearching = true; search.IsEnabled = false; next.IsEnabled = false;
                state.Text = "正在搜索…首次使用请在独立音乐网站窗口完成验证，验证后重新搜索";
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
                        state.Text = songs.Length == 0 ? "没有更多结果，可换关键词或平台搜索" : "第 " + page + " 页 · 极高音质 MP3 320K + LRC";
                        foreach (object item in songs)
                        {
                            Dictionary<string, object> song = item as Dictionary<string, object>;
                            if (song == null) continue;
                            StackPanel row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                            TextBlock label = Ui.Text(MusicSource.Text(song, "name") + " - " + MusicSource.Text(song, "artist"), 14, "Text");
                            label.TextWrapping = TextWrapping.Wrap; row.Children.Add(label);
                            row.Children.Add(Ui.Text(MusicSource.Text(song, "album_name"), 11, "TextMuted"));
                            Button add = Ui.Button("下载并入库", "OutlineButton", delegate { ImportMusic(song, source, endpoint, target, state); });
                            add.HorizontalAlignment = HorizontalAlignment.Left;
                            try { MusicSource.Extreme(song); } catch { add.IsEnabled = false; add.Content = "无极高音质"; }
                            row.Children.Add(add); results.Children.Add(row);
                        }
                    });
                });
            };
            search = Ui.Button("搜索", "PrimaryButton", delegate { load(1); });
            next = Ui.Button("加载更多", "OutlineButton", delegate { load(page + 1); }); next.IsEnabled = false;
            keyword.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { load(1); e.Handled = true; } };
            body.Children.Add(Ui.Row(8, platform, keyword, search));
            body.Children.Add(state); body.Children.Add(scroll); body.Children.Add(next);
            ShowModal("在线找歌", "来源：flac.music.hi.cn · 上传到 " + (target == "/" ? "云盘根目录" : target)
                + "。成功后删除本次下载文件；失败时保留，可选同一首重试。", body,
                new List<ModalAction> { new ModalAction("关闭", "OutlineButton", null) }, 620);
        }

        private void ImportMusic(Dictionary<string, object> song, string platform, string endpoint, string target, TextBlock state)
        {
            if (uploading || musicSearching) { state.Text = "还有任务正在进行，请稍候"; return; }
            uploading = true;
            Action<string> report = delegate(string message)
            {
                Dispatcher.BeginInvoke((Action)delegate { state.Text = message; if (statusText != null) statusText.Text = message; });
            };
            ThreadPool.QueueUserWorkItem(delegate
            {
                string result, directory = null;
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
                }
                catch (Exception ex)
                { result = "入库未完成：" + ex.GetBaseException().Message + (directory == null ? "" : "。本地文件保留于 " + directory + "，选择同一首可重试"); }
                Dispatcher.BeginInvoke((Action)delegate
                {
                    uploading = false; state.Text = result; UpdateStatusText(); ShowToast(result); Rescan();
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
