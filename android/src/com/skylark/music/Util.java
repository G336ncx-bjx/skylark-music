package com.skylark.music;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.Charset;
import java.util.ArrayList;
import java.util.List;

/** 工具集：文件名解析、时间格式、HTTP、MP3 时长解析。 */
public class Util {

    public static final String UA =
            "Mozilla/5.0 (Linux; Android) Skylark/3.1 (+https://github.com/G336ncx-bjx/skylark-music)";

    // ---------- 文本 ----------

    public static String safeMusicName(String text) throws IOException {
        String name = text.trim().replaceAll("[\\x00-\\x1f<>:\"/\\\\|?*]", "_").replaceAll("[ .]+$", "");
        if (name.length() == 0) throw new IOException("歌名或歌手为空，无法按规则命名");
        return name.length() > 50 ? name.substring(0, 50).replaceAll("[ .]+$", "") : name;
    }

    public static String cleanMusicTitle(String title) {
        String promo = "主题曲|主题歌|片头曲|片头歌|片尾曲|片尾歌|插曲|推广曲|宣传曲|印象曲|预告曲|原声带";
        String media = "电视连续剧|电视剧|影视剧|网络剧|网剧|电影|影片|动画片|动画|动漫|纪录片|综艺|音乐剧|游戏|手游";
        String text = (title == null ? "" : title).trim().replaceAll(
            "\\([^()]*?(?:" + promo + ")[^()]*\\)|（[^（）]*?(?:" + promo + ")[^（）]*）|【[^【】]*?(?:" + promo + ")[^【】]*】|\\[[^\\[\\]]*?(?:" + promo + ")[^\\[\\]]*\\]", "").trim();
        java.util.regex.Matcher marker = java.util.regex.Pattern.compile(promo).matcher(text);
        if (!marker.find()) return text;
        String prefix = text.substring(0, marker.start()); int start = -1;
        java.util.regex.Matcher separator = java.util.regex.Pattern.compile("\\s+[-–—:：|/]\\s*|[-–—:：|/]\\s*(?=[《〈「『])").matcher(prefix);
        while (separator.find()) start = separator.start();
        if (start < 0) {
            for (char c : new char[] { '《', '〈', '「', '『' }) start = Math.max(start, prefix.lastIndexOf(c));
            if (start > 0) {
                java.util.regex.Matcher description = java.util.regex.Pattern.compile("(?:" + media + ")\\s*$").matcher(prefix.substring(0, start));
                if (description.find()) start = description.start();
            } else {
                start = -1;
                java.util.regex.Matcher description = java.util.regex.Pattern.compile(media).matcher(prefix);
                while (description.find()) if (description.start() > 0) start = description.start();
                if (start < 0) for (char c : new char[] { '-', '–', '—', ':', '：', '|', '/' }) start = Math.max(start, prefix.lastIndexOf(c));
                if (start < 0) start = Math.max(prefix.lastIndexOf(' '), prefix.lastIndexOf('\t'));
            }
        }
        String cleaned = start > 0 ? text.substring(0, start).replaceAll("[ \\t–—:：|/·-]+$", "") : text;
        return cleaned.length() > 0 ? cleaned : text;
    }

    public static void validateMusicLyric(String text) throws IOException {
        if (text == null || !java.util.regex.Pattern.compile("\\[\\d{1,3}:\\d{2}(?:[.:]\\d+)?\\]").matcher(text).find())
            throw new IOException("网站没有返回有效的 LRC 歌词，未上传歌曲");
    }

    public static void validateMusicHead(byte[] head, int length) throws IOException {
        for (int i = 0; i + 4 < length; i++) {
            if ((head[i] & 255) == 255 && (head[i + 1] & 254) == 250 && ((head[i + 2] & 255) >> 4) == 14
                    && (head[i + 2] & 12) != 12) {
                int rate = new int[] { 44100, 48000, 32000 }[((head[i + 2] & 255) >> 2) & 3];
                int next = i + 144000 * 320 / rate + ((head[i + 2] >> 1) & 1);
                if (next + 4 < length && (head[next] & 255) == 255 && (head[next + 1] & 254) == 250
                    && ((head[next + 2] & 255) >> 4) == 14) return;
            }
        }
        throw new IOException("下载内容不是 MP3 320K 音频，请重新获取地址");
    }

