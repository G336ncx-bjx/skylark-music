package com.skylark.music;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.ProgressDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.res.Configuration;
import android.database.Cursor;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.LayerDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.OpenableColumns;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.view.inputmethod.InputMethodManager;
import android.widget.AdapterView;
import android.widget.BaseAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

/** 主界面：音乐库、播放队列、歌词、设置，共用一个底部播放条。 */

public abstract class AppShell extends Activity {

    protected static final int REQ_PICK = 101;
    protected static final int REQ_NOTIFY = 102;
    protected static final int REQ_STORAGE = 103;

    /** 与 AndroidManifest.xml 的 versionName 保持一致。 */
    public static final String VERSION = "3.3.23";

    /** 系统播放器（MediaPlayer）原生支持的格式：mp3 / m4a / aac / wav / wma / flac / ogg / opus。 */
    protected static final String[] AUDIO_EXT = { "mp3", "m4a", "aac", "wav", "wma", "flac", "ogg", "oga", "opus" };
    protected static final String[] IGNORED_EXT = { "ape", "wv", "aif", "aiff", "mp4", "mkv", "avi", "m4v" };

    /** 重建界面（换主题）后回到原来的标签页。 */
    protected static int lastTab = 0;

    // 调色板
    protected boolean dark;
    protected int cBg, cSurface, cAlt, cText, cDim, cAccent, cDivider, cDanger, cTrack;

    protected final Handler ui = new Handler(Looper.getMainLooper());

    protected final TextView[] tabs = new TextView[4];
    protected final View[] tabLines = new View[4];
    protected final View[] pages = new View[4];
    protected int tabIndex = 0;

    // 音乐库
    protected EditText search;
    protected ListView libList;
    protected TextView libInfo, libEmpty;
    protected Button sortButton;
    protected SongAdapter libAdapter;
    protected final List<Song> shown = new ArrayList<Song>();
    /** 歌单筛选：空串＝全部歌单。 */
    protected String playlistFilter = "";
    /** 音乐库分两层：false＝歌单（文件夹）列表，true＝某个歌单里的歌曲列表。 */
    protected boolean inPlaylist;
    protected LinearLayout libHome, libFolders, libListPanel, libHeadRow, libSelHead, libSelActions, libSearchRow;
    protected TextView libTitle, libSelCount, libHomeInfo;
    protected Button libSelAll;

    // 播放队列
    protected ListView queueList;
    protected TextView queueInfo, queueEmpty;
    protected SongAdapter queueAdapter;
    protected LinearLayout queueHeadRow, queueSelHead, queueSelActions;
    protected View queueTipsRow;
    protected TextView queueSelCount;
    protected Button queueSelAll;

    // 歌词
    protected TextView lyricTitle, lyricArtist, lyricState, offsetLabel;
    protected LinearLayout lyricBox;
    protected ScrollView lyricScroll;
    protected final List<LinearLayout> lyricRows = new ArrayList<LinearLayout>();
    protected final List<Lrc.Line> lyricLines = new ArrayList<Lrc.Line>();
    protected boolean lyricsSynced;
    protected int lyricIndex = -1;
    protected String lyricLoadedPath = "";
    protected String lyricPendingPath = "";
    protected long manualScrollUntil = 0;
    /**
     * 正在做「自动滚动到当前句」的动画。这期间不更新「回到当前歌词」按钮的显隐，
     * 否则动画途中取景短暂偏离当前句，按钮会闪一下。
     */
    protected boolean autoScrolling;
    /** 歌词页浮动的「回到当前歌词」按钮（滚离当前句时才出现）。 */
    protected TextView backToCurrent;

    // 设置
    protected EditText linkInput, tokenInput;
    protected Switch cacheSwitch;
    protected TextView connInfo, cacheInfo, hiddenInfo, updateStatus;
    protected final TextView[] themeButtons = new TextView[3];
    protected TextView cacheModeHint;

