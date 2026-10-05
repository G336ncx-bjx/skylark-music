package com.skylark.music;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.net.URLEncoder;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * 云盘（清华云盘 / Seafile）访问。
 * 支持两种连接方式：分享链接（读 + 上传）与资料库 API 令牌（读 + 上传 + 删除）。
 */
public class Cloud {

    public static final String DEFAULT_HOST = "https://cloud.tsinghua.edu.cn";

    /** 云盘上的一个文件/目录。 */
    public static class Entry {
        public String name;
        public String path;   // 以 / 开头
        public long size;
        public String modified = "";   // 云盘上的修改时间，用来判断歌词有没有更新
        public boolean dir;
    }

    // ---------- 端点解析 ----------

    public static boolean isToken(String endpoint) {
        if (endpoint == null || endpoint.length() != 40 || endpoint.indexOf('/') >= 0) return false;
        for (int i = 0; i < endpoint.length(); i++) {
            char c = endpoint.charAt(i);
            boolean hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        return true;
    }

    public static String host(String endpoint) {
        if (endpoint == null) return DEFAULT_HOST;
        String text = endpoint.trim();
        int scheme = text.indexOf("://");
        if (scheme < 0) return DEFAULT_HOST;
        int start = scheme + 3;
        int slash = text.indexOf('/', start);
        String h = slash < 0 ? text : text.substring(0, slash);
        return h.length() > 8 ? h : DEFAULT_HOST;
    }

    public static String token(String endpoint) {
        if (endpoint == null) return "";
        String text = endpoint.trim();
        if (text.indexOf('/') < 0) return text;
        String[] parts = text.split("/");
        for (int i = 0; i + 1 < parts.length; i++) {
            if ("d".equals(parts[i]) || "upload".equals(parts[i])) return parts[i + 1];
        }
        return parts[parts.length - 1];
    }

    private static String[] auth(String endpoint) {
        if (isToken(endpoint)) {
            return new String[] { "Authorization", "Token " + endpoint.trim() };
        }
        return null;
    }

    private static String encodePath(String path) {
        if (path == null) return "";
        String p = path.startsWith("/") ? path.substring(1) : path;
        String[] parts = p.split("/");
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < parts.length; i++) {
            if (parts[i].length() == 0) continue;
            if (sb.length() > 0) sb.append('/');
            try {
                sb.append(URLEncoder.encode(parts[i], "UTF-8").replace("+", "%20"));
            } catch (Exception e) {
                sb.append(parts[i]);
            }
        }
        return sb.toString();
    }

    private static String enc(String text) {
        try {
            return URLEncoder.encode(text == null ? "" : text, "UTF-8").replace("+", "%20");
        } catch (Exception e) {
            return text;
        }
    }

    /** Android 里的 org.json 把 JSONException 当受检异常，这里统一包成 IOException。 */
    private static JSONObject parse(String body) throws IOException {
        try {
            return new JSONObject(body);
        } catch (Exception e) {
            throw new IOException("云端没有返回预期的 JSON：" + Util.shorten(body));
        }
    }

    // ---------- 列表 ----------

    /** 列出云盘上的全部文件（不含目录）。 */
    public static List<Entry> listAll(String endpoint) throws IOException {
        if (isToken(endpoint)) return listByToken(endpoint);
        return listByShareLink(endpoint);
    }

    private static List<Entry> listByToken(String endpoint) throws IOException {
        String url = host(endpoint) + "/api/v2.1/via-repo-token/dir/?path=%2F&recursive=1";
        JSONObject json = parse(Util.getString(url, auth(endpoint)));
        List<Entry> out = new ArrayList<Entry>();
        JSONArray items = json.optJSONArray("dirent_list");
        for (int i = 0; items != null && i < items.length(); i++) {
            JSONObject o = items.optJSONObject(i);
            if (o == null) continue;
            Entry e = new Entry();
            e.name = o.optString("name");
            String parent = o.optString("parent_dir", "/");
            if (!parent.endsWith("/")) parent = parent + "/";
            e.path = parent + e.name;
            e.size = o.optLong("size");
            e.modified = o.optString("mtime");
            e.dir = "dir".equalsIgnoreCase(o.optString("type"));
            out.add(e);
        }
        return out;
    }

