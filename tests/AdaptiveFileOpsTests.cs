using System;
using System.IO;
using System.Reflection;

static class AdaptiveFileOpsTests
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args[1]);
            if (!File.Exists(Path.Combine(root, "isolated-test.marker"))) throw new Exception("Unmarked test directory");
            var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type type = assembly.GetType("Aion2CNTool.MainForm", true);
            using (var form = (IDisposable)Activator.CreateInstance(type, true))
                type.GetMethod("RunAdaptiveFileOpsTest").Invoke(form, new object[] { root });
            Console.WriteLine("PASS old-over-new restore rejected; interrupted migration restored state/backups/game bytes; adaptive install, backup migration, interruption rollback, update, restore to NEW PAK; old loose DAT removed; previous generation retained.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
