package selftest;

import com.skylark.music.Lrc;
import com.skylark.music.Util;
import com.skylark.music.MusicImportFiles;

import java.io.FileDescriptor;
import java.io.FileOutputStream;
import java.io.PrintStream;
import java.nio.charset.Charset;

/**
 * 纯 Java 逻辑自检：不需要手机、不需要网络。
 * 覆盖文件名解析、编码识别、时间格式、MP3 时长解析与 LRC 解析。
 */
public class SelfTest {

    private static int passed = 0;
    private static int failed = 0;
    private static PrintStream out = System.out;

    public static void main(String[] args) {
        try {
            out = new PrintStream(new FileOutputStream(FileDescriptor.out), true, "UTF-8");
        } catch (Exception e) {
            out = System.out;
        }
        testSongName();
        testMusicImport();
        testDecode();
        testTime();
        testMp3Duration();
        testLrc();
        out.println("通过 " + passed + " 项，失败 " + failed + " 项");
        if (failed > 0) System.exit(1);
    }

    // ---------- 用例 ----------

    private static void testMusicImport() {
        java.io.File dir = null;
        try {
            String[][] titles = new String[][] {
                { "樱花草-《米可，GO！》电视剧主题曲_《星苹果乐园》电视剧插曲", "樱花草" },
                { "如愿（电影《我和我的父辈》主题推广曲）", "如愿" },
                { "孤勇者 - 动画《英雄联盟：双城之战》主题曲", "孤勇者" },
                { "光的方向《长歌行》电视剧片头曲", "光的方向" },
                { "歌曲 (Live) - 《剧名》片尾曲", "歌曲 (Live)" },
                { "A-B - 《剧名》插曲", "A-B" },
                { "My Song - Some Show 主题曲", "My Song" },
                { "世界-某某剧主题曲", "世界" },
                { "歌曲（伴奏）", "歌曲（伴奏）" },
                { "歌曲 (Remix)", "歌曲 (Remix)" },
                { "我的主题曲", "我的主题曲" },
                { "主题曲", "主题曲" }
            };
            for (String[] pair : titles) check("入库歌名清理：" + pair[0], pair[1].equals(Util.cleanMusicTitle(pair[0])));
            check("在线入库：安全文件名", "歌_曲".equals(Util.safeMusicName("歌/曲")));
            boolean rejected = false;
            try { Util.validateMusicLyric("<html>验证页面</html>"); } catch (java.io.IOException e) { rejected = true; }
            check("在线入库：拒绝无效歌词", rejected);
            Util.validateMusicLyric("[00:01.00]歌词");
            byte[] frames = new byte[3000]; frames[0] = frames[1044] = (byte)255;
            frames[1] = frames[1045] = (byte)251; frames[2] = frames[1046] = (byte)224;
            Util.validateMusicHead(frames, frames.length);
            rejected = false;
            try { Util.validateMusicHead("<html>验证页面</html>".getBytes("UTF-8"), 20); } catch (java.io.IOException e) { rejected = true; }
            check("在线入库：拒绝伪音频", rejected);
            dir = java.nio.file.Files.createTempDirectory("skylark-import-test").toFile();
            final java.io.File audio = new java.io.File(dir, "歌曲 - 歌手.mp3"), lyric = new java.io.File(dir, "歌曲 - 歌手.lrc");
            java.nio.file.Files.write(audio.toPath(), frames); java.nio.file.Files.write(lyric.toPath(), "[00:01]歌词".getBytes("UTF-8"));
            final java.util.Map<String, Long> cloud = new java.util.HashMap<String, Long>();
            final int[] uploads = new int[1]; final boolean[] fail = new boolean[1]; final boolean[] mismatch = new boolean[1];
            MusicImportFiles.Destination destination = new MusicImportFiles.Destination() {
                public Long size(String name) { return cloud.get(name); }
                public void upload(java.io.File file) throws java.io.IOException {
                    if (fail[0] && file.equals(lyric)) throw new java.io.IOException("模拟中断");
                    uploads[0]++; cloud.put(file.getName(), Long.valueOf(file.length() - (mismatch[0] ? 1 : 0)));
                }
            };
            java.io.File[] files = new java.io.File[] { audio, lyric };
            cloud.put(lyric.getName(), Long.valueOf(5)); rejected = false;
            try { MusicImportFiles.uploadPair(files, destination); } catch (java.io.IOException e) { rejected = true; }
            check("在线入库：歌词撞名时不上传任何文件", rejected && uploads[0] == 0 && audio.exists() && lyric.exists());
            cloud.clear(); fail[0] = true; rejected = false;
            try { MusicImportFiles.uploadPair(files, destination); } catch (java.io.IOException e) { rejected = true; }
            check("在线入库：上传失败保留文件和记录", rejected && audio.exists() && lyric.exists() && new java.io.File(audio + ".uploaded").exists());
            fail[0] = false; MusicImportFiles.uploadPair(files, destination);
            check("在线入库：重试跳过已上传音频", uploads[0] == 2 && cloud.size() == 2);
            check("在线入库：核对两份云端文件后删除本地", !audio.exists() && !lyric.exists() && dir.list().length == 0);
            cloud.clear(); mismatch[0] = true;
            java.nio.file.Files.write(audio.toPath(), frames); java.nio.file.Files.write(lyric.toPath(), "[00:01]歌词".getBytes("UTF-8"));
            rejected = false;
            try { MusicImportFiles.uploadPair(files, destination); } catch (java.io.IOException e) { rejected = true; }
            check("在线入库：云端大小不一致时保留本地", rejected && audio.exists() && lyric.exists());
        } catch (Exception e) { check("在线入库自检异常：" + e, false); }
        finally { if (dir != null) { for (java.io.File file : dir.listFiles()) file.delete(); dir.delete(); } }
    }