    private static List<Entry> listByShareLink(String endpoint) throws IOException {
        List<Entry> files = new ArrayList<Entry>();
        List<String> dirs = new ArrayList<String>();
        dirs.add("");
        int depth = 0;
        while (!dirs.isEmpty() && depth <= 3) {
            List<String> next = new ArrayList<String>();
            for (int i = 0; i < dirs.size(); i++) {
                String dir = dirs.get(i);
                String url = host(endpoint) + "/api/v2.1/share-links/" + token(endpoint)
                        + "/dirents/?path=%2F" + encodePath(dir);
                JSONObject json = parse(Util.getString(url, null));
                JSONArray items = json.optJSONArray("dirent_list");
                for (int k = 0; items != null && k < items.length(); k++) {
                    JSONObject o = items.optJSONObject(k);
                    if (o == null) continue;
                    Entry e = new Entry();
                    e.name = o.optString("file_name");
                    e.path = o.optString("file_path", "/" + e.name);
                    e.size = o.optLong("size");
                    e.modified = o.optString("last_modified");
                    e.dir = o.optBoolean("is_dir");
                    if (e.dir) {
                        String p = e.path.startsWith("/") ? e.path.substring(1) : e.path;
                        next.add(p);
                    } else {
                        files.add(e);
                    }
                }
            }
            dirs = next;
            depth++;
        }
        return files;
    }

    /** 资料库名称（令牌模式）。 */
    public static String repoName(String endpoint) {
        try {
            String url = host(endpoint) + "/api/v2.1/via-repo-token/repo-info/";
            JSONObject o = parse(Util.getString(url, auth(endpoint)));
            return o.optString("repo_name", "");
        } catch (Exception e) {
            return "";
        }
    }

    // ---------- 下载 / 上传 / 删除 ----------

    public static String downloadUrl(String endpoint, String cloudPath) throws IOException {
        if (isToken(endpoint)) {
            String url = host(endpoint) + "/api/v2.1/via-repo-token/download-link/?path=" + enc(cloudPath);
            String body = Util.getString(url, auth(endpoint)).trim();
            return unquote(body);
        }
        return host(endpoint) + "/d/" + token(endpoint) + "/files/?p=%2F" + encodePath(cloudPath) + "&dl=1";
    }

    /** 取文件前若干字节（用于解析时长）：优先用 Range，服务器不支持时退化为直接读前若干字节。 */
    public static byte[] head(String endpoint, String cloudPath, int bytes) throws IOException {
        String url = downloadUrl(endpoint, cloudPath);
        int span = Math.max(1, bytes);
        try {
            return Util.getBytes(url, new String[] { "Range", "bytes=0-" + (span - 1) }, span);
        } catch (IOException e) {
            return Util.getBytes(url, null, span);
        }
    }

    /** 取一个小文本文件（歌词）的内容。 */
    public static String downloadText(String endpoint, String cloudPath) throws IOException {
        String url = downloadUrl(endpoint, cloudPath);
        return Util.getString(url, null);
    }

    public static void download(String endpoint, String cloudPath, File target, Util.Progress p) throws IOException {
        String url = downloadUrl(endpoint, cloudPath);
        Util.downloadTo(url, null, target, p);
    }

    public static String uploadLink(String endpoint, String dir) throws IOException {
        String path = (dir == null || dir.length() == 0) ? "/" : dir;
        if (isToken(endpoint)) {
            String url = host(endpoint) + "/api/v2.1/via-repo-token/upload-link/?path=" + enc(path);
            return unquote(Util.getString(url, auth(endpoint)).trim());
        }
        String url = host(endpoint) + "/api/v2.1/share-links/" + token(endpoint) + "/upload/?path=" + enc(path);
        JSONObject o = parse(Util.getString(url, null));
        String link = o.optString("upload_link", "");
        if (link.length() == 0) throw new IOException("该分享链接没有开启上传权限");
        return link;
    }

    public static void upload(String endpoint, File file, String dir, Util.Progress p) throws IOException {
        // 令牌模式下先把同名旧文件删掉：upload-api 不认 replace=1，遇到同名只会默默改名成
        // 「xxx (1).ext」，直接传会出现一堆副本。
        String targetDir = (dir == null || dir.length() == 0) ? "/" : dir;
        if (isToken(endpoint)) {
            String existing = findSameName(endpoint, targetDir, file.getName());
            if (existing != null) {
                delete(endpoint, existing);
                try {
                    Thread.sleep(900);   // 等服务器删干净，避免又撞名
                } catch (InterruptedException ignored) {
                    Thread.currentThread().interrupt();
                }
            }
        }
        String link = uploadLink(endpoint, dir) + "?ret-json=1";
        try {
            Util.upload(link, dir, file, p);
        } catch (IOException e) {
            String msg = e.getMessage() == null ? "" : e.getMessage().toLowerCase();
            if (msg.contains("exist") || msg.contains("already")) {
                Util.upload(link + "&replace=1", dir, file, p);
                return;
            }
            throw e;
        }
    }