    /** "歌名 - 歌手" 拆成标题与歌手（歌手里再出现分隔符时保留原样）。 */
    public static String[] parseSongName(String nameNoExt) {
        String title = nameNoExt == null ? "" : nameNoExt.trim();
        String[] seps = new String[] { " - ", " – ", " — ", "-", "–", "—" };
        for (int i = 0; i < seps.length; i++) {
            int idx = title.indexOf(seps[i]);
            if (idx <= 0) continue;
            String[] parts = title.split(java.util.regex.Pattern.quote(seps[i]));
            if (parts.length < 2) continue;
            String name = parts[0].trim();
            if (name.length() == 0) continue;
            StringBuilder artist = new StringBuilder();
            for (int k = 1; k < parts.length; k++) {
                String piece = parts[k].trim();
                if (piece.length() == 0) continue;
                if (artist.length() > 0) artist.append(' ').append(seps[i].trim()).append(' ');
                artist.append(piece);
            }
            if (artist.length() == 0) continue;
            return new String[] { name, artist.toString() };
        }
        return new String[] { title, "" };
    }

    public static String formatTime(double seconds) {
        if (seconds <= 0) return "0:00";
        int total = (int) seconds;
        return String.format("%d:%02d", total / 60, total % 60);
    }

    /** BOM / UTF-8 / GBK 依次尝试。 */
    public static String decodeText(byte[] bytes) {
        if (bytes == null || bytes.length == 0) return "";
        if (bytes.length >= 3 && (bytes[0] & 0xFF) == 0xEF && (bytes[1] & 0xFF) == 0xBB && (bytes[2] & 0xFF) == 0xBF) {
            return new String(bytes, 3, bytes.length - 3, Charset.forName("UTF-8"));
        }
        if (bytes.length >= 2 && (bytes[0] & 0xFF) == 0xFF && (bytes[1] & 0xFF) == 0xFE) {
            return new String(bytes, 2, bytes.length - 2, Charset.forName("UTF-16LE"));
        }
        try {
            Charset utf8 = Charset.forName("UTF-8");
            java.nio.charset.CharsetDecoder dec = utf8.newDecoder();
            dec.onMalformedInput(java.nio.charset.CodingErrorAction.REPORT);
            dec.onUnmappableCharacter(java.nio.charset.CodingErrorAction.REPORT);
            return dec.decode(java.nio.ByteBuffer.wrap(bytes)).toString();
        } catch (Exception e) {
            try {
                return new String(bytes, Charset.forName("GBK"));
            } catch (Exception e2) {
                return new String(bytes, Charset.forName("UTF-8"));
            }
        }
    }

    // ---------- HTTP ----------

    public interface Progress {
        void onProgress(long done, long total);
    }

    private static HttpURLConnection open(String url, String[] headers) throws IOException {
        HttpURLConnection conn = (HttpURLConnection) new URL(url).openConnection();
        conn.setRequestProperty("User-Agent", UA);
        conn.setConnectTimeout(15000);
        conn.setReadTimeout(60000);
        conn.setInstanceFollowRedirects(true);
        if (headers != null) {
            for (int i = 0; i + 1 < headers.length; i += 2) conn.setRequestProperty(headers[i], headers[i + 1]);
        }
        return conn;
    }

    public static String getString(String url, String[] headers) throws IOException {
        HttpURLConnection conn = open(url, headers);
        try {
            int code = conn.getResponseCode();
            InputStream in = code >= 400 ? conn.getErrorStream() : conn.getInputStream();
            String body = in == null ? "" : readAll(in, 4 * 1024 * 1024);
            if (code >= 400) throw new IOException("HTTP " + code + ": " + shorten(body));
            return body;
        } finally {
            conn.disconnect();
        }
    }

    public static byte[] getBytes(String url, String[] headers, int maxBytes) throws IOException {
        HttpURLConnection conn = open(url, headers);
        try {
            int code = conn.getResponseCode();
            if (code >= 400) throw new IOException("HTTP " + code);
            InputStream in = conn.getInputStream();
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[16384];
            int total = 0;
            int n;
            while ((n = in.read(buf)) > 0 && total < maxBytes) {
                int write = Math.min(n, maxBytes - total);
                out.write(buf, 0, write);
                total += write;
            }
            return out.toByteArray();
        } finally {
            conn.disconnect();
        }
    }

    public static void downloadTo(String url, String[] headers, File target, Progress progress) throws IOException {
        HttpURLConnection conn = open(url, headers);
        try {
            int code = conn.getResponseCode();
            if (code >= 400) throw new IOException("HTTP " + code);
            long total = conn.getContentLength();
            File parent = target.getParentFile();
            if (parent != null && !parent.exists()) parent.mkdirs();
            File temp = new File(target.getAbsolutePath() + ".part");
            InputStream in = conn.getInputStream();
            FileOutputStream out = new FileOutputStream(temp);
            byte[] buf = new byte[65536];
            long done = 0;
            int n;
            try {
                while ((n = in.read(buf)) > 0) {
                    out.write(buf, 0, n);
                    done += n;
                    if (progress != null) progress.onProgress(done, total);
                }
            } finally {
                out.close();
                in.close();
            }
            if (total >= 0 && done != total) throw new IOException("文件下载不完整");
            if (target.exists()) target.delete();
            if (!temp.renameTo(target)) throw new IOException("无法写入缓存文件");
        } finally {
            conn.disconnect();
        }
    }

