using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using Aion2CNTool;

static class UpdaterTests
{
    static int assertions;
    static void Assert(bool value, string label) { if (!value) throw new Exception(label); assertions++; }
    static void Reject(Action action, string label)
    {
        bool rejected = false;
        try { action(); } catch (Exception) { rejected = true; }
        Assert(rejected, label);
    }
    static string Metadata(string hash, string url, bool draft, string tag)
    {
        return new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
            { "draft", draft }, { "prerelease", false }, { "tag_name", tag },
            { "assets", new[] { new Dictionary<string, object> {
                { "name", "Aion2-Steam-CN-" + tag + ".exe" }, { "size", 100 },
                { "state", "uploaded" }, { "digest", hash }, { "browser_download_url", url }
            } } }
        });
    }
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--apply-update") { Updater.Apply(args[1]); return; }
        string fixture = Path.GetFullPath(args[0]);
        string root = Path.Combine(Path.GetTempPath(), "Aion2CN-UpdaterTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string url = "https://github.com/" + Updater.Repository + "/releases/download/v2.2.0/Aion2-Steam-CN-v2.2.0.exe";
        string valid = Metadata("sha256:" + new string('a', 64), url, false, "v2.2.0");
        Assert(Updater.Parse(valid, new Version("2.1.0")).Version == new Version("2.2.0"), "new release");
        Assert(Updater.Parse(valid, new Version("2.2.0")) == null, "same release");
        Assert(Updater.Parse(valid, new Version("2.3.0")) == null, "no downgrade");
        Reject(delegate { Updater.Parse(Metadata("sha256:" + new string('a', 64), url, true, "v2.2.0"), new Version("2.1.0")); }, "draft rejected");
        Reject(delegate { Updater.Parse(Metadata("invalid", url, false, "v2.2.0"), new Version("2.1.0")); }, "digest required");
        Reject(delegate { Updater.Parse(Metadata("sha256:" + new string('a', 64), "https://example.com/file.exe", false, "v2.2.0"), new Version("2.1.0")); }, "foreign URL rejected");
        Reject(delegate { Updater.Parse(valid.Replace("\"prerelease\":false", "\"prerelease\":true"), new Version("2.1.0")); }, "prerelease rejected");
        Reject(delegate { Updater.Parse(valid.Replace("\"size\":100", "\"size\":999999999"), new Version("2.1.0")); }, "oversize rejected");
        Reject(delegate { Updater.Parse(valid.Replace("uploaded", "new"), new Version("2.1.0")); }, "incomplete upload rejected");
        Reject(delegate { Updater.Parse("{}", new Version("2.1.0")); }, "invalid metadata rejected");

        var release = new UpdateRelease { Version = new Version("2.2.0"), Hash = Updater.Hash(fixture), Size = new FileInfo(fixture).Length };
        string candidate = Path.Combine(root, "new.exe"), target = Path.Combine(root, "工具 & old.exe"), backup = Path.Combine(root, "previous.exe");
        File.Copy(fixture, candidate); File.WriteAllText(target, "original"); string oldHash = Updater.Hash(target);
        Updater.Verify(candidate, release); assertions++;
        release.Version = new Version("2.3.0"); Reject(delegate { Updater.Verify(candidate, release); }, "assembly version checked"); release.Version = new Version("2.2.0");
        File.AppendAllText(candidate, "broken");
        Reject(delegate { Updater.ReplaceVerified(candidate, target, backup, oldHash, release); }, "truncated/corrupt file rejected");
        Assert(Updater.Hash(target) == oldHash, "corruption preserves old tool");
        File.Copy(fixture, candidate, true);
        Reject(delegate { Updater.ReplaceVerified(candidate, target, backup, "incorrect", release); }, "changed target rejected");
        File.WriteAllText(backup, "previous backup");
        Reject(delegate { Updater.ReplaceVerified(candidate, target, backup, oldHash, release); }, "existing backup preserved");
        Assert(File.ReadAllText(backup) == "previous backup", "backup unchanged"); File.Delete(backup);
        Reject(delegate { Updater.ReplaceAndLaunch(candidate, target, backup, oldHash, release, delegate { throw new IOException("simulated restart failure"); }); }, "restart failure handled");
        Assert(Updater.Hash(target) == oldHash, "restart failure rolls back");
        bool launched = false;
        Updater.ReplaceAndLaunch(candidate, target, backup, oldHash, release, delegate { launched = true; });
        Assert(launched && Updater.Hash(target) == release.Hash && Updater.Hash(backup) == oldHash, "successful replacement retains old tool");

        // Exercise the actual helper subprocess, plan parsing, replacement and restart.
        string helperDirectory = Path.Combine(root, ".aion2cn-update-test"); Directory.CreateDirectory(helperDirectory);
        string testRunner = Assembly.GetExecutingAssembly().Location;
        string helperTarget = Path.Combine(root, "工具 update.exe"); File.Copy(testRunner, helperTarget);
        string helper = Path.Combine(helperDirectory, "UpdateHelper.exe"); File.Copy(testRunner, helper);
        string nextFixture = Path.Combine(Path.GetDirectoryName(testRunner), "update-fixture", "Aion2-Steam-CN.exe");
        File.Copy(nextFixture, Path.Combine(helperDirectory, "Aion2-Steam-CN.exe"));
        string helperPlan = Path.Combine(helperDirectory, "update.json");
        File.WriteAllText(helperPlan, new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
            { "target", helperTarget }, { "old_hash", Updater.Hash(helperTarget) }, { "hash", Updater.Hash(nextFixture) },
            { "size", new FileInfo(nextFixture).Length }, { "version", "2.3.0" }, { "pid", Int32.MaxValue }
        }));
        using (var process = Process.Start(new ProcessStartInfo(helper, "--apply-update \"" + helperPlan + "\"") { UseShellExecute = false, CreateNoWindow = true }))
        {
            Assert(process.WaitForExit(10000) && process.ExitCode == 0, "actual helper completed");
        }
        string marker = Path.Combine(root, "restarted.ok");
        for (int i = 0; i < 50 && !File.Exists(marker); i++) Thread.Sleep(100);
        Assert(File.Exists(marker) && Updater.Hash(helperTarget) == Updater.Hash(nextFixture), "actual replacement restarted fixture");
        Assert(Updater.Hash(Path.Combine(helperDirectory, "previous.exe")) == Updater.Hash(testRunner), "actual helper preserved backup");

        if (args.Length > 1 && args[1] == "--network")
        {
            var live = Updater.Check(new Version("0.0.0"));
            Assert(live != null, "GitHub latest release");
            string stageTarget = Path.Combine(root, "network-test.exe"); File.Copy(fixture, stageTarget);
            string plan = Updater.Stage(live, stageTarget, delegate { return false; }, delegate(int percent) { });
            Assert(File.Exists(plan) && Updater.Hash(stageTarget) == release.Hash, "real release downloaded without changing target");
            Reject(delegate { Updater.Stage(live, stageTarget, delegate { return true; }, delegate(int percent) { }); }, "cancellation");
            Assert(Updater.Hash(stageTarget) == release.Hash, "cancellation preserves target");
        }
        Console.WriteLine("PASS: " + assertions + " assertions. Test artifacts: " + root);
    }
}
