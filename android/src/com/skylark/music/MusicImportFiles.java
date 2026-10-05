package com.skylark.music;

import java.io.File;
import java.io.IOException;

/** 两份文件全部确认入库后再清理；失败留下文件及上传记录以便重试。 */
public final class MusicImportFiles {
    public interface Destination {
        Long size(String name) throws IOException;
        void upload(File file) throws IOException;
    }
    private static boolean exists(File file, Destination cloud) throws IOException {
        Long size = cloud.size(file.getName());
        if (size == null) return false;
        if (!new File(file + ".uploaded").exists() || size.longValue() != file.length())
            throw new IOException("云盘已有同名文件「" + file.getName() + "」，请换歌单或处理同名文件");
        return true;
    }
    public static void uploadPair(File[] files, Destination cloud) throws IOException {
        for (File file : files) exists(file, cloud);
        for (File file : files) {
            if (exists(file, cloud)) continue;
            cloud.upload(file);
            File marker = new File(file + ".uploaded");
            if (!marker.exists() && !marker.createNewFile()) throw new IOException("无法记录上传状态");
        }
        for (File file : files) {
            Long size = cloud.size(file.getName());
            if (size == null || size.longValue() != file.length()) throw new IOException("云端文件尚未确认完整，请稍后重试");
        }
        for (File file : files) {
            if (!file.delete()) throw new IOException("云端入库成功，本地文件删除失败");
        }
        for (File file : files) {
            File marker = new File(file + ".uploaded");
            if (marker.exists() && !marker.delete()) throw new IOException("云端入库成功，本地上传记录清理失败");
        }
    }
}