    /** multipart/form-data 上传一个文件。 */
    public static String upload(String uploadUrl, String parentDir, File file, Progress progress) throws IOException {
        return uploadTo(uploadUrl, parentDir, "", file, progress);
    }

    /**
     * multipart/form-data 上传一个文件；relativePath 非空时服务端会按它递归建子目录
     * （文件夹令牌没有 mkdir 接口，云雀靠这一招建歌单文件夹）。
     */
    public static String uploadTo(String uploadUrl, String parentDir, String relativePath,
                                  File file, Progress progress) throws IOException {
        String boundary = "----Skylark" + System.currentTimeMillis();
        HttpURLConnection conn = open(uploadUrl, new String[] { "Content-Type", "multipart/form-data; boundary=" + boundary });
        conn.setRequestMethod("POST");
        conn.setDoOutput(true);
        conn.setChunkedStreamingMode(65536);
        OutputStream out = conn.getOutputStream();
        long size = file.length();
        try {
            write(out, "--" + boundary + "\r\n"
                    + "Content-Disposition: form-data; name=\"parent_dir\"\r\n\r\n" + parentDir + "\r\n");
            write(out, "--" + boundary + "\r\n"
                    + "Content-Disposition: form-data; name=\"relative_path\"\r\n\r\n"
                    + (relativePath == null ? "" : relativePath) + "\r\n");
            write(out, "--" + boundary + "\r\n"
                    + "Content-Disposition: form-data; name=\"file\"; filename=\"" + file.getName() + "\"\r\n"
                    + "Content-Type: application/octet-stream\r\n\r\n");
            InputStream in = new java.io.FileInputStream(file);
            byte[] buf = new byte[65536];
            long done = 0;
            int n;
            try {
                while ((n = in.read(buf)) > 0) {
                    out.write(buf, 0, n);
                    done += n;
                    if (progress != null) progress.onProgress(done, size);
                }
            } finally {
                in.close();
            }
            write(out, "\r\n--" + boundary + "--\r\n");
        } finally {
            out.close();
        }
        int code = conn.getResponseCode();
        InputStream in = code >= 400 ? conn.getErrorStream() : conn.getInputStream();
        String body = in == null ? "" : readAll(in, 1024 * 1024);
        conn.disconnect();
        if (code >= 400) throw new IOException("HTTP " + code + ": " + shorten(body));
        if (body.contains("\"error\"")) throw new IOException(shorten(body));
        return body;
    }

    public static String request(String method, String url, String[] headers, String jsonBody) throws IOException {
        HttpURLConnection conn = open(url, headers);
        conn.setRequestMethod(method);
        if (jsonBody != null) {
            conn.setDoOutput(true);
            conn.setRequestProperty("Content-Type", "application/json");
            byte[] data = jsonBody.getBytes(Charset.forName("UTF-8"));
            conn.setFixedLengthStreamingMode(data.length);
            OutputStream out = conn.getOutputStream();
            try {
                out.write(data);
            } finally {
                out.close();
            }
        }
        try {
            int code = conn.getResponseCode();
            InputStream in = code >= 400 ? conn.getErrorStream() : conn.getInputStream();
            String body = in == null ? "" : readAll(in, 1024 * 1024);
            if (code >= 400) throw new IOException("HTTP " + code + ": " + shorten(body));
            return body;
        } finally {
            conn.disconnect();
        }
    }

    private static void write(OutputStream out, String text) throws IOException {
        out.write(text.getBytes(Charset.forName("UTF-8")));
    }

