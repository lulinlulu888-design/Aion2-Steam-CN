using System;
using System.IO;
using System.Reflection;
[assembly: AssemblyVersion("2.3.0.0")]
static class UpdateFixture
{
    static void Main() { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "restarted.ok"), "fixture restarted"); }
}
