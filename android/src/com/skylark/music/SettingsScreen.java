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

public abstract class SettingsScreen extends PlayerScreen {

    // ---------------------------------------------------------------- 音乐库页

    protected View buildLibraryPage() {
        LinearLayout page = column();
        page.setPadding(dp(12), dp(12), dp(12), 0);

        // ================= 第一层：歌单（云盘上的文件夹） =================
        libHome = column();
        LinearLayout homeHead = row();
        TextView homeTitle = text("音乐库", 18, cText);
        homeTitle.setTypeface(null, Typeface.BOLD);
        homeHead.addView(homeTitle, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        homeHead.addView(button("刷新", false, new View.OnClickListener() {
            public void onClick(View v) {
                startScan(false);
            }
        }));
        libHome.addView(homeHead);
        TextView homeHint = text("点歌单进去看歌；长按歌单可以改名 / 删除。", 12, cDim);
        LinearLayout.LayoutParams homeHintP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        homeHintP.topMargin = dp(6);
        homeHint.setLayoutParams(homeHintP);
        libHome.addView(homeHint);

        libFolders = column();
        ScrollView folderScroll = new ScrollView(this);
        folderScroll.setVerticalScrollBarEnabled(false);
        folderScroll.addView(libFolders);
        LinearLayout.LayoutParams folderP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        folderP.topMargin = dp(12);
        libHome.addView(folderScroll, folderP);

        libHomeInfo = text("", 12, cDim);
        LinearLayout.LayoutParams homeInfoP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        homeInfoP.topMargin = dp(10);
        libHomeInfo.setLayoutParams(homeInfoP);
        libHome.addView(libHomeInfo);
        page.addView(libHome, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        // ================= 第二层：某个歌单里的歌曲 =================
        libListPanel = column();
        libListPanel.setVisibility(View.GONE);

        // 第一行：返回 + 歌单名 + 播放 + 上传（多选时换成：完成 + 已选 + 全选）
        libHeadRow = row();
        libHeadRow.addView(button("返回", false, new View.OnClickListener() {
            public void onClick(View v) {
                backToFolders();
            }
        }));
        libTitle = text("全部歌曲", 15, cText);
        libTitle.setTypeface(null, Typeface.BOLD);
        libTitle.setSingleLine(true);
        libTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);
        libHeadRow.addView(libTitle, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        libHeadRow.addView(button("播放", false, new View.OnClickListener() {
            public void onClick(View v) {
                playAll();
            }
        }));
        libHeadRow.addView(button("找歌", false, new View.OnClickListener() {
            public void onClick(View v) {
                new MusicImportDialog(SettingsScreen.this).show();
            }
        }));
        libHeadRow.addView(button("上传", true, new View.OnClickListener() {
            public void onClick(View v) {
                pickFiles();
            }
        }));
        libListPanel.addView(libHeadRow);

        libSelHead = row();
        libSelHead.setVisibility(View.GONE);
        libSelHead.addView(button("完成", false, new View.OnClickListener() {
            public void onClick(View v) {
                setLibSelecting(false);
            }
        }));
        libSelCount = text("已选 0 首", 13, cText);
        libSelHead.addView(libSelCount, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        libSelAll = button("全选", false, new View.OnClickListener() {
            public void onClick(View v) {
                toggleSelectAll(libAdapter, libSelAll);
            }
        });
        libSelHead.addView(libSelAll);
        libListPanel.addView(libSelHead);

        // 第二行：搜索 + 排序（多选时换成：下载 / 加入歌单 / 删除）
        libSearchRow = row();
        LinearLayout.LayoutParams searchRowP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        searchRowP.topMargin = dp(8);
        libSearchRow.setLayoutParams(searchRowP);
        search = new EditText(this);
        search.setHint("搜索歌名、歌手");
        search.setHintTextColor(cDim);
        search.setTextColor(cText);
        search.setTextSize(14);
        search.setSingleLine(true);
        search.setBackground(round(cAlt, 12));
        search.setPadding(dp(14), dp(8), dp(14), dp(8));
        LinearLayout.LayoutParams sp = new LinearLayout.LayoutParams(0, dp(42), 1f);
        sp.rightMargin = dp(8);
        search.setLayoutParams(sp);
        search.addTextChangedListener(new TextWatcher() {
            public void beforeTextChanged(CharSequence s, int a, int b, int c) { }
            public void onTextChanged(CharSequence s, int a, int b, int c) { }
            public void afterTextChanged(Editable s) {
                applyFilter();
            }
        });
        libSearchRow.addView(search);
        sortButton = button("排序", false, new View.OnClickListener() {
            public void onClick(View v) {
                showSortDialog();
            }
        });
        libSearchRow.addView(sortButton);
        libListPanel.addView(libSearchRow);

        libSelActions = row();
        libSelActions.setVisibility(View.GONE);
        LinearLayout.LayoutParams selActionsP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        selActionsP.topMargin = dp(8);
        libSelActions.setLayoutParams(selActionsP);
        libSelActions.addView(growButton("下载", true, new View.OnClickListener() {
            public void onClick(View v) {
                askDownloadScope(libAdapter.pickedSongs());
            }
        }));
        libSelActions.addView(growButton("加入歌单", false, new View.OnClickListener() {
            public void onClick(View v) {
                addSelectionToPlaylist(libAdapter.pickedSongs());
            }
        }));
        libSelActions.addView(growButton("删除", false, new View.OnClickListener() {
            public void onClick(View v) {
                confirmDeleteMany(libAdapter.pickedSongs());
            }
        }));
        libListPanel.addView(libSelActions);

        libInfo = text("", 12, cDim);
        LinearLayout.LayoutParams infoP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        infoP.topMargin = dp(8);
        libInfo.setLayoutParams(infoP);
        libListPanel.addView(libInfo);

        libEmpty = text("还没有歌曲。先在「设置」里连接云盘，再回来点「刷新」。", 13, cDim);
        libEmpty.setGravity(Gravity.CENTER);
        libEmpty.setPadding(dp(20), dp(40), dp(20), dp(40));
        libListPanel.addView(libEmpty, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        libList = new ListView(this);
        libList.setDivider(new android.graphics.drawable.ColorDrawable(cDivider));
        libList.setDividerHeight(1);
        libList.setBackgroundColor(cSurface);
        libList.setSelector(new android.graphics.drawable.ColorDrawable(cAlt));
        LinearLayout.LayoutParams listP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        listP.topMargin = dp(8);
        libList.setLayoutParams(listP);
        libList.setOnItemClickListener(new AdapterView.OnItemClickListener() {
            public void onItemClick(AdapterView<?> parent, View view, int position, long id) {
                if (libAdapter.isSelecting()) {
                    libAdapter.toggle(songAt(shown, position));
                    updateSelectionBars();
                    return;
                }
                playFromLibrary(position);
            }
        });
        libList.setOnItemLongClickListener(new AdapterView.OnItemLongClickListener() {
            public boolean onItemLongClick(AdapterView<?> parent, View view, int position, long id) {
                if (libAdapter.isSelecting()) {
                    showLibraryMenu(position);
                    return true;
                }
                setLibSelecting(true);
                libAdapter.toggle(songAt(shown, position));
                updateSelectionBars();
                return true;
            }
        });
        libAdapter = new SongAdapter(this, false);
        libList.setAdapter(libAdapter);
        libListPanel.addView(libList);
        page.addView(libListPanel, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        return page;
    }


    /** 把选中的歌复制进另一个歌单（一个歌单＝云盘上一个文件夹）。 */
    protected void addSelectionToPlaylist(final List<Song> songs) {
        if (songs == null || songs.isEmpty()) {
            toast("先选中歌曲");
            return;
        }
        final List<String> names = playlistNames();
        names.add("＋ 新建歌单…");
        new AlertDialog.Builder(this)
                .setTitle("把 " + songs.size() + " 首加入歌单")
                .setItems(names.toArray(new String[0]), new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        if (which == names.size() - 1) newPlaylistFor(songs);
                        else copyToPlaylist(songs, names.get(which));
                    }
                })
                .show();
    }


    protected void newPlaylistFor(final List<Song> songs) {
        final EditText field = input("歌单名", "");
        new AlertDialog.Builder(this)
                .setTitle("新建歌单")
                .setView(field)
                .setNegativeButton("取消", null)
                .setPositiveButton("创建并加入", new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        String name = field.getText().toString().trim();
                        if (Cloud.badName(name)) {
                            toast("这个名字不能用在文件夹上");
                            return;
                        }
                        copyToPlaylist(songs, name);
                    }
                })
                .show();
    }


    protected void showLibraryMenu(final int position) {
        if (position < 0 || position >= shown.size()) return;
        final Song song = shown.get(position);
        final boolean canDelete = Cloud.canDelete(Store.endpoint);
        final List<String> items = new ArrayList<String>();
        items.add("立即播放");
        items.add("下一首播放");
        items.add("加到队列末尾");
        items.add("从音乐库移除");
        if (canDelete) items.add("从云盘删除");
        new AlertDialog.Builder(this)
                .setTitle(song.title + " · " + song.artistText())
                .setItems(items.toArray(new String[0]), new DialogInterface.OnClickListener() {
                    public void onClick(DialogInterface dialog, int which) {
                        String action = items.get(which);
                        if ("立即播放".equals(action)) {
                            playFromLibrary(position);
                        } else if ("下一首播放".equals(action)) {
                            enqueueToService(song, true);
                        } else if ("加到队列末尾".equals(action)) {
                            enqueueToService(song, false);
                        } else if ("从音乐库移除".equals(action)) {
                            hideFromLibrary(song);
                        } else {
                            confirmCloudDelete(song);
                        }
                    }
                })
                .show();
    }


    // ---------------------------------------------------------------- 播放队列页

    protected View buildQueuePage() {
        LinearLayout page = column();
        page.setPadding(dp(12), dp(12), dp(12), 0);

        LinearLayout head = row();
        queueHeadRow = head;
        queueInfo = text("播放队列", 14, cText);
        head.addView(queueInfo, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        head.addView(button("清空", false, new View.OnClickListener() {
            public void onClick(View v) {
                confirmClearQueue();
            }
        }));
        page.addView(head);

        // 多选时，上面那一行换成「完成 + 已选 + 全选」
        queueSelHead = row();
        queueSelHead.setVisibility(View.GONE);
        queueSelHead.addView(button("完成", false, new View.OnClickListener() {
            public void onClick(View v) {
                setQueueSelecting(false);
            }
        }));
        queueSelCount = text("已选 0 首", 13, cText);
        queueSelHead.addView(queueSelCount, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        queueSelAll = button("全选", false, new View.OnClickListener() {
            public void onClick(View v) {
                toggleSelectAll(queueAdapter, queueSelAll);
            }
        });
        queueSelHead.addView(queueSelAll);
        page.addView(queueSelHead);

        TextView tips = text("点歌曲立即播放；长按进入多选，可以批量下载、加入歌单、移除。", 12, cDim);
        queueTipsRow = tips;
        LinearLayout.LayoutParams tipsP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        tipsP.topMargin = dp(8);
        tips.setLayoutParams(tipsP);
        page.addView(tips);

        // 多选时，提示行换成三个批量操作
        queueSelActions = row();
        queueSelActions.setVisibility(View.GONE);
        LinearLayout.LayoutParams queueSelP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        queueSelP.topMargin = dp(8);
        queueSelActions.setLayoutParams(queueSelP);
        queueSelActions.addView(growButton("下载", true, new View.OnClickListener() {
            public void onClick(View v) {
                askDownloadScope(queueAdapter.pickedSongs());
            }
        }));
        queueSelActions.addView(growButton("加入歌单", false, new View.OnClickListener() {
            public void onClick(View v) {
                addSelectionToPlaylist(queueAdapter.pickedSongs());
            }
        }));
        queueSelActions.addView(growButton("移除", false, new View.OnClickListener() {
            public void onClick(View v) {
                removeSelectedFromQueue(queueAdapter.pickedSongs());
            }
        }));
        page.addView(queueSelActions);

        queueEmpty = text("播放队列是空的。到「音乐库」点歌名就会开始播放并加入队列。", 13, cDim);
        queueEmpty.setGravity(Gravity.CENTER);
        queueEmpty.setPadding(dp(20), dp(40), dp(20), dp(40));
        page.addView(queueEmpty, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));

        queueList = new ListView(this);
        queueList.setDivider(new android.graphics.drawable.ColorDrawable(cDivider));
        queueList.setDividerHeight(1);
        queueList.setBackgroundColor(cSurface);
        queueList.setSelector(new android.graphics.drawable.ColorDrawable(cAlt));
        LinearLayout.LayoutParams listP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f);
        listP.topMargin = dp(10);
        queueList.setLayoutParams(listP);
        queueList.setOnItemClickListener(new AdapterView.OnItemClickListener() {
            public void onItemClick(AdapterView<?> parent, View view, int position, long id) {
                if (queueAdapter.isSelecting()) {
                    queueAdapter.toggle(songAt(Store.queue, position));
                    updateSelectionBars();
                    return;
                }
                PlayerService service = PlayerService.instance;
                if (service != null) service.playAt(position);
            }
        });
        queueList.setOnItemLongClickListener(new AdapterView.OnItemLongClickListener() {
            public boolean onItemLongClick(AdapterView<?> parent, View view, int position, long id) {
                if (queueAdapter.isSelecting()) {
                    showQueueMenu(position);
                    return true;
                }
                setQueueSelecting(true);
                queueAdapter.toggle(songAt(Store.queue, position));
                updateSelectionBars();
                return true;
            }
        });
        queueAdapter = new SongAdapter(this, true);
        queueList.setAdapter(queueAdapter);
        page.addView(queueList);
        return page;
    }


    // ---------------------------------------------------------------- 设置页

    protected View buildSettingsPage() {
        ScrollView scroll = new ScrollView(this);
        LinearLayout box = column();
        box.setPadding(dp(12), dp(12), dp(12), dp(20));
        scroll.addView(box, new android.widget.FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        // 云盘
        LinearLayout cloud = card();
        cloud.addView(cardTitle("云盘连接"));
        cloud.addView(hint("支持清华云盘等 Seafile 站点。填「分享链接」或「资料库 API 令牌」，两个都填时优先用令牌；"
                + "令牌还能删除云端文件。什么都不填只填令牌也可以。"));
        linkInput = input("分享链接，例如 https://cloud.tsinghua.edu.cn/d/xxxx/", Prefs.link(this));
        cloud.addView(linkInput);
        tokenInput = input("资料库 API 令牌（40 位十六进制）", Prefs.token(this));
        cloud.addView(tokenInput);
        LinearLayout cloudButtons = row();
        cloudButtons.setPadding(0, dp(4), 0, 0);
        cloudButtons.addView(button("粘贴", false, new View.OnClickListener() {
            public void onClick(View v) {
                pasteFromClipboard();
            }
        }));
        cloudButtons.addView(button("测试连接", false, new View.OnClickListener() {
            public void onClick(View v) {
                testConnection();
            }
        }));
        cloudButtons.addView(button("保存并刷新", true, new View.OnClickListener() {
            public void onClick(View v) {
                saveEndpoint();
            }
        }));
        cloud.addView(cloudButtons);
        connInfo = text("", 12, cDim);
        connInfo.setPadding(0, dp(8), 0, 0);
        cloud.addView(connInfo);
        box.addView(cloud);

        // 播放与缓存
        LinearLayout play = card();
        play.addView(cardTitle("本地占用与缓存"));
        cacheSwitch = new Switch(this);
        cacheSwitch.setText("把听过的歌缓存到本机（可离线播放）");
        cacheSwitch.setTextSize(14);
        cacheSwitch.setTextColor(cText);
        cacheSwitch.setChecked(Store.cacheAll());
        cacheSwitch.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                applyCacheMode(cacheSwitch.isChecked() ? Store.CACHE_ALL : Store.CACHE_WINDOW);
            }
        });
        play.addView(cacheSwitch);
        cacheModeHint = text("", 12, cDim);
        cacheModeHint.setPadding(0, dp(8), 0, 0);
        play.addView(cacheModeHint);
        LinearLayout cacheRow = row();
        LinearLayout.LayoutParams cacheRowP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        cacheRowP.topMargin = dp(8);
        cacheRow.setLayoutParams(cacheRowP);
        cacheInfo = text("", 13, cText);
        cacheRow.addView(cacheInfo, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        cacheRow.addView(button("清除缓存", false, new View.OnClickListener() {
            public void onClick(View v) {
                confirmClearCache();
            }
        }));
        play.addView(cacheRow);
        LinearLayout scanRow = row();
        scanRow.addView(button("重新扫描曲库", false, new View.OnClickListener() {
            public void onClick(View v) {
                startScan(true);
            }
        }));
        play.addView(scanRow);
        box.addView(play);

        // 外观
        LinearLayout appearance = card();
        appearance.addView(cardTitle("外观"));
        appearance.addView(hint("默认跟随系统，也可以手动固定。"));
        LinearLayout themeRow = row();
        String[] themeNames = { "跟随系统", "浅色", "深色" };
        for (int i = 0; i < 3; i++) {
            final int mode = i;
            TextView t = text(themeNames[i], 13, cText);
            t.setGravity(Gravity.CENTER);
            t.setPadding(0, dp(10), 0, dp(10));
            LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(0, dp(40), 1f);
            p.rightMargin = i == 2 ? 0 : dp(8);
            t.setLayoutParams(p);
            t.setOnClickListener(new View.OnClickListener() {
                public void onClick(View v) {
                    applyThemeMode(mode);
                }
            });
            themeButtons[i] = t;
            themeRow.addView(t);
        }
        appearance.addView(themeRow);
        box.addView(appearance);

        // 曲库维护
        LinearLayout library = card();
        library.addView(cardTitle("曲库维护"));
        hiddenInfo = text("", 12, cDim);
        hiddenInfo.setPadding(0, 0, 0, dp(8));
        library.addView(hiddenInfo);
        LinearLayout hiddenRow = row();
        hiddenRow.addView(button("恢复已隐藏的歌曲", false, new View.OnClickListener() {
            public void onClick(View v) {
                Store.setHiddenSet(SettingsScreen.this, new java.util.HashSet<String>());
                refreshSettings();
                startScan(false);
                toast("已恢复全部隐藏歌曲");
            }
        }));
        library.addView(hiddenRow);
        box.addView(library);

        // 关于
        LinearLayout about = card();
        about.addView(cardTitle("关于"));
        TextView version = text("云雀 · Android 版 v" + VERSION, 13, cText);
        version.setPadding(0, 0, 0, dp(6));
        about.addView(version);
        about.addView(hint("Windows 版与 Android 版共用同一个云盘曲库：歌名和歌词都放在云盘上，"
                + "这个 App 只负责取回来播放，换设备不用重新整理。\n"
                + "APK 没有上架应用商店、也没有购买签名证书，安装时系统会提示"
                + "「未知来源 / 未知开发者」，允许安装即可。"));
        TextView repo = text("项目主页：github.com/G336ncx-bjx/skylark-music", 13, cAccent);
        repo.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW,
                            Uri.parse("https://github.com/G336ncx-bjx/skylark-music")));
                } catch (Exception e) {
                    toast("没有可用的浏览器");
                }
            }
        });
        about.addView(repo);

        // 更新也放在「关于」这张卡片里，一起在最下面
        updateStatus = text("当前已是最新（" + VERSION + "）", 13, cText);
        updateStatus.setPadding(0, dp(10), 0, 0);
        about.addView(updateStatus);
        LinearLayout updateRow = row();
        updateRow.addView(button("检查更新", true, new View.OnClickListener() {
            public void onClick(View v) {
                checkUpdate();
            }
        }));
        about.addView(updateRow);
        about.addView(hint("点「检查更新」才会联网（平时不会自己检查）。发现新版本后"
                + "在应用里直接下载安装（更新包放在云盘上专门的 apk 仓库里），"
                + "装完会自动删掉安装包；下载下来的包会先校验版本和签名，不符就直接丢弃。"));
        box.addView(about);

        return scroll;
    }


    protected void uploadFiles(final List<Uri> uris) {
        if (uris == null || uris.isEmpty()) return;
        final String endpoint = Store.endpoint;
        if (endpoint.length() == 0) {
            toast("先在「设置」里连接云盘，才能上传");
            return;
        }
        if (uploading) {
            toast("上一批还在上传，等它传完");
            return;
        }
        uploading = true;
        final String target = uploadTargetDir();
        toast("开始上传 " + uris.size() + " 个文件到"
                + ("/".equals(target) ? "云盘根目录" : "歌单「" + target.substring(1) + "」"));
        new Thread(new Runnable() {
            public void run() {
                int ok = 0, fail = 0;
                String lastError = "";
                boolean dirReady = "/".equals(target);
                final String targetDir = target;
                for (int i = 0; i < uris.size(); i++) {
                    final int index = i + 1;
                    final String name = displayName(uris.get(i));
                    postInfo("正在上传 " + index + "/" + uris.size() + "：" + name);
                    File temp = null;
                    try {
                        if (!dirReady) {
                            Cloud.ensureDir(endpoint, targetDir, getCacheDir());
                            dirReady = true;
                        }
                        temp = copyToUploadDir(uris.get(i));
                        final long total = temp.length();
                        Cloud.upload(endpoint, temp, targetDir, new Util.Progress() {
                            private long lastPercent = -5;

                            public void onProgress(long done, long totalBytes) {
                                long size = totalBytes > 0 ? totalBytes : total;
                                int percent = size > 0 ? (int) (done * 100 / size) : 0;
                                if (percent >= lastPercent + 5) {
                                    lastPercent = percent;
                                    postInfo("正在上传 " + index + "/" + uris.size() + "：" + name
                                            + " " + percent + "%");
                                }
                            }
                        });
                        ok++;
                    } catch (Exception e) {
                        fail++;
                        lastError = Util.shorten(e.getMessage());
                    } finally {
                        if (temp != null) temp.delete();
                    }
                }
                uploading = false;
                final int success = ok, failed = fail;
                final String error = lastError;
                ui.post(new Runnable() {
                    public void run() {
                        Store.status = "上传完成：成功 " + success + " 个"
                                + (failed > 0 ? "，失败 " + failed + " 个（" + error + "）" : "");
                        toast(Store.status);
                        startScan(true);
                    }
                });
            }
        }).start();
    }


    // ---------------------------------------------------------------- 底部播放条

    protected View buildPlayerBar() {
        LinearLayout wrapper = column();
        View divider = new View(this);
        divider.setBackgroundColor(cDivider);
        wrapper.addView(divider, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 1));

        LinearLayout bar = column();
        bar.setBackgroundColor(cSurface);
        bar.setPadding(dp(14), dp(10), dp(14), dp(12));
        wrapper.addView(bar);

        LinearLayout titleRow = row();
        LinearLayout names = column();
        nowTitle = text("还没有播放歌曲", 15, cText);
        nowTitle.setSingleLine(true);
        nowTitle.setEllipsize(android.text.TextUtils.TruncateAt.END);
        nowArtist = text("到「音乐库」点歌名就会开始播放", 12, cDim);
        nowArtist.setSingleLine(true);
        nowArtist.setEllipsize(android.text.TextUtils.TruncateAt.END);
        names.addView(nowTitle);
        names.addView(nowArtist);
        titleRow.addView(names, new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        modeText = text(Store.modeName(1), 12, cAccent);
        modeText.setPadding(dp(10), dp(6), dp(6), dp(6));
        modeText.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                PlayerService service = PlayerService.instance;
                if (service == null) return;
                int next = (Store.mode + 1) % 4;
                service.setMode(next);
                modeText.setText(Store.modeName(next));
                toast("播放模式：" + Store.modeName(next));
            }
        });
        titleRow.addView(modeText);
        bar.addView(titleRow);

        LinearLayout progressRow = row();
        LinearLayout.LayoutParams progressP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        progressP.topMargin = dp(6);
        progressRow.setLayoutParams(progressP);
        timeNow = text("0:00", 11, cDim);
        timeNow.setGravity(Gravity.CENTER);
        progressRow.addView(timeNow, new LinearLayout.LayoutParams(dp(40),
                ViewGroup.LayoutParams.WRAP_CONTENT));
        seek = new SeekBar(this);
        seek.setMax(1000);
        seek.setProgress(0);
        seek.setProgressDrawable(progressDrawable());
        seek.setThumb(thumbDrawable());
        seek.setSplitTrack(false);
        seek.setMinimumHeight(dp(34));
        LinearLayout.LayoutParams seekP = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        seekP.leftMargin = dp(6);
        seekP.rightMargin = dp(6);
        seek.setLayoutParams(seekP);
        // 左右留出圆点的位置，否则滑到两端会被切掉
        seek.setPadding(dp(9), dp(12), dp(9), dp(12));
        seek.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            public void onProgressChanged(SeekBar bar, int value, boolean fromUser) {
                if (!fromUser) return;
                double total = currentDuration();
                timeNow.setText(Util.formatTime(total * value / 1000.0));
            }

            public void onStartTrackingTouch(SeekBar bar) {
                dragging = true;
            }

            public void onStopTrackingTouch(SeekBar bar) {
                dragging = false;
                PlayerService service = PlayerService.instance;
                double total = currentDuration();
                if (service != null && total > 0) service.seekTo(total * bar.getProgress() / 1000.0);
            }
        });
        progressRow.addView(seek);
        timeTotal = text("0:00", 11, cDim);
        timeTotal.setGravity(Gravity.CENTER);
        progressRow.addView(timeTotal, new LinearLayout.LayoutParams(dp(40),
                ViewGroup.LayoutParams.WRAP_CONTENT));
        bar.addView(progressRow);

        LinearLayout controls = row();
        controls.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams controlsP = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        controlsP.topMargin = dp(6);
        controls.setLayoutParams(controlsP);

        ImageView prev = iconButton(R.drawable.ic_prev, 40, false);
        prev.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                PlayerService service = PlayerService.instance;
                if (service != null) service.prev();
            }
        });
        controls.addView(prev);

        playIcon = iconButton(R.drawable.ic_play, 52, true);
        playIcon.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                PlayerService service = PlayerService.instance;
                if (service == null) {
                    startPlayerService();
                    toast("正在启动播放服务，再点一次");
                    return;
                }
                service.toggle();
                ui.postDelayed(new Runnable() {
                    public void run() {
                        refreshNowPlaying();
                    }
                }, 150);
            }
        });
        controls.addView(playIcon);

        ImageView next = iconButton(R.drawable.ic_next, 40, false);
        next.setOnClickListener(new View.OnClickListener() {
            public void onClick(View v) {
                PlayerService service = PlayerService.instance;
                if (service != null) service.next(true);
            }
        });
        controls.addView(next);
        bar.addView(controls);
        return wrapper;
    }

}
