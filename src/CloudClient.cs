using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Skylark
{
    /// <summary>云盘上的一个条目（文件或目录）。</summary>
    public class CloudEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }

        public CloudEntry()
        {
            Name = string.Empty;
            Path = string.Empty;
        }
    }

    [DataContract]
    internal class DirentDto
    {
        [DataMember(Name = "file_name")]
        public string Name { get; set; }

        [DataMember(Name = "file_path")]
        public string Path { get; set; }

        [DataMember(Name = "is_dir")]
        public bool IsDirectory { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }

        [DataMember(Name = "last_modified")]
        public string Modified { get; set; }
    }

    [DataContract]
    internal class DirentListDto
    {
        [DataMember(Name = "dirent_list")]
        public List<DirentDto> Items { get; set; }
    }

    /// <summary>资料库 API 令牌模式下的目录条目（字段名与分享链接不同）。</summary>
    [DataContract]
    internal class TokenDirentDto
    {
        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "parent_dir")]
        public string ParentDir { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }

        [DataMember(Name = "mtime")]
        public string Modified { get; set; }
    }

    [DataContract]
    internal class TokenDirDto
    {
        [DataMember(Name = "repo_name")]
        public string RepoName { get; set; }

        [DataMember(Name = "dirent_list")]
        public List<TokenDirentDto> Items { get; set; }
    }

    [DataContract]
    internal class TokenRepoInfoDto
    {
        [DataMember(Name = "repo_id")]
        public string RepoId { get; set; }

        [DataMember(Name = "repo_name")]
        public string RepoName { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }

        [DataMember(Name = "file_count")]
        public int FileCount { get; set; }
    }

    /// <summary>资料库信息（API 令牌模式）。</summary>
    public class CloudRepoInfo
    {
        public string RepoId;
        public string Name;
        public long Size;
        public int FileCount;
    }

    /// <summary>
    /// 云盘（Seafile 分享链接）访问：列目录、读取文件头、下载、读取歌词。
    /// 服务端要求带 User-Agent，否则返回 403。
    /// </summary>
    public static class CloudClient
    {
        public const string DefaultHost = "https://cloud.tsinghua.edu.cn";
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Skylark/2.0 (+https://github.com/G336ncx-bjx/skylark-music)";

        static CloudClient()
        {
            // .NET Framework 默认只启用老协议，这里显式打开 TLS 1.2
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12
                    | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ServicePointManager.DefaultConnectionLimit = 8;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>从分享链接（或裸 token）中取出 token。</summary>
        public static string ParseToken(string urlOrToken)
        {
            if (string.IsNullOrEmpty(urlOrToken)) return string.Empty;
            string text = urlOrToken.Trim();
            if (text.IndexOf('/') < 0) return text.Trim('/');

            string[] parts = text.Split('/');
            for (int i = 0; i + 1 < parts.Length; i++)
            {
                if (parts[i] == "d" || parts[i] == "upload") return parts[i + 1];
            }
            return parts[parts.Length - 1];
        }

        /// <summary>
        /// 判断填入的是「资料库 API 令牌」还是分享链接。
        /// API 令牌形如 40 位十六进制（资料的 API 令牌），用它可以直接读写资料库。
        /// </summary>
        public static bool IsApiToken(string urlOrToken)
        {
            if (string.IsNullOrEmpty(urlOrToken)) return false;
            string text = urlOrToken.Trim();
            if (text.IndexOf('/') >= 0 || text.Length != 40) return false;
            foreach (char c in text)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        /// <summary>资料库信息（仅 API 令牌模式）。</summary>
        public static CloudRepoInfo GetRepoInfo(string urlOrToken)
        {
            string json = TokenGet(urlOrToken, "/api/v2.1/via-repo-token/repo-info/");
            TokenRepoInfoDto dto;
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(TokenRepoInfoDto));
                dto = (TokenRepoInfoDto)ser.ReadObject(ms);
            }
            CloudRepoInfo info = new CloudRepoInfo();
            if (dto != null)
            {
                info.RepoId = dto.RepoId;
                info.Name = dto.RepoName;
                info.Size = dto.Size;
                info.FileCount = dto.FileCount;
            }
            return info;
        }

        /// <summary>取分享链接所在的主机（默认清华云盘）。</summary>
        public static string ParseHost(string urlOrToken)
        {
            if (string.IsNullOrEmpty(urlOrToken)) return DefaultHost;
            string text = urlOrToken.Trim();
            int scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0) return DefaultHost;
            int start = scheme + 3;
            int slash = text.IndexOf('/', start);
            string host = slash < 0 ? text : text.Substring(0, slash);
            return host.Length > 8 ? host : DefaultHost;
        }

        public static string ShareUrl(string urlOrToken)
        {
            return ParseHost(urlOrToken) + "/d/" + ParseToken(urlOrToken);
        }

        public static string EscapePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string[] parts = path.Split('/');
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                if (sb.Length > 0) sb.Append('/');
                sb.Append(Uri.EscapeDataString(parts[i]));
            }
            return sb.ToString();
        }

        /// <summary>列出某个目录下的条目。</summary>
        public static List<CloudEntry> List(string urlOrToken, string path)
        {
            if (IsApiToken(urlOrToken)) return ListByToken(urlOrToken, path, false);

            string token = ParseToken(urlOrToken);
            string host = ParseHost(urlOrToken);
            if (token.Length == 0) throw new InvalidOperationException("分享链接无效");

            string url = host + "/api/v2.1/share-links/" + token + "/dirents/?path=%2F"
                       + EscapePath(path);
            string json = GetString(url);

            DirentListDto dto;
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(DirentListDto));
                dto = (DirentListDto)ser.ReadObject(ms);
            }

            List<CloudEntry> list = new List<CloudEntry>();
            if (dto == null || dto.Items == null) return list;
            foreach (DirentDto item in dto.Items)
            {
                if (item == null || string.IsNullOrEmpty(item.Name)) continue;
                CloudEntry entry = new CloudEntry();
                entry.Name = item.Name;
                entry.Path = string.IsNullOrEmpty(item.Path) ? "/" + item.Name : item.Path;
                entry.IsDirectory = item.IsDirectory;
                entry.Size = item.Size;
                DateTime modified;
                if (!string.IsNullOrEmpty(item.Modified) &&
                    DateTime.TryParse(item.Modified, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out modified))
                    entry.Modified = modified;
                else
                    entry.Modified = DateTime.UtcNow;
                list.Add(entry);
            }
            return list;
        }

        /// <summary>递归列出所有文件（最多 5 层深）。</summary>
        public static List<CloudEntry> ListAllFiles(string urlOrToken, int maxDepth)
        {
            // API 令牌模式下服务端支持一次递归列出
            if (IsApiToken(urlOrToken)) return ListByToken(urlOrToken, string.Empty, true);

            List<CloudEntry> files = new List<CloudEntry>();
            List<string> dirs = new List<string>();
            dirs.Add(string.Empty);
            int depth = 0;
            while (dirs.Count > 0 && depth <= maxDepth)
            {
                List<string> next = new List<string>();
                foreach (string dir in dirs)
                {
                    List<CloudEntry> entries = List(urlOrToken, dir);
                    foreach (CloudEntry entry in entries)
                    {
                        if (entry.IsDirectory) next.Add(Trim(entry.Path));
                        else files.Add(entry);
                    }
                }
                dirs = next;
                depth++;
            }
            return files;
        }

        private static string Trim(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return path.Trim('/');
        }

        public static string BuildFileUrl(string urlOrToken, string filePath)
        {
            string token = ParseToken(urlOrToken);
            string host = ParseHost(urlOrToken);
            string encoded = filePath.StartsWith("/") ? filePath.Substring(1) : filePath;
            return host + "/d/" + token + "/files/?p=%2F" + EscapePath(encoded) + "&dl=1";
        }
        /// <summary>按连接方式（分享链接 / API 令牌）解析出可直接下载的 URL。</summary>
        public static string ResolveFileUrl(string urlOrToken, string filePath)
        {
            if (IsApiToken(urlOrToken)) return GetDownloadUrlByToken(urlOrToken, filePath);
            return BuildFileUrl(urlOrToken, filePath);
        }

        /// <summary>只取文件前若干字节（利用 Range），用于解析时长等信息。</summary>
        public static byte[] DownloadHead(string urlOrToken, string filePath, int bytes)
        {
            HttpWebRequest request = CreateRequest(ResolveFileUrl(urlOrToken, filePath));
            request.AddRange(0, bytes - 1);
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    MemoryStream buffer = new MemoryStream();
                    byte[] chunk = new byte[16384];
                    int total = 0;
                    while (total < bytes)
                    {
                        int read = stream.Read(chunk, 0, Math.Min(chunk.Length, bytes - total));
                        if (read <= 0) break;
                        buffer.Write(chunk, 0, read);
                        total += read;
                    }
                    return buffer.ToArray();
                }
            }
        }

        public static string GetText(string urlOrToken, string filePath)
        {
            byte[] data = DownloadAll(urlOrToken, filePath);
            return TextUtil.DecodeBytes(data);
        }

        public static byte[] DownloadAll(string urlOrToken, string filePath)
        {
            HttpWebRequest request = CreateRequest(ResolveFileUrl(urlOrToken, filePath));
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    MemoryStream buffer = new MemoryStream();
                    byte[] chunk = new byte[65536];
                    int read;
                    while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                    }
                    return buffer.ToArray();
                }
            }
        }

        /// <summary>下载到本地文件，progress 回调参数为 (已下载字节, 总字节)。</summary>
        public static void DownloadTo(string urlOrToken, string filePath, string targetPath,
            Action<long, long> progress)
        {
            HttpWebRequest request = CreateRequest(ResolveFileUrl(urlOrToken, filePath));
            request.Timeout = 30000;
            string temp = targetPath + ".part";
            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using (WebResponse response = request.GetResponse())
            {
                long total = response.ContentLength;
                using (Stream stream = response.GetResponseStream())
                {
                    using (FileStream file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] chunk = new byte[131072];
                        long done = 0;
                        int read;
                        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            file.Write(chunk, 0, read);
                            done += read;
                            if (progress != null) progress(done, total);
                        }
                    }
                }
            }

            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(temp, targetPath);
        }

        /// <summary>取得某个目录的上传地址（分享链接需要开启上传权限）。</summary>
        public static string GetUploadUrl(string urlOrToken, string dirPath)
        {
            if (IsApiToken(urlOrToken)) return GetUploadUrlByToken(urlOrToken, dirPath);

            string token = ParseToken(urlOrToken);
            string host = ParseHost(urlOrToken);
            string path = string.IsNullOrEmpty(dirPath) ? "/" : dirPath;
            string url = host + "/api/v2.1/share-links/" + token + "/upload/?path="
                       + Uri.EscapeDataString(path);
            string json = GetString(url);

            int at = json.IndexOf("\"upload_link\"", StringComparison.Ordinal);
            if (at < 0) throw new InvalidOperationException("该分享链接没有开启上传权限");
            int start = json.IndexOf('"', json.IndexOf(':', at) + 1);
            int end = json.IndexOf('"', start + 1);
            if (start < 0 || end < 0) throw new InvalidOperationException("上传地址解析失败");
            return json.Substring(start + 1, end - start - 1);
        }

        /// <summary>
        /// 上传本地文件到云盘的指定目录。
        /// progress 回调参数为 (已上传字节, 总字节)；同名文件会自动重试覆盖。
        /// </summary>
        public static bool Upload(string urlOrToken, string localFilePath, string dirPath,
            Action<long, long> progress, out bool replaced)
        {
            replaced = false;
            string link = GetUploadUrl(urlOrToken, dirPath);
            string targetDir = string.IsNullOrEmpty(dirPath) ? "/" : dirPath;
            string targetName = Path.GetFileName(localFilePath);

            // 令牌模式下先把同名旧文件删掉：upload-api 不认 replace=1，遇到同名只会默默改名成
            // 「xxx (1).ext」，直接传会出现一堆副本。
            if (IsApiToken(urlOrToken) && CloudHasFile(urlOrToken.Trim(), targetDir, targetName))
            {
                DeleteFiles(urlOrToken.Trim(), targetDir, new List<string>(new string[] { targetName }));
                System.Threading.Thread.Sleep(900);   // 等服务器那边删干净，避免又撞名
                replaced = true;
            }

            string result;
            bool ok = TryUpload(link, localFilePath, dirPath, progress, out result);
            if (ok && NameMatches(result, localFilePath)) return true;

            // 服务器遇到同名文件会「默默改名」成 xxx (1).ext（upload-api 不认 replace=1），
            // 所以发现名字被改了：删掉刚传上去的副本和原来那个同名文件，再传一次。
            if (ok)
            {
                string uploaded = UploadedName(result);
                bool canDelete = IsApiToken(urlOrToken);
                if (canDelete && !string.IsNullOrEmpty(uploaded))
                {
                    DeleteFiles(urlOrToken.Trim(), string.IsNullOrEmpty(dirPath) ? "/" : dirPath,
                        new List<string>(new string[] { uploaded, Path.GetFileName(localFilePath) }));
                    ok = TryUpload(link, localFilePath, dirPath, progress, out result);
                    replaced = ok;
                    if (ok) return true;
                }
            }
            if (!ok && result == null) result = "上传失败";

            if (result != null && (result.IndexOf("exist", StringComparison.OrdinalIgnoreCase) >= 0
                || result.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                ok = TryUpload(link, localFilePath, dirPath, progress, out result);
                replaced = ok;
            }
            if (!ok) throw new InvalidOperationException(Shorten(result));
            return true;
        }

        /// <summary>在线入库专用：只新增，绝不删除或覆盖原文件。</summary>
        public static void UploadNew(string endpoint, string path, string dir, Action<long, long> progress)
        {
            foreach (CloudEntry entry in List(endpoint, dir))
                if (string.Equals(entry.Name, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("云盘已出现同名文件，请检查目标歌单后重试");
            string result;
            if (!TryUpload(GetUploadUrl(endpoint, dir), path, dir, progress, true, out result))
                throw new IOException(Shorten(result));
            if (!NameMatches(result, path)) throw new IOException("服务器改了上传文件名，请检查云端副本后重试");
        }

        /// <summary>上传接口的返回里带着服务器实际保存的文件名。</summary>
        private static string UploadedName(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            // 服务端可能转义中文、引号或反斜杠，文件名必须按 JSON 解码再比较。
            try
            {
                object parsed = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(body);
                object[] items = parsed as object[];
                Dictionary<string, object> record = (items != null && items.Length > 0 ? items[0] : parsed) as Dictionary<string, object>;
                object name;
                if (record != null && record.TryGetValue("name", out name)) return Convert.ToString(name);
            }
            catch (Exception) { }
            int at = body.IndexOf("\"name\"", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            int colon = body.IndexOf(':', at);
            if (colon < 0) return null;
            int start = body.IndexOf('"', colon + 1);
            if (start < 0) return null;
            int end = body.IndexOf('"', start + 1);
            if (end < 0) return null;
            return body.Substring(start + 1, end - start - 1);
        }

        /// <summary>服务器返回的名字和我们传的名字是否一致（不一致说明被改名了）。</summary>
        /// <summary>云端指定目录里是否已经有这个文件名的文件。</summary>
        private static bool CloudHasFile(string token, string dir, string name)
        {
            try
            {
                foreach (CloudEntry entry in ListAllFiles(token, 4))
                {
                    if (entry.IsDirectory) continue;
                    if (!string.Equals(entry.Name, name, StringComparison.Ordinal)) continue;
                    string parent = entry.Path == null ? "/" : entry.Path;
                    int slash = parent.LastIndexOf('/');
                    parent = slash > 0 ? parent.Substring(0, slash) : "/";
                    string want = string.IsNullOrEmpty(dir) || dir == "/" ? "/" : dir.TrimEnd('/');
                    if (string.Equals(parent, want, StringComparison.Ordinal)) return true;
                }
            }
            catch (Exception)
            {
                // 查不到就当没有，交给上传本身去处理
            }
            return false;
        }

        private static bool NameMatches(string body, string localFilePath)
        {
            string uploaded = UploadedName(body);
            if (string.IsNullOrEmpty(uploaded)) return true;   // 没解析出来就不折腾
            return string.Equals(uploaded, Path.GetFileName(localFilePath), StringComparison.Ordinal);
        }

        private static bool TryUpload(string uploadLink, string localFilePath, string dirPath,
            Action<long, long> progress, out string error)
        {
            return TryUpload(uploadLink, localFilePath, dirPath, progress, false, out error);
        }

        private static bool TryUpload(string uploadLink, string localFilePath, string dirPath,
            Action<long, long> progress, bool captureResponse, out string error)
        {
            error = null;
            string url = uploadLink + "?ret-json=1";

            string boundary = "----Skylark" + Guid.NewGuid().ToString("N");
            string fileName = Path.GetFileName(localFilePath);
            string dir = string.IsNullOrEmpty(dirPath) ? "/" : dirPath;
            if (!dir.StartsWith("/")) dir = "/" + dir;

            byte[] dirPart = FormField(boundary, "parent_dir", dir);
            byte[] relPart = FormField(boundary, "relative_path", string.Empty);
            byte[] filePart = FileFieldHeader(boundary, fileName);
            byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            long fileLength = new FileInfo(localFilePath).Length;

            HttpWebRequest request = CreateRequest(url);
            request.Method = "POST";
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.ContentLength = dirPart.Length + relPart.Length + filePart.Length + fileLength + tail.Length;
            request.Timeout = 60000;

            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(dirPart, 0, dirPart.Length);
                stream.Write(relPart, 0, relPart.Length);
                stream.Write(filePart, 0, filePart.Length);

                using (FileStream file = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[131072];
                    long done = 0;
                    int read;
                    while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        stream.Write(buffer, 0, read);
                        done += read;
                        if (progress != null) progress(done, fileLength);
                    }
                }
                stream.Write(tail, 0, tail.Length);
            }

            try
            {
                using (WebResponse response = request.GetResponse())
                {
                    using (Stream stream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string body = reader.ReadToEnd();
                            if (body.IndexOf("\"error\"", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                error = body;
                                return false;
                            }
                            if (captureResponse) error = body;
                            return true;
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                error = ReadError(ex);
                return false;
            }
        }

        private static string ReadError(WebException ex)
        {
            try
            {
                if (ex.Response != null)
                {
                    using (Stream stream = ex.Response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
            }
            catch (Exception)
            {
            }
            return ex.Message;
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return "上传失败";
            string clean = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length > 160 ? clean.Substring(0, 160) : clean;
        }

        private static byte[] FormField(string boundary, string name, string value)
        {
            string text = "--" + boundary + "\r\n"
                + "Content-Disposition: form-data; name=\"" + name + "\"\r\n\r\n"
                + value + "\r\n";
            return Encoding.UTF8.GetBytes(text);
        }

        private static byte[] FileFieldHeader(string boundary, string fileName)
        {
            string text = "--" + boundary + "\r\n"
                + "Content-Disposition: form-data; name=\"file\"; filename=\""
                + fileName.Replace("\"", "_") + "\"\r\n"
                + "Content-Type: application/octet-stream\r\n\r\n";
            return Encoding.UTF8.GetBytes(text);
        }

        private static HttpWebRequest CreateRequest(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 30000;
            request.AllowAutoRedirect = true;
            request.KeepAlive = true;
            return request;
        }

        #region 资料库 API 令牌模式

        private static string TokenGet(string token, string apiPath)
        {
            string url = ParseHost(token) + apiPath;
            HttpWebRequest request = CreateRequest(url);
            request.Headers.Add("Authorization", "Token " + token.Trim());
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
        }

        /// <summary>用 API 令牌列目录（recursive=true 时一次列出所有层级）。</summary>
        public static List<CloudEntry> ListByToken(string token, string path, bool recursive)
        {
            string dir = string.IsNullOrEmpty(path) ? "/" : path;
            string url = ParseHost(token) + "/api/v2.1/via-repo-token/dir/?path="
                       + Uri.EscapeDataString(dir) + (recursive ? "&recursive=1" : "&recursive=0");
            HttpWebRequest request = CreateRequest(url);
            request.Headers.Add("Authorization", "Token " + token.Trim());

            string json;
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        json = reader.ReadToEnd();
                    }
                }
            }

            TokenDirDto dto;
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(TokenDirDto));
                dto = (TokenDirDto)ser.ReadObject(ms);
            }

            List<CloudEntry> list = new List<CloudEntry>();
            if (dto == null || dto.Items == null) return list;
            foreach (TokenDirentDto item in dto.Items)
            {
                if (item == null || string.IsNullOrEmpty(item.Name)) continue;
                CloudEntry entry = new CloudEntry();
                entry.Name = item.Name;
                entry.IsDirectory = string.Equals(item.Type, "dir", StringComparison.OrdinalIgnoreCase);
                entry.Size = item.Size;
                string parent = string.IsNullOrEmpty(item.ParentDir) ? "/" : item.ParentDir;
                if (!parent.EndsWith("/")) parent += "/";
                entry.Path = parent + item.Name;
                DateTime modified;
                if (!string.IsNullOrEmpty(item.Modified) &&
                    DateTime.TryParse(item.Modified, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out modified))
                    entry.Modified = modified;
                else
                    entry.Modified = DateTime.UtcNow;
                list.Add(entry);
            }
            return list;
        }

        /// <summary>用 API 令牌取文件下载地址。</summary>
        public static string GetDownloadUrlByToken(string token, string filePath)
        {
            string json = TokenGet(token, "/api/v2.1/via-repo-token/download-link/?path="
                + Uri.EscapeDataString(filePath));
            return Unquote(json);
        }

        /// <summary>用 API 令牌取上传地址。</summary>
        public static string GetUploadUrlByToken(string token, string dirPath)
        {
            string dir = string.IsNullOrEmpty(dirPath) ? "/" : dirPath;
            string json = TokenGet(token, "/api/v2.1/via-repo-token/upload-link/?path="
                + Uri.EscapeDataString(dir));
            return Unquote(json);
        }

        /// <summary>用 API 令牌删除文件（需要令牌有读写权限）。</summary>
        public static void DeleteFiles(string token, string parentDir, List<string> names)
        {
            if (names == null || names.Count == 0) return;
            string url = ParseHost(token) + "/api/v2.1/via-repo-token/batch-delete-item/";
            HttpWebRequest request = CreateRequest(url);
            request.Method = "DELETE";
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", "Token " + token.Trim());

            StringBuilder body = new StringBuilder();
            body.Append("{\"parent_dir\":\"").Append(JsonEscape(parentDir)).Append("\",\"dirents\":[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) body.Append(',');
                body.Append('"').Append(JsonEscape(names[i])).Append('"');
            }
            body.Append("]}");

            byte[] data = Encoding.UTF8.GetBytes(body.ToString());
            request.ContentLength = data.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        reader.ReadToEnd();
                    }
                }
            }
        }

        /// <summary>
        /// 批量移动文件/文件夹（服务端完成，不重传数据）。
        /// 目标目录必须已存在 —— 这是 Seafile 的 `sync-batch-move-item` 接口约定。
        /// 歌单功能就是靠它把歌搬进/搬出歌单文件夹。
        /// </summary>
        public static void MoveItems(string token, string srcDir, List<string> names, string dstDir)
        {
            if (names == null || names.Count == 0) return;
            StringBuilder body = new StringBuilder();
            body.Append("{\"src_parent_dir\":\"").Append(JsonEscape(srcDir)).Append("\",\"src_dirents\":[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) body.Append(',');
                body.Append('"').Append(JsonEscape(names[i])).Append('"');
            }
            body.Append("],\"dst_parent_dir\":\"").Append(JsonEscape(dstDir)).Append("\"}");
            PostJsonByToken(token, "/api/v2.1/via-repo-token/sync-batch-move-item/", body.ToString());
        }

        /// <summary>把某个文件/目录复制一份到目标目录（目标目录必须已存在）。</summary>
        public static void CopyItems(string token, string srcDir, List<string> names, string dstDir)
        {
            if (names == null || names.Count == 0) return;
            StringBuilder body = new StringBuilder();
            body.Append("{\"src_parent_dir\":\"").Append(JsonEscape(srcDir)).Append("\",\"src_dirents\":[");
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) body.Append(',');
                body.Append('"').Append(JsonEscape(names[i])).Append('"');
            }
            body.Append("],\"dst_parent_dir\":\"").Append(JsonEscape(dstDir)).Append("\"}");
            PostJsonByToken(token, "/api/v2.1/via-repo-token/sync-batch-copy-item/", body.ToString());
        }

        /// <summary>移动一个文件夹到别的目录（同一资料库内）。</summary>
        public static void MoveDir(string token, string srcParentDir, string dirName, string dstParentDir)
        {
            StringBuilder body = new StringBuilder();
            body.Append("{\"src_parent_dir\":\"").Append(JsonEscape(srcParentDir))
                .Append("\",\"src_dirent_name\":\"").Append(JsonEscape(dirName))
                .Append("\",\"dst_parent_dir\":\"").Append(JsonEscape(dstParentDir)).Append("\"}");
            PostJsonByToken(token, "/api/v2.1/via-repo-token/move-dir/", body.ToString());
        }

        /// <summary>
        /// 确保云盘上某个目录存在（歌单的「新建」用它）。
        /// 文件夹令牌没有 mkdir 接口（/api2/repos/… 那套只有账号令牌能用），
        /// 但上传接口支持 relative_path 递归建子目录：往目标目录传一个占位文件、
        /// 再把占位文件删掉，目录就留下了。
        /// </summary>
        public static void EnsureDir(string endpoint, string dirPath)
        {
            string dir = string.IsNullOrEmpty(dirPath) ? "/" : dirPath.Trim();
            if (dir == "/" || dir.Length == 0) return;
            if (!dir.StartsWith("/")) dir = "/" + dir;
            if (DirExists(endpoint, dir)) return;

            string temp = Path.Combine(Path.GetTempPath(), "skylark-dir-probe.tmp");
            File.WriteAllText(temp, "placeholder", new UTF8Encoding(false));
            string relative = dir.Trim('/') + "/";
            try
            {
                string link = GetUploadUrlByToken(endpoint.Trim(), "/");
                string error;
                if (!TryUploadTo(link, temp, "/", relative, null, out error))
                    throw new InvalidOperationException(Shorten(error));
            }
            finally
            {
                try { File.Delete(temp); } catch (Exception) { }
            }
            // 占位文件删掉，只留目录
            try
            {
                DeleteFiles(endpoint.Trim(), dir, new List<string>(new string[] { "skylark-dir-probe.tmp" }));
            }
            catch (Exception)
            {
                // 删不掉也不影响：占位文件不是音频/歌词，App 扫描时会忽略
            }
        }

        /// <summary>从云盘路径取出歌单名（根目录下的歌返回空串）。</summary>
        public static string PlaylistOf(string cloudPath)
        {
            if (string.IsNullOrEmpty(cloudPath)) return "";
            string p = cloudPath.Replace('\\', '/').Trim('/');
            int slash = p.IndexOf('/');
            return slash > 0 ? p.Substring(0, slash) : "";
        }

        /// <summary>云盘上某个目录存不存在。</summary>
        public static bool DirExists(string endpoint, string dirPath)
        {
            try
            {
                List<CloudEntry> list = List(endpoint.Trim(), dirPath);
                return list != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>和 TryUpload 一样，但能指定上传接口的 relative_path（用来递归建子目录）。</summary>
        private static bool TryUploadTo(string uploadLink, string localFilePath, string dirPath,
            string relativePath, Action<long, long> progress, out string error)
        {
            error = null;
            string url = uploadLink + "?ret-json=1";
            string boundary = "----Skylark" + Guid.NewGuid().ToString("N");
            string fileName = Path.GetFileName(localFilePath);
            string dir = string.IsNullOrEmpty(dirPath) ? "/" : dirPath;
            if (!dir.StartsWith("/")) dir = "/" + dir;

            byte[] dirPart = FormField(boundary, "parent_dir", dir);
            byte[] relPart = FormField(boundary, "relative_path", relativePath == null ? "" : relativePath);
            byte[] filePart = FileFieldHeader(boundary, fileName);
            byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            long fileLength = new FileInfo(localFilePath).Length;

            HttpWebRequest request = CreateRequest(url);
            request.Method = "POST";
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.ContentLength = dirPart.Length + relPart.Length + filePart.Length + fileLength + tail.Length;
            request.Timeout = 60000;
            try
            {
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(dirPart, 0, dirPart.Length);
                    stream.Write(relPart, 0, relPart.Length);
                    stream.Write(filePart, 0, filePart.Length);
                    using (FileStream file = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        byte[] buffer = new byte[65536];
                        long done = 0;
                        int read;
                        while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            stream.Write(buffer, 0, read);
                            done += read;
                            if (progress != null) progress(done, fileLength);
                        }
                    }
                    stream.Write(tail, 0, tail.Length);
                }
                using (WebResponse response = request.GetResponse())
                {
                    using (Stream stream = response.GetResponseStream())
                    {
                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            reader.ReadToEnd();
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void PostJsonByToken(string token, string apiPath, string json)
        {
            string url = ParseHost(token) + apiPath;
            HttpWebRequest request = CreateRequest(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", "Token " + token.Trim());
            byte[] data = Encoding.UTF8.GetBytes(json);
            request.ContentLength = data.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        reader.ReadToEnd();
                    }
                }
            }
        }

        private static string JsonEscape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string Unquote(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            string text = json.Trim();
            if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
                text = text.Substring(1, text.Length - 2);
            return text.Replace("\\/", "/");
        }

        #endregion

        private static string GetString(string url)
        {
            HttpWebRequest request = CreateRequest(url);
            using (WebResponse response = request.GetResponse())
            {
                using (Stream stream = response.GetResponseStream())
                {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
        }
    }
}
