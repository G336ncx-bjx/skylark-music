using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Skylark
{
    /// <summary>无界面自检：歌词解析、文件名解析、时长解析、配置读写、扫描、M3U。</summary>
    public static class SelfTest
    {
        private static int failures;
        private static readonly StringBuilder Log = new StringBuilder();

        public static int Run()
        {
            try
            {
                TestSongName();
                TestMusicImport();
                TestLrc();
                TestEncoding();
                TestSettingsRoundTrip();
                TestDuration();
                TestPlayback();
                TestScan();
                TestM3u();
            }
            catch (Exception ex)
            {
                failures++;
                Log.AppendLine("异常: " + ex);
            }

            string text = Log.ToString();
            Console.WriteLine(text);
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "skylark-selftest.log"), text, new UTF8Encoding(true));
            }
            catch (Exception)
            {
            }
            Console.WriteLine(failures == 0 ? "SELFTEST OK" : "SELFTEST FAILED: " + failures);
            return failures == 0 ? 0 : 1;
        }

        private static void Check(string name, bool ok, string detail)
        {
            if (!ok) failures++;
            Log.AppendLine((ok ? "[ok]   " : "[FAIL] ") + name + (detail == null ? "" : "  -> " + detail));
        }

        private static void TestSongName()
        {
            string title, artist, album;
            TextUtil.ParseSongName("冬眠 - 司南", out title, out artist, out album);
            Check("文件名解析：单歌手", title == "冬眠" && artist == "司南", title + " / " + artist);

            TextUtil.ParseSongName("City of Stars - Ryan Gosling、Emma Stone", out title, out artist, out album);
            List<string> artists = TextUtil.SplitArtists(artist);
            Check("文件名解析：多歌手", title == "City of Stars" && artists.Count == 2,
                title + " / " + artists.Count + " 位歌手");

            TextUtil.ParseSongName("只有歌名", out title, out artist, out album);
            Check("文件名解析：无歌手", title == "只有歌名" && artist.Length == 0, title + " / 空歌手");
        }

        private static void TestMusicImport()
        {
            Dictionary<string, object> song = new Dictionary<string, object> { { "name", "歌/曲" }, { "artist", "甲&乙" },
                { "minfo", new object[] { new Dictionary<string, object> { { "format", "flac" }, { "bitrate", 2000 } },
                    new Dictionary<string, object> { { "format", "mp3" }, { "bitrate", 320 } } } } };
            Check("在线入库：按极高音质选 MP3 320K", MusicSource.Text(MusicSource.Extreme(song), "bitrate") == "320", null);
            Check("在线入库：音频歌词共用安全文件名", MusicSource.FileName(song) == "歌_曲 - 甲&乙", null);
            song["minfo"] = new object[] { new Dictionary<string, object> { { "format", "mp3" }, { "bitrate", 128 } } };
            bool rejected = false;
            try { MusicSource.Extreme(song); } catch (IOException) { rejected = true; }
            Check("在线入库：不降级为普通音质", rejected, null);
            rejected = false;
            try { MusicSource.ValidateLyric("<html>验证页面</html>"); } catch (IOException) { rejected = true; }
            Check("在线入库：拒绝空歌词和网页", rejected, null);
            MusicSource.ValidateLyric("[00:01.00]一句歌词");
            string dir = Path.Combine(AppPaths.DataDir, "import-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string audio = Path.Combine(dir, "歌曲 - 歌手.mp3"), lyric = Path.Combine(dir, "歌曲 - 歌手.lrc");
            byte[] frames = new byte[3000];
            frames[0] = frames[1044] = 255; frames[1] = frames[1045] = 251; frames[2] = frames[1046] = 224;
            File.WriteAllBytes(audio, frames); MusicSource.ValidateMp3(audio);
            File.WriteAllText(lyric, "[00:01.00]歌词", new UTF8Encoding(false));
            Dictionary<string, long> cloud = new Dictionary<string, long>();
            Func<string, long?> size = delegate(string name) { return cloud.ContainsKey(name) ? cloud[name] : (long?)null; };
            int uploads = 0;
            Action<string> upload = delegate(string file) { uploads++; cloud[Path.GetFileName(file)] = new FileInfo(file).Length; };
            Action<string> report = delegate { };
            try
            {
                cloud[Path.GetFileName(lyric)] = 5;
                rejected = false;
                try { MusicSource.UploadPair(new string[] { audio, lyric }, size, upload, report); } catch (IOException) { rejected = true; }
                Check("在线入库：歌词撞名时不上传任何文件", rejected && uploads == 0 && File.Exists(audio) && File.Exists(lyric), null);
                cloud.Clear();
                rejected = false;
                try
                {
                    MusicSource.UploadPair(new string[] { audio, lyric }, size, delegate(string file)
                    { if (file == lyric) throw new IOException("模拟上传中断"); upload(file); }, report);
                }
                catch (IOException) { rejected = true; }
                Check("在线入库：失败保留两份本地文件和上传记录", rejected && File.Exists(audio) && File.Exists(lyric) && File.Exists(audio + ".uploaded"), null);
                MusicSource.UploadPair(new string[] { audio, lyric }, size, upload, report);
                Check("在线入库：重试跳过已上传音频", uploads == 2 && cloud.Count == 2, null);
                Check("在线入库：确认云端两份文件后清理本地", !File.Exists(audio) && !File.Exists(lyric) && Directory.GetFiles(dir).Length == 0, null);
                cloud.Clear(); File.WriteAllBytes(audio, frames); File.WriteAllText(lyric, "[00:01]歌词");
                rejected = false;
                try
                {
                    MusicSource.UploadPair(new string[] { audio, lyric }, size, delegate(string file)
                    { cloud[Path.GetFileName(file)] = new FileInfo(file).Length - 1; }, report);
                }
                catch (IOException) { rejected = true; }
                Check("在线入库：云端大小不一致时禁止删除本地", rejected && File.Exists(audio) && File.Exists(lyric), null);
                File.WriteAllText(audio, "<html>验证页面</html>"); rejected = false;
                try { MusicSource.ValidateMp3(audio); } catch (IOException) { rejected = true; }
                Check("在线入库：拒绝伪装成音频的验证页", rejected, null);
            }
            finally { Directory.Delete(dir, true); }
        }

        private static void TestLrc()
        {
            string text =
                "[ti:测试]\n[ar:歌手]\n[offset:+500]\n" +
                "[00:01.00]第一句\n" +
                "[00:05.50][01:05.50]重复段落\n" +
                "[00:10.20]原文\n[00:10.20]Translation\n" +
                "无时间标签的一行\n";
            LyricDocument doc = LrcParser.Parse(text);
            Check("歌词：解析行数", doc.Lines.Count == 5, doc.Lines.Count + " 行");
            Check("歌词：已同步", doc.Synced, "synced=" + doc.Synced);
            LyricLine first = null;
            foreach (LyricLine line in doc.Lines)
            {
                if (line.Text == "第一句") first = line;
            }
            Check("歌词：offset 生效", first != null && Math.Abs(first.Time - 1.5) < 0.001,
                first == null ? "未找到歌词行" : first.Time.ToString("0.000"));
            bool hasTranslation = false;
            string detail = "";
            foreach (LyricLine line in doc.Lines)
            {
                if (line.Translation == "Translation") hasTranslation = true;
                if (line.Text == "原文" || line.Text == "Translation")
                    detail += "[" + line.Text + "|" + line.Translation + "@" + line.Time.ToString("0.00") + "]";
            }
            Check("歌词：翻译行合并", hasTranslation, detail);

            // 双语歌词的另一种写法：译文紧跟原文，但时间戳被标成下一句的时间
            // （云盘上《Take Me Hand》《願い～あの頃のキミへ～》就是这种）
            LyricDocument shifted = LrcParser.Parse(
                "[00:59.99]In my dreams\n[01:01.66]我的梦里\n[01:01.66]I feel your light\n" +
                "[01:03.60]有你的光芒\n[01:03.60]I feel love is born again\n" +
                "[01:07.32]爱再次绽放\n[01:07.32]Fireflies\n");
            bool shiftedPaired = shifted.Lines.Count == 4 && shifted.Lines[0].Translation == "我的梦里"
                && shifted.Lines[0].Text == "In my dreams"
                && shifted.Lines[1].Translation == "有你的光芒"
                && shifted.Lines[1].Text == "I feel your light"
                && Math.Abs(shifted.Lines[1].Time - 61.66) < 0.01;
            string shiftedDetail = "";
            foreach (LyricLine line in shifted.Lines)
                shiftedDetail += "[" + line.Text + "|" + line.Translation + "@" + line.Time.ToString("0.00") + "]";
            Check("歌词：译文带错位时间戳也配对", shiftedPaired, shiftedDetail);

            // 真实场景：日文歌 + 中文译文，而且中文那侧多出「词：/曲：」信息行，
            // 按「谁多谁是原文」会判反，必须用 [ti:标题] 的语言来定
            LyricDocument jp = LrcParser.Parse(
                "[ti:願い～あの頃のキミへ～ (祈愿~致那个时候的你～)]\n" +
                "[00:00.24]願い～あの頃のキミへ～ - 當山みれい\n" +
                "[00:06.15]词：Dohzi-T\n" +
                "[00:14.94]二人の思い出 かき集めたなら\n" +
                "[00:20.63]回想起和你之间的回忆\n" +
                "[00:20.63]また泣けてきちゃう 寂しさ溢れて\n" +
                "[00:26.34]又会令我落泪 令我感到寂寞\n" +
                "[00:26.34]最後の恋だと 信じて願った\n");
            bool jpOk = false;
            string jpDetail = "";
            foreach (LyricLine line in jp.Lines)
            {
                if (line.Text == "二人の思い出 かき集めたなら"
                    && line.Translation == "回想起和你之间的回忆") jpOk = true;
                jpDetail += "[" + line.Text + "|" + line.Translation + "]";
            }
            Check("歌词：日文歌不会把中文译文当原文", jpOk, jpDetail);

            // 《summertime》：标题是英文、歌词是日文，文件开头还有「歌名 - 歌手」行和全角空格占位行。
            // 以前这两行会被当成「原文」，整首歌错开一行（表现为「上一句译文配下一句原文」）。
            LyricDocument mixedTitle = LrcParser.Parse(
                "[ml:1.0]\n[ti:summertime]\n[ar:cinnamons]\n" +
                "[00:00.00]summertime - cinnamons\n" +
                "[00:01.11]　\n" +
                "[00:01.38]君の虜になってしまえばきっと\n" +
                "[00:01.38]如果能成为你的俘虏\n" +
                "[00:05.46]この夏は充実するのもっと\n" +
                "[00:05.46]这个夏天一定会更加充实\n");
            bool mixedOk = mixedTitle.Lines.Count == 2
                && mixedTitle.Lines[0].Text == "君の虜になってしまえばきっと"
                && mixedTitle.Lines[0].Translation == "如果能成为你的俘虏"
                && mixedTitle.Lines[1].Text == "この夏は充実するのもっと"
                && mixedTitle.Lines[1].Translation == "这个夏天一定会更加充实";
            string mixedDetail = "";
            foreach (LyricLine line in mixedTitle.Lines)
                mixedDetail += "[" + line.Text + "|" + line.Translation + "]";
            Check("歌词：英文标题的日文歌 + 标题行/空行不占位", mixedOk, mixedDetail);

            // 标准排版里「磊々落々反戦国家」这种纯汉字日文原句，不能因为「没假名」被当成译文
            LyricDocument kanji = LrcParser.Parse(
                "[ti:千本桜 (千本樱)]\n[ar:初音ミク (初音未来)]\n" +
                "[00:00.00]千本桜 (千本樱) - 初音ミク (初音未来)\n" +
                "[00:32.11]大胆不敵にハイカラ革命\n[00:32.11]英勇无畏 维新革命\n" +
                "[00:34.98]磊々落々反戦国家\n[00:34.98]光明磊落反战国家\n");
            bool kanjiOk = kanji.Lines.Count == 2
                && kanji.Lines[0].Translation == "英勇无畏 维新革命"
                && kanji.Lines[1].Text == "磊々落々反戦国家"
                && kanji.Lines[1].Translation == "光明磊落反战国家";
            string kanjiDetail = "";
            foreach (LyricLine line in kanji.Lines)
                kanjiDetail += "[" + line.Text + "|" + line.Translation + "]";
            Check("歌词：纯汉字日文原句仍算原文", kanjiOk, kanjiDetail);

            // 旧排版（译文时间戳被标成下一句）里碰上纯汉字原句
            LyricDocument kanjiShifted = LrcParser.Parse(
                "[ti:インドア系ならトラックメイカー (内向都是作曲家)]\n" +
                "[00:41.69]納期は明日だ\n[00:42.68]交稿期限是明天\n" +
                "[00:42.68]絶対徹夜\n[00:43.59]绝对要熬夜了\n" +
                "[00:43.59]エビデイ\n[00:44.04]Every day\n");
            bool kanjiShiftedOk = kanjiShifted.Lines.Count == 3
                && kanjiShifted.Lines[0].Text == "納期は明日だ"
                && kanjiShifted.Lines[0].Translation == "交稿期限是明天"
                && kanjiShifted.Lines[1].Text == "絶対徹夜"
                && kanjiShifted.Lines[1].Translation == "绝对要熬夜了"
                && kanjiShifted.Lines[2].Text == "エビデイ"
                && kanjiShifted.Lines[2].Translation == "Every day";
            string shiftedKanjiDetail = "";
            foreach (LyricLine line in kanjiShifted.Lines)
                shiftedKanjiDetail += "[" + line.Text + "|" + line.Translation + "]";
            Check("歌词：旧排版里的纯汉字原句", kanjiShiftedOk, shiftedKanjiDetail);

            LyricDocument plain = LrcParser.Parse("第一行\n第二行\n");
            Check("歌词：纯文本歌词", !plain.Synced && plain.Lines.Count == 2, plain.Lines.Count + " 行");

            // 中文歌里夹一句英文副歌：整篇没有重复时间戳 → 不该把它当成上一句的译文
            LyricDocument hook = LrcParser.Parse(
                "[ti:星辰大海]\n" +
                "[00:15.49]我愿变成一颗恒星\n" +
                "[00:21.34]守护海底的蜂鸣\n" +
                "[00:26.71]It's my dream it's magic\n" +
                "[00:29.55]照亮你的心\n");
            bool hookOk = hook.Lines.Count == 4
                && hook.Lines[1].Text == "守护海底的蜂鸣"
                && string.IsNullOrEmpty(hook.Lines[1].Translation)
                && hook.Lines[2].Text == "It's my dream it's magic"
                && string.IsNullOrEmpty(hook.Lines[2].Translation);
            string hookDetail = "";
            foreach (LyricLine line in hook.Lines)
                hookDetail += "[" + line.Text + "|" + line.Translation + "]";
            Check("歌词：中文歌里的英文副歌不算译文", hookOk, hookDetail);

            // 用真实的音乐目录做一次批量解析
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (!Directory.Exists(dir)) return;
            string[] files = Directory.GetFiles(dir, "*.lrc");
            int ok = 0;
            string firstProblem = null;
            foreach (string file in files)
            {
                LyricDocument real = LrcParser.Load(file);
                if (real.Found && real.Synced && real.Lines.Count > 0) ok++;
                else if (firstProblem == null) firstProblem = Path.GetFileName(file);
            }
            Check("歌词：真实文件批量解析", files.Length == 0 || ok == files.Length,
                ok + "/" + files.Length + " 个文件解析成功" + (firstProblem == null ? "" : "，第一个失败：" + firstProblem));
        }

        private static void TestEncoding()
        {
            string file = Path.Combine(Path.GetTempPath(), "skylark-gbk.lrc");
            string content = "[00:01.00]中文歌词测试";
            try
            {
                File.WriteAllBytes(file, Encoding.GetEncoding(936).GetBytes(content));
                string read = TextUtil.ReadAllTextSmart(file);
                Check("编码：GBK 歌词读取", read.Contains("中文歌词测试"), read.Trim());
            }
            catch (Exception ex)
            {
                Check("编码：GBK 歌词读取", false, ex.Message);
            }
        }

        private static void TestSettingsRoundTrip()
        {
            AppSettings settings = new AppSettings();
            settings.Volume = 0.42;
            settings.MusicDir = "D:\\音乐\\music";
            settings.Queue = new List<string>(new string[] { "a.mp3", "b.mp3" });
            settings.Mode = PlayMode.Shuffle;
            settings.Durations.Add(new DurationEntry());

            string file = Path.Combine(Path.GetTempPath(), "skylark-settings.json");
            using (FileStream fs = new FileStream(file, FileMode.Create))
            {
                System.Runtime.Serialization.Json.DataContractJsonSerializer ser =
                    new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppSettings));
                ser.WriteObject(fs, settings);
            }
            AppSettings loaded;
            using (FileStream fs = new FileStream(file, FileMode.Open))
            {
                System.Runtime.Serialization.Json.DataContractJsonSerializer ser =
                    new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppSettings));
                loaded = (AppSettings)ser.ReadObject(fs);
            }
            Check("配置：JSON 往返", Math.Abs(loaded.Volume - 0.42) < 0.0001
                && loaded.Mode == PlayMode.Shuffle && loaded.Queue.Count == 2, "音量 " + loaded.Volume);
        }

        private static void TestDuration()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            string[] files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.mp3") : new string[0];
            if (files.Length == 0)
            {
                Check("时长：MP3 解析", true, "跳过（没有测试文件）");
                return;
            }

            int limit = Math.Min(files.Length, 6);
            int parsedOk = 0;
            int matchOk = 0;
            int probed = 0;
            StringBuilder detail = new StringBuilder();
            for (int i = 0; i < limit; i++)
            {
                double parsed = DurationReader.Read(files[i]);
                if (parsed > 10 && parsed < 3600) parsedOk++;
                double actual = ProbeDurationWithMediaPlayer(files[i]);
                if (actual > 0)
                {
                    probed++;
                    if (Math.Abs(actual - parsed) < 1.5) matchOk++;
                    else detail.Append(Path.GetFileName(files[i]) + " 解析 " + parsed.ToString("0.0")
                        + " / 解码 " + actual.ToString("0.0") + "; ");
                }
            }
            Check("时长：MP3 解析", parsedOk == limit, parsedOk + "/" + limit + " 个文件通过");
            Check("时长：与解码器一致", probed == 0 || matchOk == probed,
                matchOk + "/" + probed + " 一致 " + detail);
        }

        private static double ProbeDurationWithMediaPlayer(string path)
        {
            MediaPlayer player = new MediaPlayer();
            double result = 0;
            bool done = false;
            player.MediaOpened += delegate
            {
                if (player.NaturalDuration.HasTimeSpan) result = player.NaturalDuration.TimeSpan.TotalSeconds;
                done = true;
            };
            player.MediaFailed += delegate { done = true; };
            try
            {
                player.Open(new Uri(path));
            }
            catch (Exception)
            {
                return 0;
            }

            DispatcherFrame frame = new DispatcherFrame();
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(150);
            int ticks = 0;
            timer.Tick += delegate
            {
                ticks++;
                if (done || ticks > 40)
                {
                    timer.Stop();
                    frame.Continue = false;
                }
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
            player.Close();
            return result;
        }

        /// <summary>真实调用播放内核：打开文件、播放、跳转。</summary>
        private static void TestPlayback()
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            string[] files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.mp3") : new string[0];
            if (files.Length == 0)
            {
                Check("播放：打开并播放", true, "跳过（没有测试文件）");
                return;
            }

            PlayerEngine engine = new PlayerEngine();
            string error = null;
            int opened = 0;
            engine.Opened += delegate { opened++; };
            engine.Failed += delegate(object sender, EventArgs args)
            {
                PlayerErrorArgs info = args as PlayerErrorArgs;
                error = info == null ? "未知错误" : info.Message;
            };

            Song song = new Song();
            song.Path = files[0];
            song.Title = Path.GetFileNameWithoutExtension(files[0]);
            song.Duration = DurationReader.Read(files[0]);
            engine.Volume = 0;
            engine.Open(song, true, 0);
            Pump(2.5);

            double position = engine.GetPosition();
            Check("播放：打开并开始播放", opened > 0 && engine.IsPlaying && error == null,
                "opened=" + opened + " playing=" + engine.IsPlaying + " pos=" + position.ToString("0.00") + "s"
                + (error == null ? "" : " err=" + error));
            Check("播放：进度推进", position > 0.5, position.ToString("0.00") + " s");

            engine.Seek(30);
            Pump(0.6);
            double afterSeek = engine.GetPosition();
            Check("播放：跳转", Math.Abs(afterSeek - 30) < 4, afterSeek.ToString("0.0") + " s");

            engine.Pause();
            Pump(0.3);
            double paused = engine.GetPosition();
            Pump(0.6);
            double stillPaused = engine.GetPosition();
            Check("播放：暂停后进度不动", Math.Abs(paused - stillPaused) < 0.2,
                paused.ToString("0.00") + " / " + stillPaused.ToString("0.00"));
            engine.Close();
        }

        /// <summary>推动 WPF 消息循环若干秒，让 MediaPlayer 的异步事件得以送达。</summary>
        private static void Pump(double seconds)
        {
            DispatcherFrame frame = new DispatcherFrame();
            DispatcherTimer timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(seconds);
            timer.Tick += delegate
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void TestScan()
        {
            string dir = Path.Combine(Path.GetTempPath(), "skylark-scan");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "sub"));

            // 扫描只看文件名与文件头，这里用内容无关的占位文件即可，保证测试不依赖本机音乐库
            File.WriteAllText(Path.Combine(dir, "测试歌曲 - 甲、乙.mp3"), "dummy");
            File.WriteAllText(Path.Combine(dir, "sub", "子目录歌曲 - 丙.mp3"), "dummy");
            File.WriteAllText(Path.Combine(dir, "测试歌曲 - 甲、乙.lrc"),
                "[00:01.00]测试歌词", new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(dir, "不是音乐.txt"), "dummy");

            ScanResult flat = LibraryScanner.Scan(dir, false, null, null);
            ScanResult deep = LibraryScanner.Scan(dir, true, null, null);
            Check("扫描：非递归", flat.Songs.Count == 1, flat.Songs.Count + " 首");
            Check("扫描：递归子目录", deep.Songs.Count == 2, deep.Songs.Count + " 首");

            // 格式白名单：自带的能放 flac / aiff，这些都要能扫进来；
            // 完全不支持的（比如 .mpc）跳过并计数
            string fmt = Path.Combine(Path.GetTempPath(), "skylark-format");
            if (Directory.Exists(fmt)) Directory.Delete(fmt, true);
            Directory.CreateDirectory(fmt);
            File.WriteAllText(Path.Combine(fmt, "无损 - 甲.flac"), "dummy");
            File.WriteAllText(Path.Combine(fmt, "苹果 - 乙.aiff"), "dummy");
            File.WriteAllText(Path.Combine(fmt, "网络 - 丙.ogg"), "dummy");
            File.WriteAllText(Path.Combine(fmt, "冷门 - 丁.mpc"), "dummy");
            ScanResult formats = LibraryScanner.Scan(fmt, false, null, null);
            Check("扫描：flac / aiff / ogg 都进库", formats.Songs.Count == 3, formats.Songs.Count + " 首");
            Check("扫描：不支持的格式计入忽略", formats.SkippedUnsupported == 1,
                formats.SkippedUnsupported + " 个");
            if (flat.Songs.Count > 0)
            {
                Check("扫描：关联歌词", flat.Songs[0].HasLyrics, flat.Songs[0].LyricPath);
                Check("扫描：歌手拆分", flat.Songs[0].Artists.Count == 2, flat.Songs[0].Artist);
            }

            List<DurationEntry> cache = flat.Cache;
            ScanResult again = LibraryScanner.Scan(dir, false, cache, null);
            Check("扫描：缓存复用", again.Songs.Count == 1 && Math.Abs(again.Songs[0].Duration - flat.Songs[0].Duration) < 0.001,
                "缓存 " + again.Cache.Count + " 条");

            List<string> hidden = new List<string>();
            hidden.Add(Path.Combine(dir, "测试歌曲 - 甲、乙.mp3"));
            ScanResult hiddenScan = LibraryScanner.Scan(dir, false, null, hidden);
            Check("扫描：忽略已移除歌曲", hiddenScan.Songs.Count == 0, hiddenScan.Songs.Count + " 首");
        }

        private static void TestM3u()
        {
            string file = Path.Combine(Path.GetTempPath(), "skylark-playlist.m3u");
            string audio = Path.Combine(Path.GetTempPath(), "skylark-m3u-test.mp3");
            File.WriteAllText(audio, "x");
            List<Song> songs = new List<Song>();
            Song song = new Song();
            song.Path = audio;
            song.Title = "测试";
            song.Artist = "歌手";
            songs.Add(song);
            M3u.Save(file, songs);
            List<string> loaded = M3u.Load(file);
            Check("播放列表：M3U 往返", loaded.Count == 1 && loaded[0] == audio, loaded.Count + " 项");
        }
    }
}