    private static void testSongName() {
        check("歌名解析：标准格式", equal(Util.parseSongName("稻香 - 周杰伦"), "稻香", "周杰伦"));
        check("歌名解析：多歌手", equal(Util.parseSongName("City of Stars - Ryan Gosling、Emma Stone"),
                "City of Stars", "Ryan Gosling、Emma Stone"));
        check("歌名解析：歌名带间隔号", equal(Util.parseSongName("铁道唱歌·京广高铁篇 - 群星"),
                "铁道唱歌·京广高铁篇", "群星"));
        check("歌名解析：没有歌手", equal(Util.parseSongName("未知小样"), "未知小样", ""));
        check("歌名解析：歌手里还有连字符", equal(Util.parseSongName("A - B - C"), "A", "B - C"));
    }

    private static void testDecode() {
        String utf8 = "冬眠 - 司南";
        check("编码识别：UTF-8", utf8.equals(Util.decodeText(utf8.getBytes(Charset.forName("UTF-8")))));
        String gbk = "稻香";
        check("编码识别：GBK", gbk.equals(Util.decodeText(gbk.getBytes(Charset.forName("GBK")))));
        byte[] bom = new byte[] { (byte) 0xEF, (byte) 0xBB, (byte) 0xBF, 'h', 'i' };
        check("编码识别：UTF-8 BOM", "hi".equals(Util.decodeText(bom)));
        check("编码识别：空数据", "".equals(Util.decodeText(new byte[0])));
    }

    private static void testTime() {
        check("时间格式：0:00", "0:00".equals(Util.formatTime(0)));
        check("时间格式：1:05", "1:05".equals(Util.formatTime(65)));
        check("时间格式：10:00", "10:00".equals(Util.formatTime(600)));
    }

    private static void testMp3Duration() {
        // 100 字节填充 + 一个合法的 MPEG1 Layer3 128kbps / 44.1kHz 帧头
        byte[] head = new byte[500];
        head[100] = (byte) 0xFF;
        head[101] = (byte) 0xFB;
        head[102] = (byte) 0x90;
        head[103] = (byte) 0x00;
        double duration = Util.mp3Duration(head, 500);
        // 计算式：(500 - 100) * 8 / 128000 = 0.025 秒
        check("MP3 时长：按码率估算", duration > 0.02 && duration < 0.03,
                "得到 " + duration);
        check("MP3 时长：数据不足时返回 0", Util.mp3Duration(new byte[8], 8) == 0);
    }

