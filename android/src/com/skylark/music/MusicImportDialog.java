package com.skylark.music;

import android.app.AlertDialog;
import android.content.DialogInterface;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.ValueCallback;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.ArrayAdapter;
import android.widget.TextView;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.security.MessageDigest;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

/** 原生搜索和入库界面；网站验证及公开接口只在正常 WebView 会话中运行。 */
public final class MusicImportDialog {
    private static final String SITE = "https://flac.music.hi.cn/";
    private final SettingsScreen host;
    private final String endpoint, target;
    private WebView web;
    private AlertDialog dialog, verification;
    private TextView status;
    private LinearLayout root, results;
    private EditText keyword;
    private Spinner platform;
    private Button search, more;
    private volatile boolean closed, busy;
    private int page;
    private String activeKeyword, activePlatform;

    public MusicImportDialog(SettingsScreen activity) {
        host = activity; endpoint = Store.endpoint; target = host.uploadTargetDir();
    }

    public void show() {
        if (endpoint.length() == 0) { host.toast("先在设置里连接云盘"); return; }
        root = host.column(); root.setPadding(host.dp(16), host.dp(8), host.dp(16), host.dp(8));
        root.addView(host.text("极高音质 MP3 320K + LRC · 上传到 " + target + "\n成功后删除下载文件；失败保留，可选同一首重试。", 12, host.cDim));
        LinearLayout line = host.row();
        platform = new Spinner(host);
        platform.setAdapter(new ArrayAdapter<String>(host, android.R.layout.simple_spinner_dropdown_item, new String[] { "酷我", "网易云" }));
        line.addView(platform);
        keyword = new EditText(host); keyword.setSingleLine(true); keyword.setHint("歌名或歌手");
        keyword.setTextColor(host.cText); keyword.setHintTextColor(host.cDim);
        line.addView(keyword, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1));
        search = host.button("搜索", true, new View.OnClickListener() { public void onClick(View v) { load(1); } });
        line.addView(search); root.addView(line);
        status = host.text("首次使用请点「网站验证」，完成后再搜索", 12, host.cDim); root.addView(status);
        results = host.column(); ScrollView scroll = new ScrollView(host); scroll.addView(results);
        root.addView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, host.dp(270)));
        LinearLayout footer = host.row();
        more = host.button("加载更多", false, new View.OnClickListener() { public void onClick(View v) { load(page + 1); } });
        more.setEnabled(false); footer.addView(more);
        footer.addView(host.button("网站验证", false, new View.OnClickListener() { public void onClick(View v) { verify(); } }));
        root.addView(footer);
        web = new WebView(host);
        web.getSettings().setJavaScriptEnabled(true); web.getSettings().setDomStorageEnabled(true);
        web.getSettings().setAllowFileAccess(false); web.getSettings().setAllowContentAccess(false);
        web.setWebViewClient(new WebViewClient() {
            @Override public boolean shouldOverrideUrlLoading(WebView view, String url) {
                try {
                    java.net.URI uri = new java.net.URI(url);
                    return !("https".equals(uri.getScheme()) && ("flac.music.hi.cn".equals(uri.getHost()) || "challenge.rivers.chaitin.cn".equals(uri.getHost())));
                } catch (Exception e) { return true; }
            }
        });
        root.addView(web, new LinearLayout.LayoutParams(1, 1)); web.loadUrl(SITE);
        dialog = new AlertDialog.Builder(host).setTitle("在线找歌").setView(root).setNegativeButton("关闭", null).create();
        dialog.setOnDismissListener(new DialogInterface.OnDismissListener() {
            public void onDismiss(DialogInterface d) {
                closed = true;
                if (verification != null) verification.dismiss();
                web.stopLoading(); web.destroy();
            }
        });
        dialog.show();
    }

    private void verify() {
        if (busy || host.uploading) { host.toast("请等待当前任务完成"); return; }
        root.removeView(web);
        verification = new AlertDialog.Builder(host).setTitle("音乐网站验证")
            .setView(web).setPositiveButton("验证完成，返回搜索", null).create();
        verification.setOnDismissListener(new DialogInterface.OnDismissListener() {
            public void onDismiss(DialogInterface d) {
                if (web.getParent() instanceof ViewGroup) ((ViewGroup) web.getParent()).removeView(web);
                if (!closed) root.addView(web, new LinearLayout.LayoutParams(1, 1));
            }
        });
        verification.show(); verification.getWindow().setLayout(ViewGroup.LayoutParams.MATCH_PARENT, host.dp(540));
    }

    private void report(final String message) {
        host.ui.post(new Runnable() { public void run() { if (!closed) status.setText(message); host.postInfo(message); } });
    }

    // 工作线程调用；只返回 JSON 字符串，不暴露可被网页调用的 native bridge。
    private Object call(final String action, final JSONObject fields) throws Exception {
        final CountDownLatch ready = new CountDownLatch(1);
        final String[] answer = new String[1];
        final String slot = "__skylark_" + Long.toHexString(System.nanoTime());
        final String script = "(function(){window['" + slot + "']=null;if(location.origin!==" + JSONObject.quote(SITE.substring(0, SITE.length()-1))
            + "){window['" + slot + "']=JSON.stringify({__error:'请完成网站验证'});return;}"
            + "fetch('/ajax.php?act=" + action + "',{method:'POST',headers:{'Content-Type':'application/x-www-form-urlencoded;charset=UTF-8'},body:Object.keys("
            + fields.toString() + ").map(function(k){return encodeURIComponent(k)+'='+encodeURIComponent(" + fields.toString()
            + "[k]);}).join('&')}).then(function(r){return r.text();}).then(function(t){window['" + slot
            + "']=t;}).catch(function(){window['" + slot + "']=JSON.stringify({__error:'网站请求失败，请重试'});});})()";
        final long deadline = android.os.SystemClock.elapsedRealtime() + 65000;
        host.ui.post(new Runnable() {
            public void run() {
                if (closed) { ready.countDown(); return; }
                web.evaluateJavascript(script, null);
                host.ui.postDelayed(new Runnable() {
                    public void run() {
                        if (closed || android.os.SystemClock.elapsedRealtime() > deadline) { ready.countDown(); return; }
                        final Runnable poll = this;
                        web.evaluateJavascript("window['" + slot + "']", new ValueCallback<String>() {
                            public void onReceiveValue(String value) {
                                if (value == null || "null".equals(value)) { host.ui.postDelayed(poll, 200); return; }
                                try { answer[0] = new JSONArray("[" + value + "]").getString(0); } catch (Exception ignored) { }
                                web.evaluateJavascript("delete window['" + slot + "']", null); ready.countDown();
                            }
                        });
                    }
                }, 200);
            }
        });
        if (!ready.await(70, TimeUnit.SECONDS) || answer[0] == null) throw new IOException("网站响应超时，请完成验证后重试");
        JSONObject reply;
        try { reply = new JSONObject(answer[0]); } catch (Exception e) { throw new IOException("请点「网站验证」，完成验证后重新搜索"); }
        if (reply.has("__error")) throw new IOException(reply.getString("__error"));
        if (reply.optInt("code", -1) != 0) throw new IOException(reply.optString("msg", "音乐网站返回异常"));
        return reply.get("data");
    }

    private static JSONObject extreme(JSONObject song) throws IOException {
        JSONArray formats = song.optJSONArray("minfo");
        for (int i = 0; formats != null && i < formats.length(); i++) {
            JSONObject q = formats.optJSONObject(i);
            if (q != null && "mp3".equals(q.optString("format")) && "320".equals(q.optString("bitrate"))) return q;
        }
        throw new IOException("这首歌没有极高音质（MP3 320K），请选择其他结果或平台");
    }

    private void load(final int requestedPage) {
        if (busy || host.uploading) { host.toast("还有任务正在进行"); return; }
        if (requestedPage == 1) {
            activeKeyword = keyword.getText().toString().trim(); activePlatform = platform.getSelectedItemPosition() == 0 ? "kuwo" : "wyy";
            if (activeKeyword.length() == 0) { status.setText("请输入歌名或歌手"); return; }
            results.removeAllViews();
        }
        final String query = activeKeyword, source = activePlatform;
        busy = true; search.setEnabled(false); more.setEnabled(false); report("正在搜索…");
        new Thread(new Runnable() {
            public void run() {
                JSONArray songs = null; String error = null;
                try {
                    JSONObject fields = new JSONObject(); fields.put("platform", source); fields.put("keyword", query);
                    fields.put("page", requestedPage); fields.put("size", 20);
                    songs = ((JSONObject)call("search", fields)).getJSONArray("list");
                } catch (Exception e) { error = e.getMessage(); }
                final JSONArray found = songs; final String failure = error;
                host.ui.post(new Runnable() {
                    public void run() {
                        busy = false; if (closed) return; search.setEnabled(true);
                        if (failure != null) { status.setText(failure); more.setEnabled(requestedPage > 1); return; }
                        page = requestedPage; more.setEnabled(found.length() == 20);
                        status.setText(found.length() == 0 ? "没有更多结果，可换关键词或平台" : "第 " + page + " 页 · 选择正确的歌曲版本");
                        for (int i = 0; i < found.length(); i++) {
                            final JSONObject song = found.optJSONObject(i); if (song == null) continue;
                            LinearLayout row = host.column(); row.setPadding(0, host.dp(8), 0, host.dp(8));
                            row.addView(host.text(song.optString("name") + " - " + song.optString("artist"), 14, host.cText));
                            row.addView(host.text(song.optString("album_name"), 11, host.cDim));
                            Button add = host.button("下载并入库", true, new View.OnClickListener() { public void onClick(View v) { add(song, source); } });
                            try { extreme(song); } catch (Exception e) { add.setEnabled(false); add.setText("无极高音质"); }
                            row.addView(add); results.addView(row);
                        }
                    }
                });
            }
        }, "music-search").start();
    }

    private void add(final JSONObject song, final String source) {
        if (busy || host.uploading) { host.toast("还有任务正在进行"); return; }
        host.uploading = true; dialog.setCancelable(false); dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setEnabled(false);
        new Thread(new Runnable() {
            public void run() {
                String message; File dir = null;
                try {
                    String name = Util.safeMusicName(song.optString("name")) + " - " + Util.safeMusicName(song.optString("artist"));
                    byte[] hash = MessageDigest.getInstance("SHA-256").digest((endpoint + "\n" + target + "\n" + source + "\n" + song.optString("id")).getBytes("UTF-8"));
                    StringBuilder key = new StringBuilder(); for (byte b : hash) key.append(String.format(java.util.Locale.ROOT, "%02x", b & 255));
                    dir = new File(host.getFilesDir(), "music-import/" + key);
                    if (!dir.isDirectory() && !dir.mkdirs()) throw new IOException("无法创建下载目录");
                    File audio = new File(dir, name + ".mp3"), lyric = new File(dir, name + ".lrc");
                    JSONObject fields = new JSONObject(); fields.put("platform", source); fields.put("songid", song.optString("id"));
                    fields.put("time", song.optString("time")); fields.put("sign", song.optString("sign")); extreme(song);
                    if (!lyric.exists()) {
                        report("正在获取歌词…"); String text = (String)call("getLyric", fields); Util.validateMusicLyric(text);
                        FileOutputStream out = new FileOutputStream(lyric); try { out.write(text.getBytes("UTF-8")); } finally { out.close(); }
                    }
                    FileInputStream lyricsIn = new FileInputStream(lyric);
                    try { Util.validateMusicLyric(Util.readAll(lyricsIn, 4 * 1024 * 1024)); } finally { lyricsIn.close(); }
                    if (!audio.exists()) {
                        report("正在解析极高音质…"); fields.put("format", "mp3"); fields.put("bitrate", "320");
                        String url = ((JSONObject)call("getUrl", fields)).getString("url");
                        java.net.URI uri = new java.net.URI(url);
                        if (!"http".equals(uri.getScheme()) && !"https".equals(uri.getScheme())) throw new IOException("音乐下载地址无效");
                        try {
                            Util.downloadTo(url, null, audio, new Util.Progress() {
                                private int last = -1;
                                public void onProgress(long done, long total) {
                                    if (done > 200L * 1024 * 1024) throw new IllegalStateException("音频超过 200 MB");
                                    int percent = total > 0 ? (int)(done * 100 / total) : 0;
                                    if (percent != last) { last = percent; report("正在下载极高音质：" + percent + "%"); }
                                    if (total > 0 && done > total) throw new IllegalStateException("下载大小异常");
                                }
                            });
                            validateAudio(audio);
                        } catch (Exception e) { audio.delete(); new File(audio + ".part").delete(); throw e; }
                    }
                    validateAudio(audio);
                    report("正在检查云盘目标…"); Cloud.ensureDir(endpoint, target, host.getCacheDir());
                    MusicImportFiles.uploadPair(new File[] { audio, lyric }, new MusicImportFiles.Destination() {
                        public Long size(String fileName) throws IOException {
                            Cloud.Entry entry = find(fileName);
                            return entry == null ? null : Long.valueOf(entry.size);
                        }
                        public void upload(File file) throws IOException {
                        final String fileName = file.getName();
                        report("正在上传：" + fileName);
                        Cloud.uploadNew(endpoint, file, target, new Util.Progress() {
                            private int last = -1;
                            public void onProgress(long done, long total) {
                                int percent = total > 0 ? (int)(done * 100 / total) : 0;
                                if (percent != last) { last = percent; report("正在上传：" + fileName + " " + percent + "%"); }
                            }
                        });
                        }
                    });
                    if (!dir.delete()) throw new IOException("云端入库成功，本地临时目录清理失败");
                    message = "已入库「" + name + "」，本次下载文件已删除";
                } catch (Exception e) { message = "入库未完成：" + e.getMessage() + (dir == null ? "" : "。本地文件保留，选择同一首可重试"); }
                final String result = message;
                host.ui.post(new Runnable() {
                    public void run() {
                        host.uploading = false;
                        if (!closed) { status.setText(result); dialog.setCancelable(true); dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setEnabled(true); }
                        host.toast(result); host.startScan(true);
                    }
                });
            }
        }, "music-import").start();
    }

    private void validateAudio(File file) throws IOException {
        FileInputStream in = new FileInputStream(file);
        try {
            byte[] tag = new byte[10];
            if (in.read(tag) == 10 && tag[0] == 'I' && tag[1] == 'D' && tag[2] == '3') {
                long offset = 10L + ((tag[6] & 127) << 21) + ((tag[7] & 127) << 14) + ((tag[8] & 127) << 7) + (tag[9] & 127);
                if (tag[3] == 4 && (tag[5] & 16) != 0) offset += 10;
                if (offset >= file.length()) throw new IOException("音频的 ID3 标签不完整");
                in.getChannel().position(offset);
            } else in.getChannel().position(0);
            byte[] head = new byte[65536]; int length = in.read(head); Util.validateMusicHead(head, length);
        }
        finally { in.close(); }
    }
    private Cloud.Entry find(String name) throws IOException {
        String path = (target.equals("/") ? "/" : target + "/") + name;
        List<Cloud.Entry> entries = Cloud.listAll(endpoint);
        for (Cloud.Entry entry : entries) if (!entry.dir && entry.path.equalsIgnoreCase(path)) return entry;
        return null;
    }
}
