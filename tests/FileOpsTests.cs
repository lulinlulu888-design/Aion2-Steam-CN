using System;
using System.IO;
using System.Reflection;

internal static class FileOpsTests
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("debug assembly and isolated fixture root required");
            string root = Path.GetFullPath(args[1]);
            if (!File.Exists(Path.Combine(root, "isolated-test.marker")))
                throw new InvalidOperationException("Refusing unmarked game directory");
            Type formType = Assembly.LoadFile(Path.GetFullPath(args[0])).GetType("Aion2CNTool.MainForm", true);
            using (IDisposable form = (IDisposable)Activator.CreateInstance(formType, true))
                formType.GetMethod("RunFileOpsTest").Invoke(form, new object[] { root });
            Console.WriteLine("PASS: isolated install, interrupted-update rollback, update, restore, reinstall, restore");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
