using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Skylark
{
    // 所有网站 API 都在独立的正常浏览器会话里运行，保留网站自身的验证流程。
    public sealed class MusicBrowser : IDisposable
    {
        public const string Site = "https://flac.music.hi.cn/";
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private int port;
        private string socketUrl;

        public void Open()
        {
            if (socketUrl != null)
            {
                try { Evaluate("location.origin"); return; } catch { socketUrl = null; }
            }
            string browser = null;
            foreach (string root in new string[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
            {
                string candidate = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
                if (File.Exists(candidate)) { browser = candidate; break; }
            }
            if (browser == null) throw new InvalidOperationException("在线找歌需要 Microsoft Edge，请先安装 Edge");
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            string profile = Path.Combine(AppPaths.DataDir, "music-browser");
            Process.Start(new ProcessStartInfo(browser, "--remote-debugging-address=127.0.0.1 --remote-debugging-port=" + port
                + " --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check --app=" + Site) { UseShellExecute = false });
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/json");
                    req.Proxy = null; req.Timeout = 1000;
                    using (WebResponse response = req.GetResponse())
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        foreach (object item in (object[])json.DeserializeObject(reader.ReadToEnd()))
                        {
                            Dictionary<string, object> page = MusicSource.Object(item);
                            Uri uri;
                            if (MusicSource.Text(page, "type") == "page" && Uri.TryCreate(MusicSource.Text(page, "url"), UriKind.Absolute, out uri)
                                && uri.Scheme == "https" && uri.Host == "flac.music.hi.cn")
                            { socketUrl = MusicSource.Text(page, "webSocketDebuggerUrl"); return; }
                        }
                    }
                }
                catch (Exception) { }
                Thread.Sleep(200);
            }
            throw new InvalidOperationException("音乐网站窗口未就绪，请关闭该独立窗口后重试");
        }

        public string Evaluate(string expression)
        {
            using (ClientWebSocket ws = new ClientWebSocket())
            using (CancellationTokenSource timeout = new CancellationTokenSource(65000))
            {
                ws.ConnectAsync(new Uri(socketUrl), timeout.Token).GetAwaiter().GetResult();
                byte[] command = Encoding.UTF8.GetBytes(json.Serialize(new { id = 1, method = "Runtime.evaluate",
                    @params = new { expression = expression, awaitPromise = true, returnByValue = true } }));
                ws.SendAsync(new ArraySegment<byte>(command), WebSocketMessageType.Text, true, timeout.Token).GetAwaiter().GetResult();
                while (true)
                {
                    using (MemoryStream message = new MemoryStream())
                    {
                        WebSocketReceiveResult part;
                        byte[] buffer = new byte[32768];
                        do
                        {
                            part = ws.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token).GetAwaiter().GetResult();
                            if (part.MessageType == WebSocketMessageType.Close) throw new IOException("音乐网站窗口已关闭，请重新打开在线找歌");
                            message.Write(buffer, 0, part.Count);
                            if (message.Length > 8 * 1024 * 1024) throw new IOException("网站响应过大");
                        } while (!part.EndOfMessage);
                        Dictionary<string, object> reply = MusicSource.Object(json.DeserializeObject(Encoding.UTF8.GetString(message.ToArray())));
                        if (!reply.ContainsKey("id")) continue;
                        if (reply.ContainsKey("error")) throw new IOException("浏览器接口出错，请重试");
                        Dictionary<string, object> result = MusicSource.Object(reply["result"]);
                        if (result.ContainsKey("exceptionDetails")) throw new IOException("请在音乐网站窗口完成验证，再重新搜索");
                        return MusicSource.Text(MusicSource.Object(result["result"]), "value");
                    }
                }
            }
        }

        public object Call(string action, Dictionary<string, object> fields)
        {
            Open();
            // 正常页面首次加载 / 验证后跳转期间稍候，不把启动过程误报成 API 失败。
            for (int i = 0; i < 40; i++)
            {
                try { if (Evaluate("String(document.readyState==='complete' && !!document.querySelector('#root'))") == "true") break; }
                catch (Exception) { }
                Thread.Sleep(200);
            }
            string script = "(async()=>{if(location.origin!==" + json.Serialize(Site.TrimEnd('/')) + ")throw Error('origin');"
                + "const r=await fetch('/ajax.php?act=" + action + "',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded;charset=UTF-8'},body:new URLSearchParams("
                + json.Serialize(fields) + ")});return await r.text()})()";
            string raw = Evaluate(script);
            Dictionary<string, object> response;
            try { response = MusicSource.Object(json.DeserializeObject(raw)); }
            catch { throw new IOException("网站要求浏览器验证，请在音乐网站窗口完成验证后重试"); }
            if (MusicSource.Text(response, "code") != "0") throw new IOException(MusicSource.Text(response, "msg"));
            if (!response.ContainsKey("data")) throw new IOException("网站没有返回数据");
            return response["data"];
        }

        public void Dispose()
        {
            if (socketUrl == null) return;
            // 只关闭本功能创建的独立浏览器，不触碰用户的日常浏览器。
            try
            {
                using (ClientWebSocket ws = new ClientWebSocket())
                using (CancellationTokenSource timeout = new CancellationTokenSource(1500))
                {
                    ws.ConnectAsync(new Uri(socketUrl), timeout.Token).GetAwaiter().GetResult();
                    byte[] message = Encoding.UTF8.GetBytes("{\"id\":2,\"method\":\"Browser.close\"}");
                    ws.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, timeout.Token).GetAwaiter().GetResult();
                }
            }
            catch (Exception) { }
            socketUrl = null;
        }
    }

    public static class MusicSource
    {
        static MusicSource()
        { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
        public static Dictionary<string, object> Object(object value)
        {
            Dictionary<string, object> result = value as Dictionary<string, object>;
            if (result == null) throw new IOException("音乐网站返回的数据格式异常");
            return result;
        }
        public static string Text(Dictionary<string, object> value, string key)
        { object text; return value.TryGetValue(key, out text) && text != null ? Convert.ToString(text, System.Globalization.CultureInfo.InvariantCulture) : ""; }
        public static Dictionary<string, object> Extreme(Dictionary<string, object> song)
        {
            object value;
            if (song.TryGetValue("minfo", out value) && value is object[])
                foreach (object item in (object[])value)
                {
                    Dictionary<string, object> quality = Object(item);
                    if (Text(quality, "format") == "mp3" && Text(quality, "bitrate") == "320") return quality;
                }
            throw new IOException("这首歌没有极高音质（MP3 320K），请选择其他结果或平台");
        }
        public static string SafeName(string text)
        {
            StringBuilder name = new StringBuilder();
            foreach (char c in text.Trim()) name.Append(c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0 ? '_' : c);
            string result = name.ToString().TrimEnd(' ', '.');
            if (result.Length == 0) throw new IOException("歌名或歌手为空，无法按规则命名");
            return result.Length > 50 ? result.Substring(0, 50).TrimEnd(' ', '.') : result;
        }
        public static string FileName(Dictionary<string, object> song)
        { return SafeName(CleanTitle(Text(song, "name"))) + " - " + SafeName(Text(song, "artist")); }

        public static string CleanTitle(string title)
        {
            const string promo = "主题曲|主题歌|片头曲|片头歌|片尾曲|片尾歌|插曲|推广曲|宣传曲|印象曲|预告曲|原声带";
            const string media = "电视连续剧|电视剧|影视剧|网络剧|网剧|电影|影片|动画片|动画|动漫|纪录片|综艺|音乐剧|游戏|手游";
            string text = (title ?? "").Trim();
            // 只移除含宣传说明的括号，Live、伴奏、Remix 等版本括号保留。
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"\([^()]*?(?:" + promo + @")[^()]*\)|（[^（）]*?(?:" + promo + @")[^（）]*）|【[^【】]*?(?:" + promo + @")[^【】]*】|\[[^\[\]]*?(?:" + promo + @")[^\[\]]*\]", "").Trim();
            System.Text.RegularExpressions.Match marker = System.Text.RegularExpressions.Regex.Match(text, promo);
            if (!marker.Success) return text;
            string prefix = text.Substring(0, marker.Index);
            int start = -1;
            // 从最后一个说明分隔符截断，保留标题中更早的连字符和版本信息。
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(prefix,
                @"\s+[-–—:：|/]\s*|[-–—:：|/]\s*(?=[《〈「『])")) start = match.Index;
            if (start < 0)
            {
                start = prefix.LastIndexOfAny(new char[] { '《', '〈', '「', '『' });
                if (start > 0)
                {
                    System.Text.RegularExpressions.Match description = System.Text.RegularExpressions.Regex.Match(prefix.Substring(0, start), @"(?:" + media + @")\s*$");
                    if (description.Success) start = description.Index;
                }
                else
                {
                    start = -1;
                    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(prefix, media))
                        if (match.Index > 0) start = match.Index;
                    if (start < 0) start = prefix.LastIndexOfAny(new char[] { '-', '–', '—', ':', '：', '|', '/' });
                    if (start < 0) start = prefix.LastIndexOfAny(new char[] { ' ', '\t' });
                }
            }
            string cleaned = start > 0 ? text.Substring(0, start).TrimEnd(' ', '\t', '-', '–', '—', ':', '：', '|', '/', '·') : text;
            return cleaned.Length > 0 ? cleaned : text;
        }

        public static void ValidateMp3(string path)
        {
            using (FileStream file = File.OpenRead(path))
            {
                byte[] tag = new byte[10];
                if (file.Read(tag, 0, tag.Length) == 10 && tag[0] == 'I' && tag[1] == 'D' && tag[2] == '3')
                {
                    long offset = 10L + ((tag[6] & 127) << 21) + ((tag[7] & 127) << 14) + ((tag[8] & 127) << 7) + (tag[9] & 127);
                    if (tag[3] == 4 && (tag[5] & 16) != 0) offset += 10;
                    if (offset >= file.Length) throw new IOException("音频的 ID3 标签不完整");
                    file.Position = offset;
                }
                else file.Position = 0;
                byte[] head = new byte[65536]; int length = file.Read(head, 0, head.Length);
                if (length < 4) throw new IOException("下载的音频为空或不完整");
                // 验证真实 MP3 帧，避免把验证页或 JSON 错误上传为音频。
                for (int i = 0; i + 4 < length; i++)
                    if (head[i] == 255 && (head[i + 1] & 0xFE) == 0xFA && (head[i + 2] >> 4) == 14
                        && (head[i + 2] & 12) != 12)
                    {
                        int rate = new int[] { 44100, 48000, 32000 }[(head[i + 2] >> 2) & 3];
                        int next = i + 144000 * 320 / rate + ((head[i + 2] >> 1) & 1);
                        if (next + 4 < length && head[next] == 255 && (head[next + 1] & 0xFE) == 0xFA && (head[next + 2] >> 4) == 14) return;
                    }
                throw new IOException("下载内容不是 MP3 320K 音频，请重新获取地址");
            }
        }
        public static void ValidateLyric(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || !System.Text.RegularExpressions.Regex.IsMatch(text, @"\[\d{1,3}:\d{2}(?:[.:]\d+)?\]"))
                throw new IOException("网站没有返回有效的 LRC 歌词，未上传歌曲");
        }
        public static void Download(string url, string path, Action<long, long> progress)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http")) throw new IOException("音乐下载地址无效");
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Timeout = 30000; request.ReadWriteTimeout = 60000;
            request.UserAgent = "Mozilla/5.0";
            string partial = path + ".part";
            try
            {
                using (WebResponse response = request.GetResponse())
                using (Stream input = response.GetResponseStream())
                using (FileStream output = File.Create(partial))
                {
                    long done = 0; int n; byte[] buffer = new byte[65536];
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, n); done += n;
                        if (done > 200 * 1024 * 1024) throw new IOException("音频文件超过 200 MB");
                        progress(done, response.ContentLength);
                    }
                    if (response.ContentLength >= 0 && done != response.ContentLength) throw new IOException("音频下载不完整");
                }
                ValidateMp3(partial);
                if (File.Exists(path)) File.Delete(path);
                File.Move(partial, path);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }

        public static void UploadPair(string[] files, Func<string, long?> remoteSize, Action<string> upload, Action<string> report)
        {
            // 先检查两份文件，再开始上传，歌词撞名时不会先留下半首新歌曲。
            foreach (string file in files)
            {
                long? existing = remoteSize(Path.GetFileName(file));
                if (existing.HasValue && (!File.Exists(file + ".uploaded") || existing.Value != new FileInfo(file).Length))
                    throw new IOException("云盘已有同名文件「" + Path.GetFileName(file) + "」，请换歌单或处理同名文件后重试");
            }
            foreach (string file in files)
            {
                long? existing = remoteSize(Path.GetFileName(file));
                if (existing.HasValue)
                {
                    if (!File.Exists(file + ".uploaded") || existing.Value != new FileInfo(file).Length)
                        throw new IOException("云盘出现同名文件，请检查目标歌单后重试");
                    continue;
                }
                upload(file); File.WriteAllText(file + ".uploaded", "uploaded");
            }
            report("正在核对云端文件…");
            foreach (string file in files)
            {
                long? size = remoteSize(Path.GetFileName(file));
                if (!size.HasValue || size.Value != new FileInfo(file).Length)
                    throw new IOException("云端文件尚未确认完整，保留本地文件，请稍后重试");
            }
            foreach (string file in files) File.Delete(file);
            foreach (string file in files) File.Delete(file + ".uploaded");
        }
    }
}