    public static String readAll(InputStream in, int limit) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[16384];
        int total = 0;
        int n;
        while ((n = in.read(buf)) > 0 && total < limit) {
            int write = Math.min(n, limit - total);
            out.write(buf, 0, write);
            total += write;
        }
        return Util.decodeText(out.toByteArray());
    }

    public static String shorten(String text) {
        if (text == null) return "";
        String t = text.replace('\n', ' ').replace('\r', ' ').trim();
        return t.length() > 160 ? t.substring(0, 160) : t;
    }

    public static List<String> splitLines(String text) {
        List<String> out = new ArrayList<String>();
        if (text == null) return out;
        String[] parts = text.replace("\r\n", "\n").replace('\r', '\n').split("\n");
        for (int i = 0; i < parts.length; i++) {
            String line = parts[i].trim();
            if (line.length() > 0) out.add(line);
        }
        return out;
    }

    // ---------- MP3 时长 ----------

    private static final int[] BR_V1L1 = {0,32,64,96,128,160,192,224,256,288,320,352,384,416,448,0};
    private static final int[] BR_V1L2 = {0,32,48,56,64,80,96,112,128,160,192,224,256,320,384,0};
    private static final int[] BR_V1L3 = {0,32,40,48,56,64,80,96,112,128,160,192,224,256,320,0};
    private static final int[] BR_V2L1 = {0,32,48,56,64,80,96,112,128,144,160,176,192,224,256,0};
    private static final int[] BR_V2L23 = {0,8,16,24,32,40,48,56,64,80,96,112,128,144,160,0};
    private static final int[] SR_V1 = {44100,48000,32000,0};
    private static final int[] SR_V2 = {22050,24000,16000,0};
    private static final int[] SR_V25 = {11025,12000,8000,0};

    /** 由文件头（前 256KB 即可）与文件总长算 MP3 时长。 */
    public static double mp3Duration(byte[] buf, long fileSize) {
        if (buf == null || buf.length < 16) return 0;
        int start = 0;
        if (buf[0] == 'I' && buf[1] == 'D' && buf[2] == '3' && buf.length > 10) {
            int b6 = buf[6] & 0x7F, b7 = buf[7] & 0x7F, b8 = buf[8] & 0x7F, b9 = buf[9] & 0x7F;
            long size = ((long) b6 << 21) | ((long) b7 << 14) | ((long) b8 << 7) | (long) b9;
            start = (int) (size + 10);
            if ((buf[5] & 0x10) != 0) start += 10;
        }
        if (start < 0 || start >= buf.length - 4) return 0;

        int found = -1;
        for (int i = start; i + 4 <= buf.length; i++) {
            if ((buf[i] & 0xFF) != 0xFF || (buf[i + 1] & 0xE0) != 0xE0) continue;
            int version = (buf[i + 1] >> 3) & 0x03;
            int layer = (buf[i + 1] >> 1) & 0x03;
            int brIdx = (buf[i + 2] >> 4) & 0x0F;
            int srIdx = (buf[i + 2] >> 2) & 0x03;
            if (version == 1 || layer == 0 || brIdx == 0 || brIdx == 15 || srIdx == 3) continue;
            found = i;
            break;
        }
        if (found < 0) return 0;

        int v = (buf[found + 1] >> 3) & 0x03;
        int lay = (buf[found + 1] >> 1) & 0x03;
        int brIdx = (buf[found + 2] >> 4) & 0x0F;
        int srIdx = (buf[found + 2] >> 2) & 0x03;
        int padding = (buf[found + 2] >> 1) & 0x01;
        int channelMode = (buf[found + 3] >> 6) & 0x03;

        int bitrate, sampleRate, samplesPerFrame;
        if (v == 3) {
            bitrate = lay == 3 ? BR_V1L1[brIdx] : (lay == 2 ? BR_V1L2[brIdx] : BR_V1L3[brIdx]);
            sampleRate = SR_V1[srIdx];
            samplesPerFrame = lay == 3 ? 384 : 1152;
        } else {
            bitrate = lay == 3 ? BR_V2L1[brIdx] : BR_V2L23[brIdx];
            sampleRate = v == 2 ? SR_V2[srIdx] : SR_V25[srIdx];
            samplesPerFrame = lay == 3 ? 384 : 576;
        }
        if (bitrate <= 0 || sampleRate <= 0) return 0;

        int xingOff = found + (v == 3 ? (channelMode == 3 ? 17 : 32) : (channelMode == 3 ? 9 : 17));
        if (xingOff + 12 <= buf.length) {
            boolean xing = (buf[xingOff] == 'X' && buf[xingOff + 1] == 'i' && buf[xingOff + 2] == 'n' && buf[xingOff + 3] == 'g')
                    || (buf[xingOff] == 'I' && buf[xingOff + 1] == 'n' && buf[xingOff + 2] == 'f' && buf[xingOff + 3] == 'o');
            if (xing) {
                int flags = be32(buf, xingOff + 4);
                if ((flags & 0x01) != 0) {
                    long frames = be32(buf, xingOff + 8) & 0xFFFFFFFFL;
                    if (frames > 0) return frames * (double) samplesPerFrame / sampleRate;
                }
            }
        }
        for (int k = 0; k < 2; k++) {
            int vbri = found + (k == 0 ? 36 : 32);
            if (vbri + 18 <= buf.length && buf[vbri] == 'V' && buf[vbri + 1] == 'B' && buf[vbri + 2] == 'R' && buf[vbri + 3] == 'I') {
                long frames = be32(buf, vbri + 14) & 0xFFFFFFFFL;
                if (frames > 0) return frames * (double) samplesPerFrame / sampleRate;
            }
        }
        long audioStart = start + found;
        return (fileSize - audioStart) * 8.0 / (bitrate * 1000.0);
    }

    private static int be32(byte[] b, int i) {
        return ((b[i] & 0xFF) << 24) | ((b[i + 1] & 0xFF) << 16) | ((b[i + 2] & 0xFF) << 8) | (b[i + 3] & 0xFF);
    }
}