    /** 在线入库专用：只新增，绝不删除或覆盖原文件。 */
    public static void uploadNew(String endpoint, File file, String dir, Util.Progress p) throws IOException {
        String target = normDir(dir) + ("/".equals(normDir(dir)) ? "" : "/") + file.getName();
        for (Entry entry : listAll(endpoint))
            if (entry.path.equalsIgnoreCase(target)) throw new IOException("云盘已出现同名文件，请检查歌单后重试");
        String result = Util.upload(uploadLink(endpoint, dir) + "?ret-json=1", dir, file, p);
        if (result.contains("\"error\"")) throw new IOException("云盘上传失败");
        try {
            JSONArray reply = new JSONArray(result);
            if (reply.length() > 0 && reply.optJSONObject(0) != null
                && !file.getName().equals(reply.getJSONObject(0).optString("name", file.getName())))
                throw new IOException("服务器改了上传文件名，请检查云端副本后重试");
        } catch (org.json.JSONException ignored) { /* 不同 Seafile 版本返回格式不同，随后核对文件名和大小 */ }
    }

    /** 云端指定目录里是否已有同名文件，有就返回它的完整路径。 */
    private static String findSameName(String endpoint, String dir, String name) {
        try {
            for (Entry entry : listAll(endpoint)) {
                if (entry.dir) continue;
                if (!entry.name.equals(name)) continue;
                String parent = "/";
                int slash = entry.path.lastIndexOf('/');
                if (slash > 0) parent = entry.path.substring(0, slash);
                String want = dir.equals("/") ? "/" : (dir.endsWith("/") ? dir.substring(0, dir.length() - 1) : dir);
                if (parent.equals(want)) return entry.path;
            }
        } catch (Exception e) {
            // 查不到就当没有，交给上传本身处理
        }
        return null;
    }

    public static boolean canDelete(String endpoint) {
        return isToken(endpoint);
    }

    /** 删除云盘文件（仅令牌模式；删除后进云盘回收站）。 */
    public static void delete(String endpoint, String cloudPath) throws IOException {
        if (!canDelete(endpoint)) throw new IOException("删除云端文件需要 API 令牌");
        String parent = "/";
        int slash = cloudPath.lastIndexOf('/');
        String name = cloudPath;
        if (slash > 0) {
            parent = cloudPath.substring(0, slash);
            name = cloudPath.substring(slash + 1);
        } else if (slash == 0) {
            name = cloudPath.substring(1);
        }
        JSONObject body = new JSONObject();
        try {
            body.put("parent_dir", parent);
            body.put("dirents", new JSONArray().put(name));
        } catch (Exception e) {
            throw new IOException(e.getMessage());
        }
        String url = host(endpoint) + "/api/v2.1/via-repo-token/batch-delete-item/";
        Util.request("DELETE", url, auth(endpoint), body.toString());
    }

    private static String unquote(String text) {
        String t = text;
        if (t.length() >= 2 && t.charAt(0) == '"' && t.charAt(t.length() - 1) == '"') {
            t = t.substring(1, t.length() - 1);
        }
        return t.replace("\\/", "/");
    }

    // ---------- 歌单（一个云盘文件夹＝一个歌单） ----------

    /** 路径规范化：保证以 / 开头、去掉末尾多余的 /。 */
    public static String normDir(String dir) {
        if (dir == null || dir.length() == 0) return "/";
        String d = dir.trim();
        if (!d.startsWith("/")) d = "/" + d;
        while (d.length() > 1 && d.endsWith("/")) d = d.substring(0, d.length() - 1);
        return d;
    }

    /** 从云盘路径里取出歌单名（根目录下的文件返回空串）。 */
    public static String playlistOf(String cloudPath) {
        if (cloudPath == null) return "";
        String p = cloudPath.startsWith("/") ? cloudPath.substring(1) : cloudPath;
        int slash = p.indexOf('/');
        if (slash <= 0) return "";
        return p.substring(0, slash);
    }

    /** 歌单名里不能出现的字符（云盘按文件名建目录，会直接失败）。 */
    public static boolean badName(String name) {
        if (name == null) return true;
        String t = name.trim();
        if (t.length() == 0) return true;
        String bad = "/\\:*?\"<>|";
        for (int i = 0; i < t.length(); i++) {
            if (bad.indexOf(t.charAt(i)) >= 0) return true;
        }
        return false;
    }

