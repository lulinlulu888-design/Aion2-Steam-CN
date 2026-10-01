using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Aion2CNTool
{
    sealed class UpdateRelease
    {
        public Version Version;
        public string Url, Hash;
        public long Size;
    }

    // Release metadata is fetched over HTTPS from this repository only.
    // GitHub's asset digest provides integrity, not an independent publisher signature.
    static class Updater
    {
        internal const string Repository = "lulinlulu888-design/Aion2-Steam-CN";
        internal const string Latest = "https://api.github.com/repos/" + Repository + "/releases/latest";
        const long MaxSize = 256L * 1024 * 1024;

        internal static string Hash(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        static string Text(Dictionary<string, object> value, string key)
        {
            object result;
            if (!value.TryGetValue(key, out result) || !(result is string)) throw new InvalidDataException("更新信息缺少字段：" + key);
            return (string)result;
        }

        internal static UpdateRelease Parse(string json, Version current)
        {
            var root = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("draft") || !root.ContainsKey("prerelease") ||
                !(root["draft"] is bool) || !(root["prerelease"] is bool) || (bool)root["draft"] || (bool)root["prerelease"])
                throw new InvalidDataException("只接受正式发布的更新。");
            string tag = Text(root, "tag_name");
            if (!Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+$")) throw new InvalidDataException("更新版本号格式不受支持。");
            var version = new Version(tag.Substring(1));
            if (version <= current) return null;
            object assets;
            if (!root.TryGetValue("assets", out assets) || !(assets is System.Collections.IEnumerable)) throw new InvalidDataException("未找到更新附件。");
            string expectedName = "Aion2-Steam-CN-" + tag + ".exe";
            UpdateRelease found = null;
            foreach (object item in (System.Collections.IEnumerable)assets)
            {
                var asset = item as Dictionary<string, object>;
                if (asset == null || Text(asset, "name") != expectedName) continue;
                if (found != null) throw new InvalidDataException("更新附件重复。");
                string url = Text(asset, "browser_download_url");
                string expectedUrl = "https://github.com/" + Repository + "/releases/download/" + tag + "/" + expectedName;
                string digest = Text(asset, "digest");
                if (url != expectedUrl || Text(asset, "state") != "uploaded" || !Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$"))
                    throw new InvalidDataException("更新附件地址、上传状态或 SHA-256 无效。");
                object sizeValue;
                if (!asset.TryGetValue("size", out sizeValue)) throw new InvalidDataException("更新附件缺少大小。");
                long size = Convert.ToInt64(sizeValue);
                if (size < 1 || size > MaxSize) throw new InvalidDataException("更新文件大小超出限制。");
                found = new UpdateRelease { Version = version, Url = url, Hash = digest.Substring(7).ToLowerInvariant(), Size = size };
            }
            if (found == null) throw new InvalidDataException("新版本尚未上传完整安装包，请稍后重试。");
            return found;
        }

        static bool Allowed(Uri uri)
        {
            return uri.Scheme == "https" && uri.IsDefaultPort && String.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Host == "api.github.com" || uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");
        }

        static HttpWebResponse Open(string url)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
            Uri uri = new Uri(url);
            for (int redirect = 0; redirect < 6; redirect++)
            {
                if (!Allowed(uri)) throw new InvalidDataException("更新请求被重定向到不受信任的地址。");
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.UserAgent = "Aion2-Steam-CN-Updater";
                request.Accept = "application/vnd.github+json";
                request.AllowAutoRedirect = false;
                request.Timeout = 15000; request.ReadWriteTimeout = 15000;
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                var response = (HttpWebResponse)request.GetResponse();
                int status = (int)response.StatusCode;
                if (status >= 300 && status < 400)
                {
                    string location = response.Headers["Location"]; response.Close();
                    if (String.IsNullOrEmpty(location)) throw new InvalidDataException("更新地址重定向无效。");
                    uri = new Uri(uri, location); continue;
                }
                if (status != 200) { response.Close(); throw new IOException("更新服务器响应异常：" + status); }
                return response;
            }
            throw new IOException("更新地址重定向次数过多。");
        }

        static void Transfer(string url, Stream output, long limit, Func<bool> cancelled, Action<long> progress)
        {
            var timer = Stopwatch.StartNew();
            using (var response = Open(url))
            using (var input = response.GetResponseStream())
            {
                if (response.ContentLength > limit) throw new InvalidDataException("下载大小超出限制。");
                byte[] buffer = new byte[65536]; long total = 0;
                while (true)
                {
                    if (cancelled()) throw new OperationCanceledException();
                    if (timer.Elapsed.TotalMinutes > 5) throw new IOException("下载超时，请稍后重试。");
                    int count = input.Read(buffer, 0, buffer.Length);
                    if (count == 0) break;
                    total += count;
                    if (total > limit) throw new InvalidDataException("下载大小超出限制。");
                    output.Write(buffer, 0, count); progress(total);
                }
            }
        }

        internal static UpdateRelease Check(Version current)
        {
            using (var memory = new MemoryStream())
            {
                Transfer(Latest, memory, 1024 * 1024, delegate { return false; }, delegate(long count) { });
                return Parse(Encoding.UTF8.GetString(memory.ToArray()), current);
            }
        }

        internal static void Verify(string path, UpdateRelease release)
        {
            if (new FileInfo(path).Length != release.Size || Hash(path) != release.Hash)
                throw new InvalidDataException("下载文件的大小或 SHA-256 不匹配，已拒绝更新。");
            var identity = AssemblyName.GetAssemblyName(path);
            if ((identity.Name != "Aion2-Steam-CN" && identity.Name != "Aion2-Steam-CN-v" + release.Version) || identity.Version != new Version(release.Version.Major, release.Version.Minor, release.Version.Build, 0))
                throw new InvalidDataException("下载文件的程序标识或版本与发布信息不一致。");
        }

        internal static string Stage(UpdateRelease release, string target, Func<bool> cancelled, Action<int> progress)
        {
            target = Path.GetFullPath(target);
            // Same volume as target: File.Replace must remain atomic.
            string directory = Path.Combine(Path.GetDirectoryName(target), ".aion2cn-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string candidate = Path.Combine(directory, "Aion2-Steam-CN.exe");
            try
            {
                int lastPercent = -1;
                using (var output = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Transfer(release.Url, output, release.Size, cancelled, delegate(long count) {
                        int percent = (int)(count * 100 / release.Size);
                        if (percent != lastPercent) { lastPercent = percent; progress(percent); }
                    });
                    output.Flush(true);
                }
                if (cancelled()) throw new OperationCanceledException();
                Verify(candidate, release);
                File.Copy(target, Path.Combine(directory, "UpdateHelper.exe"));
                var plan = new Dictionary<string, object> {
                    { "target", target }, { "old_hash", Hash(target) }, { "hash", release.Hash },
                    { "size", release.Size }, { "version", release.Version.ToString() },
                    { "pid", Process.GetCurrentProcess().Id }
                };
                string planPath = Path.Combine(directory, "update.json");
                File.WriteAllText(planPath, new JavaScriptSerializer().Serialize(plan), new UTF8Encoding(false));
                return planPath;
            }
            catch
            {
                foreach (string name in new[] { "Aion2-Steam-CN.exe", "UpdateHelper.exe", "update.json" })
                    try { File.Delete(Path.Combine(directory, name)); } catch { }
                try { Directory.Delete(directory); } catch { }
                throw;
            }
        }

        internal static void StartHelper(string plan)
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(plan), "UpdateHelper.exe"), "--apply-update \"" + plan + "\"") { UseShellExecute = true });
        }

        internal static void ReplaceVerified(string candidate, string target, string backup, string oldHash, UpdateRelease release)
        {
            Verify(candidate, release);
            if (Hash(target) != oldHash) throw new IOException("当前工具文件已经变化，取消更新以避免覆盖。");
            if (File.Exists(backup)) throw new IOException("更新备份已存在，已拒绝覆盖。");
            File.Replace(candidate, target, backup, true);
            try { Verify(target, release); }
            catch { File.Replace(backup, target, candidate, true); throw; }
        }

        internal static void Apply(string planPath)
        {
            planPath = Path.GetFullPath(planPath);
            string directory = Path.GetDirectoryName(planPath);
            if (!String.Equals(directory, AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新计划路径无效。");
            var plan = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(planPath));
            string target = Path.GetFullPath(Text(plan, "target"));
            if (!String.Equals(Path.GetDirectoryName(target), Path.GetDirectoryName(directory), StringComparison.OrdinalIgnoreCase) || Path.GetExtension(target) != ".exe")
                throw new InvalidDataException("更新目标路径无效。");
            string oldHash = Text(plan, "old_hash");
            if (Hash(Assembly.GetExecutingAssembly().Location) != oldHash) throw new InvalidDataException("更新助手校验失败。");
            var release = new UpdateRelease { Version = new Version(Text(plan, "version")), Hash = Text(plan, "hash"), Size = Convert.ToInt64(plan["size"]) };
            Process parent = null;
            try { parent = Process.GetProcessById(Convert.ToInt32(plan["pid"])); } catch (ArgumentException) { }
            if (parent != null) using (parent) { if (!parent.WaitForExit(60000)) throw new IOException("原工具未退出，取消更新。"); }
            string candidate = Path.Combine(directory, "Aion2-Steam-CN.exe");
            string backup = Path.Combine(directory, "previous.exe");
            ReplaceAndLaunch(candidate, target, backup, oldHash, release, delegate { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); });
            // Retain previous.exe for manual rollback. Never delete arbitrary old directories.
        }

        internal static void ReplaceAndLaunch(string candidate, string target, string backup, string oldHash, UpdateRelease release, Action launch)
        {
            ReplaceVerified(candidate, target, backup, oldHash, release);
            try { launch(); }
            catch { File.Replace(backup, target, candidate, true); throw; }
        }
    }
}
