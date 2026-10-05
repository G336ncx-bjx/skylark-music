using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Skylark;

// Credentials arrive on stdin, never in command-line arguments or files.
class PublishAndroidUpdate
{
    static int Main(string[] args)
    {
        try
        {
            string writeToken = Console.ReadLine(), readToken = Console.ReadLine();
            if (!CloudClient.IsApiToken(writeToken) || !CloudClient.IsApiToken(readToken))
                throw new InvalidOperationException("Missing valid update repository credentials");
            CloudRepoInfo writable = CloudClient.GetRepoInfo(writeToken);
            CloudRepoInfo readable = CloudClient.GetRepoInfo(readToken);
            if (string.IsNullOrEmpty(writable.RepoId) || writable.RepoId != readable.RepoId)
                throw new InvalidOperationException("Write credential does not belong to the app's update repository");
            string mode = args[0], apk = Path.GetFullPath(args[1]), version = args[2];
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            string name = "Skylark-android-" + version + ".apk";
            List<CloudEntry> entries = CloudClient.ListAllFiles(readToken, 3);
            Version requested = new Version(version);
            CloudEntry newest = null;
            Version newestVersion = null;
            foreach (CloudEntry entry in entries)
            {
                if (entry.IsDirectory) continue;
                Match match = Regex.Match(entry.Name, @"^Skylark-android-(\d+(?:\.\d+)*)\.apk$", RegexOptions.IgnoreCase);
                Version found;
                if (!match.Success || !Version.TryParse(match.Groups[1].Value, out found)) continue;
                if (found > requested) throw new InvalidOperationException("A newer version is already published; refusing a downgrade");
                if (newestVersion == null || found > newestVersion) { newest = entry; newestVersion = found; }
            }
            if (mode == "inspect")
            {
                if (newest == null) throw new InvalidOperationException("No previous APK available to verify the update signing certificate");
                CloudClient.DownloadTo(readToken, newest.Path, Path.Combine(folder, "previous-update.apk"), null);
                Console.WriteLine("Update repository confirmed; previous package: " + newest.Name);
                return 0;
            }
            if (mode != "publish") throw new InvalidOperationException("Unknown operation");
            string staging = Path.Combine(folder, name);
            File.Copy(apk, staging, true);
            CloudEntry existing = null;
            foreach (CloudEntry entry in entries) if (entry.Name == name) existing = entry;
            if (existing == null)
            {
                CloudClient.UploadNew(writeToken, staging, "/", null);
                Console.WriteLine("Uploaded " + name);
            }
            CloudEntry published = null;
            foreach (CloudEntry entry in CloudClient.ListAllFiles(readToken, 3))
                if (entry.Name == name && entry.Path == "/" + name) published = entry;
            if (published == null || published.Size != new FileInfo(apk).Length)
                throw new IOException("Published package name or size could not be verified");
            string downloaded = Path.Combine(folder, "verified-update.apk");
            CloudClient.DownloadTo(readToken, published.Path, downloaded, null);
            if (Hash(apk) != Hash(downloaded)) throw new IOException("Downloaded update hash differs from the local APK");
            Console.WriteLine("Read-only update source verified: " + name + " (" + published.Size + " bytes)");
            Console.WriteLine("SHA256: " + Hash(downloaded));
            File.Delete(downloaded); File.Delete(staging); File.Delete(Path.Combine(folder, "previous-update.apk"));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }

    private static string Hash(string file)
    {
        using (SHA256 hash = SHA256.Create())
        using (FileStream stream = File.OpenRead(file))
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }
}