    /** 云盘上某个目录存不存在。 */
    public static boolean dirExists(String endpoint, String dirPath) {
        try {
            if (!isToken(endpoint)) {
                String url = host(endpoint) + "/api/v2.1/share-links/" + token(endpoint)
                    + "/dirents/?path=%2F" + encodePath(normDir(dirPath));
                return parse(Util.getString(url, null)).has("dirent_list");
            }
            String url = host(endpoint) + "/api/v2.1/via-repo-token/dir/?path="
                    + enc(normDir(dirPath)) + "&recursive=0";
            parse(Util.getString(url, auth(endpoint)));
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    /**
     * 确保云盘上某个目录存在（新建歌单用它）。
     * 文件夹令牌没有 mkdir 接口，但上传接口支持 relative_path 递归建子目录：
     * 传一个占位文件进去、再把占位文件删掉，目录就留下了。
     */
    public static void ensureDir(String endpoint, String dirPath, File tempDir) throws IOException {
        String dir = normDir(dirPath);
        if (dir.equals("/")) return;
        if (dirExists(endpoint, dir)) return;

        File probe = new File(tempDir, "skylark-dir-probe.tmp");
        FileOutputStream out = new FileOutputStream(probe);
        try {
            out.write("placeholder".getBytes("UTF-8"));
        } finally {
            out.close();
        }
        try {
            String link = uploadLink(endpoint, "/") + "?ret-json=1";
            Util.uploadTo(link, "/", dir.substring(1) + "/", probe, null);
        } finally {
            probe.delete();
        }
        try {
            delete(endpoint, dir + "/skylark-dir-probe.tmp");
        } catch (Exception ignored) {
            // 删不掉也不影响：占位文件不是音频/歌词，扫描时会忽略
        }
    }

    private static String batchBody(String srcDir, List<String> names, String dstDir) throws IOException {
        JSONObject body = new JSONObject();
        try {
            JSONArray arr = new JSONArray();
            for (int i = 0; i < names.size(); i++) arr.put(names.get(i));
            body.put("src_parent_dir", normDir(srcDir));
            body.put("src_dirents", arr);
            body.put("dst_parent_dir", normDir(dstDir));
        } catch (Exception e) {
            throw new IOException(e.getMessage());
        }
        return body.toString();
    }

    private static void postByToken(String endpoint, String api, String body) throws IOException {
        String url = host(endpoint) + "/api/v2.1/via-repo-token/" + api;
        String result = Util.request("POST", url, auth(endpoint), body);
        if (result != null && result.indexOf("\"error\"") >= 0) throw new IOException(Util.shorten(result));
    }

    /** 服务端批量移动（不重传，秒完成；目标目录必须已存在）。 */
    public static void moveItems(String endpoint, String srcDir, List<String> names, String dstDir) throws IOException {
        if (names.isEmpty()) return;
        postByToken(endpoint, "sync-batch-move-item/", batchBody(srcDir, names, dstDir));
    }

    /** 服务端批量复制（同一首歌进两个歌单＝两处各放一份文件）。 */
    public static void copyItems(String endpoint, String srcDir, List<String> names, String dstDir) throws IOException {
        if (names.isEmpty()) return;
        postByToken(endpoint, "sync-batch-copy-item/", batchBody(srcDir, names, dstDir));
    }

    /** 移动一个文件夹（歌单改名用）。 */
    public static void moveDir(String endpoint, String dirName, String dstParentDir) throws IOException {
        JSONObject body = new JSONObject();
        try {
            body.put("src_parent_dir", "/");
            body.put("src_dirent_name", dirName);
            body.put("dst_parent_dir", normDir(dstParentDir));
        } catch (Exception e) {
            throw new IOException(e.getMessage());
        }
        postByToken(endpoint, "move-dir/", body.toString());
    }

    /** 批量删除（按所在目录分组，每组一个请求）。 */
    public static void deleteAll(String endpoint, List<String> cloudPaths) throws IOException {
        Map<String, List<String>> groups = new LinkedHashMap<String, List<String>>();
        for (int i = 0; i < cloudPaths.size(); i++) {
            String p = normDir(cloudPaths.get(i));
            int slash = p.lastIndexOf('/');
            String parent = slash <= 0 ? "/" : p.substring(0, slash);
            String name = p.substring(slash + 1);
            if (name.length() == 0) continue;
            List<String> list = groups.get(parent);
            if (list == null) {
                list = new ArrayList<String>();
                groups.put(parent, list);
            }
            list.add(name);
        }
        for (Map.Entry<String, List<String>> entry : groups.entrySet()) {
            JSONObject body = new JSONObject();
            try {
                JSONArray arr = new JSONArray();
                for (int i = 0; i < entry.getValue().size(); i++) arr.put(entry.getValue().get(i));
                body.put("parent_dir", entry.getKey());
                body.put("dirents", arr);
            } catch (Exception e) {
                throw new IOException(e.getMessage());
            }
            String url = host(endpoint) + "/api/v2.1/via-repo-token/batch-delete-item/";
            Util.request("DELETE", url, auth(endpoint), body.toString());
        }
    }
}