    // 底部播放条
    protected TextView nowTitle, nowArtist, timeNow, timeTotal, modeText;
    protected SeekBar seek;
    protected ImageView playIcon;
    protected boolean dragging;

    protected int scanToken = 0;
    protected boolean uploading;
    protected boolean starting;

    /** 云盘操作统一入口：后台跑，完事重扫曲库。 */
    protected interface CloudTask {
        void run(String endpoint) throws Exception;
    }

    protected final Runnable ticker = new Runnable() {
        public void run() {
            updateProgress();
            ui.postDelayed(this, 500);
        }
    };

    /**
     * 歌词单独用更快的节拍刷新：进度条每 500ms 刷一次就够了，但歌词跟着这个节拍走
     * 会最多晚半秒才跳到下一句（听起来就是「唱到了才慢慢换句」）。
     */
    protected final Runnable lyricTicker = new Runnable() {
        public void run() {
            if (PlayerService.instance != null && !lyricRows.isEmpty()) {
                applyLyricHighlight(PlayerService.instance.position());
            }
            ui.postDelayed(this, 120);
        }
    };


    protected int dp(float value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }


    protected Drawable round(int color, float radius) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(radius));
        return g;
    }


    protected Drawable roundStroke(int color, float radius, int strokeColor, float width) {
        GradientDrawable g = new GradientDrawable();
        g.setColor(color);
        g.setCornerRadius(dp(radius));
        g.setStroke(Math.max(1, dp(width)), strokeColor);
        return g;
    }


    protected LinearLayout row() {
        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.HORIZONTAL);
        l.setGravity(Gravity.CENTER_VERTICAL);
        return l;
    }


    protected LinearLayout column() {
        LinearLayout l = new LinearLayout(this);
        l.setOrientation(LinearLayout.VERTICAL);
        return l;
    }


    protected TextView text(String value, float size, int color) {
        TextView v = new TextView(this);
        v.setText(value);
        v.setTextSize(size);
        v.setTextColor(color);
        v.setLineSpacing(dp(2), 1f);
        return v;
    }


    protected Button button(String label, boolean primary, View.OnClickListener click) {
        Button b = new Button(this);
        b.setText(label);
        b.setAllCaps(false);
        b.setTextSize(13);
        b.setTextColor(primary ? 0xFFFFFFFF : cText);
        b.setBackground(round(primary ? cAccent : cAlt, 10));
        b.setPadding(dp(12), 0, dp(12), 0);
        b.setMinWidth(0);
        b.setMinimumWidth(0);
        b.setMinimumHeight(0);
        b.setMinHeight(0);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, dp(36));
        p.rightMargin = dp(8);
        b.setLayoutParams(p);
        if (Build.VERSION.SDK_INT >= 21) b.setStateListAnimator(null);
        if (click != null) b.setOnClickListener(click);
        return b;
    }


    protected Button wideButton(String label, boolean primary, View.OnClickListener click) {
        Button b = button(label, primary, click);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, dp(38), 1f);
        p.rightMargin = dp(8);
        b.setLayoutParams(p);
        return b;
    }


    protected EditText input(String hint, String value) {
        EditText e = new EditText(this);
        e.setHint(hint);
        e.setHintTextColor(cDim);
        e.setTextColor(cText);
        e.setTextSize(14);
        e.setSingleLine(true);
        e.setText(value == null ? "" : value);
        e.setBackground(round(cAlt, 10));
        e.setPadding(dp(12), dp(10), dp(12), dp(10));
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        p.bottomMargin = dp(8);
        e.setLayoutParams(p);
        return e;
    }


    protected LinearLayout card() {
        LinearLayout c = column();
        c.setBackground(round(cSurface, 14));
        c.setPadding(dp(14), dp(14), dp(14), dp(14));
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        p.bottomMargin = dp(12);
        c.setLayoutParams(p);
        return c;
    }


    protected TextView cardTitle(String value) {
        TextView t = text(value, 15, cText);
        t.setTypeface(null, Typeface.BOLD);
        t.setPadding(0, 0, 0, dp(8));
        return t;
    }


    protected TextView hint(String value) {
        TextView t = text(value, 12, cDim);
        t.setPadding(0, 0, 0, dp(10));
        return t;
    }


    protected void hideKeyboard() {
        View focus = getCurrentFocus();
        if (focus == null) return;
        InputMethodManager imm = (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
        if (imm != null) imm.hideSoftInputFromWindow(focus.getWindowToken(), 0);
    }


    protected static Song songAt(List<Song> list, int position) {
        if (position < 0 || position >= list.size()) return null;
        return list.get(position);
    }


    // ---------------------------------------------------------------- 多选管理

    protected Button growButton(String label, boolean primary, View.OnClickListener click) {
        Button b = button(label, primary, click);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, dp(38), 1f);
        p.rightMargin = dp(8);
        b.setLayoutParams(p);
        return b;
    }


    protected void updateSelectionBars() {
        if (libSelCount != null && libAdapter != null) {
            libSelCount.setText("已选 " + libAdapter.pickedCount() + " 首");
            if (libSelAll != null)
                libSelAll.setText(libAdapter.allPicked() ? "取消全选" : "全选");
        }
        if (queueSelCount != null && queueAdapter != null) {
            queueSelCount.setText("已选 " + queueAdapter.pickedCount() + " 首");
            if (queueSelAll != null)
                queueSelAll.setText(queueAdapter.allPicked() ? "取消全选" : "全选");
        }
    }


    protected static String lrcName(Song song) {
        String name = song.fileName;
        int dot = name.lastIndexOf('.');
        return (dot > 0 ? name.substring(0, dot) : name) + ".lrc";
    }


    protected static String mimeOf(String name) {
        String lower = name.toLowerCase(Locale.ROOT);
        if (lower.endsWith(".m4a") || lower.endsWith(".mp4")) return "audio/mp4";
        if (lower.endsWith(".aac")) return "audio/aac";
        if (lower.endsWith(".wav")) return "audio/wav";
        if (lower.endsWith(".wma")) return "audio/x-ms-wma";
        if (lower.endsWith(".flac")) return "audio/flac";
        if (lower.endsWith(".ogg") || lower.endsWith(".oga")) return "audio/ogg";
        if (lower.endsWith(".opus")) return "audio/opus";
        return "audio/mpeg";
    }


    protected void updateSortButton() {
        if (sortButton == null) return;
        String[] names = { "歌名", "歌手", "时长", "云盘顺序" };
        int mode = Prefs.sortMode(this);
        sortButton.setText("排序：" + names[Math.max(0, Math.min(3, mode))]);
        sortButton.setTextSize(13);
    }


    protected String safe(String text) {
        return text == null ? "" : text;
    }


    protected void updateLibraryInfo(String message) {
        if (libInfo == null) return;
        if (message != null) {
            libInfo.setText(message);
            if (libHomeInfo != null) libHomeInfo.setText(message);
            return;
        }
        if (Store.songs.isEmpty()) {
            String empty = Store.scanning ? "正在读取云端目录…"
                    : (Store.endpoint.length() == 0 ? "还没有连接云盘" : "云盘里还没有音频文件");
            libInfo.setText(empty);
            if (libHomeInfo != null) libHomeInfo.setText(empty);
            return;
        }
        if (libHomeInfo != null) {
            libHomeInfo.setText(Store.scanning ? "正在读取云端目录…"
                    : "共 " + Store.songs.size() + " 首"
                        + (Store.skippedUnsupported > 0
                            ? "，已忽略 " + Store.skippedUnsupported + " 个不支持的文件" : ""));
        }
        int cached = 0;
        for (int i = 0; i < shown.size(); i++) {
            if (Store.isCached(this, shown.get(i))) cached++;
        }
        StringBuilder sb = new StringBuilder();
        sb.append("共 ").append(Store.songs.size()).append(" 首");
        if (shown.size() != Store.songs.size()) sb.append("，筛出 ").append(shown.size()).append(" 首");
        if (cached > 0) sb.append("，已缓存 ").append(cached).append(" 首");
        if (Store.skippedUnsupported > 0) {
            sb.append("，已忽略 ").append(Store.skippedUnsupported).append(" 个不支持的文件");
        }
        if (Store.status.length() > 0) sb.append(" · ").append(Store.status);
        libInfo.setText(sb.toString());
    }


    protected void refreshQueue() {
        if (queueAdapter == null) return;
        queueAdapter.setData(new ArrayList<Song>(Store.queue));
        if (queueInfo != null) {
            if (Store.queue.isEmpty()) {
                queueInfo.setText("播放队列");
            } else {
                queueInfo.setText("播放队列 · " + Store.queue.size() + " 首 · 第 "
                        + (Store.index + 1) + " 首");
            }
        }
        if (queueEmpty != null) {
            boolean empty = Store.queue.isEmpty();
            queueEmpty.setVisibility(empty ? View.VISIBLE : View.GONE);
            queueList.setVisibility(empty ? View.GONE : View.VISIBLE);
        }
    }


    protected void updateOffsetLabel(Song song) {
        if (offsetLabel == null) return;
        double value = Store.lyricOffset(this, song);
        offsetLabel.setText(String.format(Locale.ROOT, "偏移 %+.1f 秒", value));
    }


    protected void refreshLyrics(boolean force) {
        final Song song = Store.current();
        if (lyricTitle == null) return;
        if (song == null) {
            lyricTitle.setText("还没有播放歌曲");
            lyricArtist.setText("");
            lyricState.setText("");
            clearLyricRows("点「音乐库」里任意一首歌开始播放，歌词会自动出现。");
            return;
        }
        lyricTitle.setText(song.title);
        lyricArtist.setText(song.artistText() + (song.hasLyrics() ? "" : " · 没有歌词文件"));
        updateOffsetLabel(song);

        if (song.cloudPath.equals(lyricLoadedPath) && !force) {
            applyLyricHighlight(PlayerService.instance == null ? 0 : PlayerService.instance.position());
            return;
        }
        if (!song.hasLyrics()) {
            lyricLoadedPath = song.cloudPath;
            clearLyricRows("这首歌旁边没有同名的 .lrc 歌词文件。\n把歌词文件按「歌名 - 歌手.lrc」命名后放到同一目录即可。");
            return;
        }
        if (song.cloudPath.equals(lyricPendingPath) && !force) return;

        if (force) {
            File cached = Store.lyricCacheFile(this, song);
            if (cached.exists()) cached.delete();
        }
        File cache = Store.lyricCacheFile(this, song);
        if (!force && cache.exists() && cache.length() > 0) {
            setLyrics(song, readFile(cache));
            return;
        }
        lyricPendingPath = song.cloudPath;
        lyricState.setText("正在获取歌词…");
        clearLyricRows("正在从云端取歌词…");
        final String endpoint = Store.endpoint;
        new Thread(new Runnable() {
            public void run() {
                try {
                    final String text = Cloud.downloadText(endpoint, song.lyricPath);
                    writeFile(Store.lyricCacheFile(AppShell.this, song), text);
                    // 歌词也和音频一样：默认只留正在听的和下一首
                    Store.pruneLyricCache(AppShell.this);
                    ui.post(new Runnable() {
                        public void run() {
                            lyricPendingPath = "";
                            setLyrics(song, text);
                        }
                    });
                } catch (final Exception e) {
                    ui.post(new Runnable() {
                        public void run() {
                            lyricPendingPath = "";
                            lyricLoadedPath = song.cloudPath;
                            lyricState.setText("");
                            clearLyricRows("歌词获取失败：" + Util.shorten(e.getMessage()));
                        }
                    });
                }
            }
        }).start();
    }


    protected void setLyrics(Song song, String text) {
        Lrc parsed = Lrc.parse(text);
        lyricLoadedPath = song.cloudPath;
        lyricsSynced = parsed.synced;
        lyricLines.clear();
        lyricLines.addAll(parsed.lines);
        lyricIndex = -1;
        autoScrolling = false;
        manualScrollUntil = 0;
        if (backToCurrent != null) backToCurrent.setVisibility(View.GONE);
        lyricBox.removeAllViews();
        lyricRows.clear();
        if (lyricLines.isEmpty()) {
            lyricState.setText("");
            clearLyricRows("歌词文件是空的。");
            return;
        }
        lyricState.setText(parsed.synced ? "" : "这是纯文本歌词，不跟时间走");
        final int pad = Math.max(dp(60), lyricScroll.getHeight() / 2 - dp(40));
        lyricBox.setPadding(dp(16), pad, dp(16), pad);
        for (int i = 0; i < lyricLines.size(); i++) {
            final Lrc.Line line = lyricLines.get(i);
            LinearLayout item = column();
            item.setPadding(0, dp(9), 0, dp(9));
            TextView original = text(line.text, 16, cText);
            // 歌词行居中显示（和电脑版歌词页一致）
            original.setGravity(Gravity.CENTER);
            item.addView(original, new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            if (line.hasTranslation()) {
                TextView trans = text(line.translation, 14, cDim);
                trans.setGravity(Gravity.CENTER);
                trans.setPadding(0, dp(3), 0, 0);
                item.addView(trans, new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));
            }
            final int index = i;
            item.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    PlayerService service = PlayerService.instance;
                    if (service == null || !lyricsSynced) return;
                    // 点某一句＝跳过去听，顺便恢复自动跟随
                    manualScrollUntil = 0;
                    backToCurrent.setVisibility(View.GONE);
                    double offset = Store.lyricOffset(AppShell.this, Store.current());
                    service.seekTo(Math.max(0, line.time - offset));
                }
            });
            lyricBox.addView(item);
            lyricRows.add(item);
        }
        applyLyricHighlight(PlayerService.instance == null ? 0 : PlayerService.instance.position());
    }


    protected void clearLyricRows(String message) {
        if (lyricBox == null) return;
        lyricBox.removeAllViews();
        lyricRows.clear();
        lyricLines.clear();
        lyricsSynced = false;
        TextView t = text(message, 14, cDim);
        t.setGravity(Gravity.CENTER);
        t.setPadding(0, dp(40), 0, dp(40));
        lyricBox.addView(t);
    }


    protected void applyLyricHighlight(double position) {
        if (lyricRows.isEmpty() || !lyricsSynced) return;
        Song song = Store.current();
        if (song == null) return;
        double offset = Store.lyricOffset(this, song);
        int index = 0;
        double time = position - offset;
        for (int i = 0; i < lyricLines.size(); i++) {
            if (lyricLines.get(i).time <= time) index = i;
            else break;
        }
        if (index == lyricIndex) {
            updateBackToCurrent();
            return;
        }
        int previous = lyricIndex;
        lyricIndex = index;
        if (previous >= 0 && previous < lyricRows.size()) {
            setLineState(lyricRows.get(previous), false);
        }
        setLineState(lyricRows.get(index), true);
        if (System.currentTimeMillis() < manualScrollUntil) {
            updateBackToCurrent();
            return;
        }
        scrollToCurrentLine(true);
        updateBackToCurrent();
    }


    /** 平滑滚到当前正在唱的那一句（「回到当前歌词」按钮和自动跟随都用它）。 */
    protected void scrollToCurrentLine(boolean smooth) {
        if (lyricRows.isEmpty() || lyricIndex < 0 || lyricIndex >= lyricRows.size()) return;
        View target = lyricRows.get(lyricIndex);
        int top = target.getTop() - (lyricScroll.getHeight() - target.getHeight()) / 2;
        if (top < 0) top = 0;
        autoScrolling = true;
        if (smooth) lyricScroll.smoothScrollTo(0, top);
        else lyricScroll.scrollTo(0, top);
        // 动画大概几百毫秒，结束后再允许按钮跟随滚动位置更新
        lyricScroll.postDelayed(new Runnable() {
            public void run() {
                autoScrolling = false;
                updateBackToCurrent();
            }
        }, 700);
    }


    /**
     * 滚动位置离当前句太远时，浮出「回到当前歌词」按钮；回到当前句附近就收起来。
     * 安卓这边手动滑动只是暂停 4 秒跟随，所以这个按钮主要是让用户随时能一键回到当前句。
     */
    protected void updateBackToCurrent() {
        if (backToCurrent == null || lyricRows.isEmpty() || lyricIndex < 0
                || lyricIndex >= lyricRows.size()) return;
        View target = lyricRows.get(lyricIndex);
        int want = target.getTop() - (lyricScroll.getHeight() - target.getHeight()) / 2;
        if (want < 0) want = 0;
        // 自动滚动动画途中不更新，避免「换行的一瞬间闪一下」
        if (autoScrolling) return;
        boolean away = Math.abs(lyricScroll.getScrollY() - want) > dp(40);
        backToCurrent.setVisibility(away ? View.VISIBLE : View.GONE);
    }


    protected void setLineState(LinearLayout item, boolean active) {
        for (int i = 0; i < item.getChildCount(); i++) {
            View child = item.getChildAt(i);
            if (!(child instanceof TextView)) continue;
            TextView t = (TextView) child;
            if (active) {
                t.setTextColor(cAccent);
                t.setTextSize(i == 0 ? 17 : 15);
            } else {
                t.setTextColor(i == 0 ? cText : cDim);
                t.setTextSize(i == 0 ? 16 : 14);
            }
        }
    }


    protected String readFile(File file) {
        try {
            byte[] data = new byte[(int) file.length()];
            java.io.FileInputStream in = new java.io.FileInputStream(file);
            int read = 0;
            while (read < data.length) {
                int n = in.read(data, read, data.length - read);
                if (n <= 0) break;
                read += n;
            }
            in.close();
            return Util.decodeText(data);
        } catch (Exception e) {
            return "";
        }
    }


    protected void writeFile(File file, String text) {
        try {
            File parent = file.getParentFile();
            if (parent != null && !parent.exists()) parent.mkdirs();
            FileOutputStream out = new FileOutputStream(file);
            out.write(text.getBytes("UTF-8"));
            out.close();
        } catch (Exception e) {
            // 写不进缓存不影响播放
        }
    }


    protected String extOf(String name) {
        if (name == null) return "";
        int dot = name.lastIndexOf('.');
        if (dot < 0 || dot == name.length() - 1) return "";
        return name.substring(dot + 1).toLowerCase(Locale.ROOT);
    }


    protected String baseOf(String name) {
        if (name == null) return "";
        int dot = name.lastIndexOf('.');
        return dot < 0 ? name : name.substring(0, dot);
    }


    protected boolean isAudio(String ext) {
        for (int i = 0; i < AUDIO_EXT.length; i++) {
            if (AUDIO_EXT[i].equals(ext)) return true;
        }
        return false;
    }


    protected boolean isIgnored(String ext) {
        for (int i = 0; i < IGNORED_EXT.length; i++) {
            if (IGNORED_EXT[i].equals(ext)) return true;
        }
        return false;
    }


    protected Drawable progressDrawable() {
        GradientDrawable track = new GradientDrawable();
        track.setColor(cTrack);
        track.setCornerRadius(dp(4));
        track.setSize(-1, dp(8));
        GradientDrawable fill = new GradientDrawable();
        fill.setColor(cAccent);
        fill.setCornerRadius(dp(4));
        fill.setSize(-1, dp(8));
        LayerDrawable layer = new LayerDrawable(new Drawable[] { track, fill });
        layer.setId(0, android.R.id.background);
        layer.setId(1, android.R.id.progress);
        // 槽与已播放部分用同样的上下内缩，保证两端对齐（14 = 18dp 圆点 - 4dp 轨道）
        layer.setLayerInset(0, 0, dp(7), 0, dp(7));
        layer.setLayerInset(1, 0, dp(7), 0, dp(7));
        return layer;
    }


    protected Drawable thumbDrawable() {
        GradientDrawable thumb = new GradientDrawable();
        thumb.setShape(GradientDrawable.OVAL);
        thumb.setColor(cAccent);
        thumb.setStroke(dp(2), cSurface);
        thumb.setSize(dp(18), dp(18));
        return thumb;
    }


    protected ImageView iconButton(int res, int size, boolean filled) {
        ImageView v = new ImageView(this);
        v.setImageResource(res);
        int pad = dp(filled ? 12 : 8);
        v.setPadding(pad, pad, pad, pad);
        v.setColorFilter(filled ? 0xFFFFFFFF : cText);
        if (filled) v.setBackground(round(cAccent, 100));
        v.setClickable(true);
        v.setFocusable(true);
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(dp(size), dp(size));
        p.leftMargin = dp(10);
        p.rightMargin = dp(10);
        v.setLayoutParams(p);
        return v;
    }


    protected double currentDuration() {
        Song song = Store.current();
        if (song != null && song.duration > 0) return song.duration;
        PlayerService service = PlayerService.instance;
        return service == null ? 0 : service.duration();
    }


    protected void refreshNowPlaying() {
        if (nowTitle == null) return;
        Song song = Store.current();
        if (song == null) {
            nowTitle.setText("还没有播放歌曲");
            nowArtist.setText("到「音乐库」点歌名就会开始播放");
        } else {
            nowTitle.setText(song.title);
            PlayerService service = PlayerService.instance;
            if (service != null && service.isBuffering()) nowArtist.setText(service.bufferingText());
            else nowArtist.setText(song.artistText() + " · " + Store.modeName(Store.mode));
        }
        if (modeText != null) modeText.setText(Store.modeName(Store.mode));
        PlayerService service = PlayerService.instance;
        boolean playing = service != null && service.isPlaying();
        playIcon.setImageResource(playing ? R.drawable.ic_pause : R.drawable.ic_play);
        updateProgress();
    }


    protected void updateProgress() {
        if (seek == null) return;
        PlayerService service = PlayerService.instance;
        double total = currentDuration();
        double position = service == null ? 0 : service.position();
        if (!dragging) {
            int value = total > 0 ? (int) Math.round(position / total * 1000) : 0;
            seek.setProgress(Math.max(0, Math.min(1000, value)));
            timeNow.setText(Util.formatTime(position));
        }
        timeTotal.setText(total > 0 ? Util.formatTime(total) : "--:--");
        if (tabIndex == 2) {
            Song song = Store.current();
            String path = song == null ? "" : song.cloudPath;
            if (!path.equals(lyricLoadedPath) && !path.equals(lyricPendingPath)) refreshLyrics(false);
            else applyLyricHighlight(position);
        }
    }

    // ---------------------------------------------------------------- 杂项

    protected void startPlayerService() {
        if (starting) return;
        starting = true;
        try {
            Intent intent = new Intent(this, PlayerService.class);
            if (Build.VERSION.SDK_INT >= 26) startForegroundService(intent);
            else startService(intent);
        } catch (Exception e) {
            toast("启动播放服务失败：" + Util.shorten(e.getMessage()));
        }
        ui.postDelayed(new Runnable() {
            public void run() {
                starting = false;
            }
        }, 1500);
    }


    protected void toast(String message) {
        if (message == null || message.length() == 0) return;
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show();
    }


    protected void toastAsync(final String message) {
        ui.post(new Runnable() {
            public void run() {
                toast(message);
            }
        });
    }


    protected void postInfo(final String message) {
        ui.post(new Runnable() {
            public void run() {
                updateLibraryInfo(message);
            }
        });
    }

}
