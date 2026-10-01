using System;
using System.IO;
using System.Reflection;

// Runs the updater embedded in the published OLD binary, not today's source.
// No game files or installed tools are touched. GUI/UAC restart is not automated.
static class PublishedUpgradeTests
{
    static object Call(Type type, string method, params object[] args)
    {
        return type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    }
    static int Main(string[] args)
    {
        try
        {
            string old = Path.GetFullPath(args[0]);
            Assembly assembly = Assembly.LoadFile(old);
            Version version = assembly.GetName().Version;
            Type updater = assembly.GetType("Aion2CNTool.Updater", true);
            string root = Path.Combine(Path.GetTempPath(), "Aion2CN-PublishedUpgrade-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string target = Path.Combine(root, "Aion2-Steam-CN.exe");
            File.Copy(old, target);
            string oldHash = (string)Call(updater, "Hash", target);
            Console.WriteLine("OLD " + version + "; isolated test: " + root);
            object release = Call(updater, "Check", new Version(version.Major, version.Minor, version.Build));
            if (release == null) throw new Exception("Old version did not detect a newer release");
            Type metadata = release.GetType();
            string latest = metadata.GetField("Version").GetValue(release).ToString();
            if (latest != args[1]) throw new Exception("Unexpected release " + latest);
            Console.WriteLine("PASS old binary detected " + latest);
            int last = -1;
            Action<int> progress = delegate(int percent) {
                if (percent / 10 != last) { last = percent / 10; Console.WriteLine("DOWNLOAD " + percent + "%"); }
            };
            string plan = (string)Call(updater, "Stage", release, target, new Func<bool>(delegate { return false; }), progress);
            if ((string)Call(updater, "Hash", target) != oldHash) throw new Exception("Download changed old target");
            string candidate = Path.Combine(Path.GetDirectoryName(plan), "Aion2-Steam-CN.exe");
            string backup = Path.Combine(Path.GetDirectoryName(plan), "previous.exe");
            bool restartRequested = false;
            Call(updater, "ReplaceAndLaunch", candidate, target, backup, oldHash, release,
                new Action(delegate { restartRequested = true; }));
            Call(updater, "Verify", target, release);
            if (!restartRequested || (string)Call(updater, "Hash", backup) != oldHash)
                throw new Exception("Restart callback or backup check failed");
            Console.WriteLine("PASS old binary downloaded, verified and replaced with " + latest + "; backup preserved; restart callback invoked.");
            Console.WriteLine("NOT TESTED: actual elevated GUI restart/UAC. No game files changed.");
            return 0;
        }
        catch (Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            Console.Error.WriteLine("FAIL " + ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }
}