    private static void testLrc() {
        String text = "[ti:测试]\n"
                + "[offset:-500]\n"
                + "[00:01.00]第一句\n"
                + "[00:03.50]第二句\n"
                + "[00:03.50]Second line\n"
                + "[00:05.00][00:07.00]重复句\n"
                + "[00:20.000]最后一句\n";
        Lrc lrc = Lrc.parse(text);
        check("歌词：识别为同步歌词", lrc.synced);
        check("歌词：行数（多时间戳展开 + 翻译合并）", lrc.lines.size() == 5,
                "得到 " + lrc.lines.size() + " 行");
        if (lrc.lines.size() >= 5) {
            check("歌词：offset 生效（1.0 - 0.5 = 0.5）", near(lrc.lines.get(0).time, 0.5));
            check("歌词：同时间戳第二行当翻译", lrc.lines.get(1).hasTranslation()
                    && "Second line".equals(lrc.lines.get(1).translation));
            check("歌词：多时间戳展开", near(lrc.lines.get(2).time, 4.5) && near(lrc.lines.get(3).time, 6.5));
            check("歌词：毫秒三位数解析", near(lrc.lines.get(4).time, 19.5));
            check("歌词：定位第 1 句", lrc.indexAt(0.6) == 0);
            check("歌词：定位第 2 句", lrc.indexAt(3.2) == 1);
            check("歌词：定位第 3 句", lrc.indexAt(4.9) == 2);
            check("歌词：开头之前返回 -1", lrc.indexAt(0.1) == -1);
            check("歌词：结尾之后停在最后一句", lrc.indexAt(999) == 4);
        }
        Lrc plain = Lrc.parse("这是一行纯文本歌词\n第二行");
        check("歌词：纯文本不算同步", !plain.synced && plain.lines.size() == 2);
        check("歌词：空文本不报错", Lrc.parse("").lines.isEmpty());

        // 双语歌词的另一种写法：译文紧跟原文，但时间戳被标成下一句的时间
        // （云盘上《Take Me Hand》《願い～あの頃のキミへ～》就是这种）
        String shifted = "[00:59.99]In my dreams\n"
                + "[01:01.66]我的梦里\n"
                + "[01:01.66]I feel your light\n"
                + "[01:03.60]有你的光芒\n"
                + "[01:03.60]I feel love is born again\n"
                + "[01:07.32]爱再次绽放\n"
                + "[01:07.32]Fireflies\n";
        Lrc bilingual = Lrc.parse(shifted);
        check("歌词：译文错位的时间戳也能配对", bilingual.lines.size() == 4,
                "得到 " + bilingual.lines.size() + " 行");
        if (bilingual.lines.size() >= 4) {
            check("歌词：原文是外语、译文是中文",
                    "In my dreams".equals(bilingual.lines.get(0).text)
                            && "我的梦里".equals(bilingual.lines.get(0).translation));
            check("歌词：第二句配对不错位",
                    "I feel your light".equals(bilingual.lines.get(1).text)
                            && "有你的光芒".equals(bilingual.lines.get(1).translation));
            check("歌词：时间用原文的时间戳", near(bilingual.lines.get(1).time, 61.66));
            check("歌词：定位到第二句", bilingual.indexAt(62.0) == 1);
        }

        // 真实场景：日文歌 + 中文译文，而且中文那侧多出「词：/曲：」信息行，
        // 按「谁多谁是原文」会判反，必须用 [ti:标题] 的语言来定
        String jp = "[ti:願い～あの頃のキミへ～ (祈愿~致那个时候的你～)]\n"
                + "[00:00.24]願い～あの頃のキミへ～ - 當山みれい\n"
                + "[00:06.15]词：Dohzi-T\n"
                + "[00:14.94]二人の思い出 かき集めたなら\n"
                + "[00:20.63]回想起和你之间的回忆\n"
                + "[00:20.63]また泣けてきちゃう 寂しさ溢れて\n"
                + "[00:26.34]又会令我落泪 令我感到寂寞\n"
                + "[00:26.34]最後の恋だと 信じて願った\n";
        Lrc jpLrc = Lrc.parse(jp);
        boolean jpOk = false;
        for (int i = 0; i < jpLrc.lines.size(); i++) {
            Lrc.Line line = jpLrc.lines.get(i);
            if ("二人の思い出 かき集めたなら".equals(line.text)
                    && "回想起和你之间的回忆".equals(line.translation)) {
                jpOk = true;
                break;
            }
        }
        check("歌词：日文歌不会把中文译文当原文", jpOk);
    }

    // ---------- 工具 ----------

    private static boolean equal(String[] parts, String title, String artist) {
        return parts.length == 2 && title.equals(parts[0]) && artist.equals(parts[1]);
    }

    private static boolean near(double value, double expect) {
        return Math.abs(value - expect) < 0.001;
    }

    private static void check(String name, boolean ok) {
        check(name, ok, "");
    }

    private static void check(String name, boolean ok, String detail) {
        if (ok) {
            passed++;
            out.println("  [ok]   " + name);
        } else {
            failed++;
            out.println("  [FAIL] " + name + (detail.length() > 0 ? "  " + detail : ""));
        }
    }
}
