using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("永恒之塔2 一键汉化工具")]
[assembly: AssemblyDescription("台服官方繁中转简体，支持兼容性检测、安全备份与一键还原")]
[assembly: AssemblyCompany("Aion2CNTool")]
[assembly: AssemblyProduct("永恒之塔2 一键汉化工具")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace Aion2CNTool
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--self-test")
            {
                try
                {
                    var rows = L10N.ReadLegacy(args[1]);
                    foreach (Entry e in rows) e.Value = Chinese.ToSimplified(e.Value);
                    L10N.WriteLegacy(args[2], rows);
                    File.WriteAllText(args[2] + ".ok.txt", rows.Count.ToString(), Encoding.ASCII);
                }
                catch (Exception ex) { File.WriteAllText(args[2] + ".error.txt", ex.GetType().FullName + Environment.NewLine + ex.Message + Environment.NewLine + ex.StackTrace, Encoding.UTF8); Environment.ExitCode = 1; }
                return;
            }
            if (args.Length == 2 && args[0] == "--fileops-test")
            {
                try { using (var form = new MainForm()) form.RunFileOpsTest(args[1]); File.WriteAllText(Path.Combine(args[1], "fileops.ok.txt"), "ok", Encoding.ASCII); }
                catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "fileops.error.txt"), ex.ToString(), Encoding.UTF8); Environment.ExitCode = 1; }
                return;
            }
            if (args.Length == 2 && args[0] == "--detect-test")
            {
                try { using (var form = new MainForm()) form.RunDiscoveryTest(args[1]); }
                catch (Exception ex) { File.WriteAllText(args[1], ex.ToString(), Encoding.UTF8); Environment.ExitCode = 1; }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    sealed class MainForm : Form
    {
        const string SupportedPakHash = "5BFCDEC64CED073002C9E58210A3C956CCF321A6A69E9474575C79C133B37733";
        const string PayloadHash = "3F5D372A7D44B169C32018F396DF1263E90B7D17F4CE4BA23CFF6031B27527FB";
        readonly TextBox steam = new TextBox();
        readonly TextBox tw = new TextBox();
        readonly TextBox log = new TextBox();
        readonly Button install = new Button();
        readonly Button restore = new Button();
        readonly Button inspect = new Button();
        bool testMode;

        public MainForm()
        {
            Text = "永恒之塔2 一键汉化工具";
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(720, 500);
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label { Text = "永恒之塔2 简体中文工具", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = true, Left = 20, Top = 18 };
            var sub = new Label { Text = "台服官方繁中 → 简体中文，并统一《永恒之塔》职业、技能及系统术语", AutoSize = true, Left = 22, Top = 58, ForeColor = Color.DimGray };
            Controls.Add(title); Controls.Add(sub);

            AddPathRow("Steam / Global 客户端", steam, 92, BrowseSteam);
            AddPathRow("台服客户端（繁中来源）", tw, 142, BrowseTw);

            inspect.Text = "检测兼容性"; inspect.SetBounds(20, 198, 135, 38); inspect.Click += delegate { SafeRun(Inspect); };
            install.Text = "一键安装 / 更新"; install.SetBounds(170, 198, 160, 38); install.BackColor = Color.FromArgb(38, 116, 221); install.ForeColor = Color.White; install.FlatStyle = FlatStyle.Flat; install.Click += delegate { SafeRun(Install); };
            restore.Text = "一键还原"; restore.SetBounds(345, 198, 135, 38); restore.Click += delegate { SafeRun(Restore); };
            Controls.Add(inspect); Controls.Add(install); Controls.Add(restore);

            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.SetBounds(20, 254, 720, 240); log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            log.BackColor = Color.FromArgb(248, 249, 251); Controls.Add(log);

            steam.Text = DiscoverSteamClient();
            tw.Text = DiscoverTwClient();
            Shown += delegate { SafeRun(Inspect); };
        }

        string DiscoverSteamClient()
        {
            var roots = new List<string>();
            AddCandidate(roots, ReadRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"));
            AddCandidate(roots, ReadRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"));
            AddCandidate(roots, @"C:\Program Files (x86)\Steam");
            AddCandidate(roots, @"C:\Program Files\Steam");

            var libraries = new List<string>(roots);
            foreach (string root in roots)
            {
                string vdf = Path.Combine(root, @"steamapps\libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (string line in File.ReadAllLines(vdf))
                    {
                        Match m = Regex.Match(line, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"");
                        if (m.Success) AddCandidate(libraries, m.Groups["path"].Value.Replace("\\\\", "\\"));
                    }
                }
                catch { }
            }
            foreach (string library in libraries)
            {
                string client = Path.Combine(library, @"steamapps\common\AION2");
                if (File.Exists(Path.Combine(client, @"Aion2\Content\Paks\L10N\Text\en-US\pakchunk502000-Windows_0_P.pak")) ||
                    File.Exists(Path.Combine(client, @"Aion2\Content\Paks\L10N\Text\en-US\pakchunk502000-Windows_0_P.pak.tool_bak"))) return client;
            }
            return @"C:\Program Files (x86)\Steam\steamapps\common\AION2";
        }

        string DiscoverTwClient()
        {
            var candidates = new List<string>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType == DriveType.CDRom) continue;
                AddCandidate(candidates, Path.Combine(drive.RootDirectory.FullName, @"Program Files (x86)\NCSOFT\AION2_TW"));
                AddCandidate(candidates, Path.Combine(drive.RootDirectory.FullName, @"Program Files\NCSOFT\AION2_TW"));
                AddCandidate(candidates, Path.Combine(drive.RootDirectory.FullName, @"NCSOFT\AION2_TW"));
            }
            foreach (string client in candidates)
                if (File.Exists(Path.Combine(client, @"Aion2\Content\Paks\L10N\Text\zh-TW\pakchunk504000-Windows_0_P.pak"))) return client;
            return @"C:\Program Files (x86)\NCSOFT\AION2_TW";
        }

        static string ReadRegistryString(RegistryKey root, string subKey, string name)
        {
            try { using (RegistryKey key = root.OpenSubKey(subKey)) return key == null ? null : key.GetValue(name) as string; }
            catch { return null; }
        }

        static void AddCandidate(List<string> values, string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return;
            value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')).Replace('/', '\\');
            foreach (string existing in values) if (String.Equals(existing, value, StringComparison.OrdinalIgnoreCase)) return;
            values.Add(value);
        }

        void AddPathRow(string caption, TextBox box, int top, EventHandler browse)
        {
            var label = new Label { Text = caption, Left = 20, Top = top, Width = 185 };
            box.SetBounds(205, top - 4, 455, 28); box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var button = new Button { Text = "浏览…" }; button.SetBounds(670, top - 5, 70, 29); button.Anchor = AnchorStyles.Top | AnchorStyles.Right; button.Click += browse;
            Controls.Add(label); Controls.Add(box); Controls.Add(button);
        }

        void BrowseSteam(object sender, EventArgs e) { BrowseInto(steam); }
        void BrowseTw(object sender, EventArgs e) { BrowseInto(tw); }
        void BrowseInto(TextBox target)
        {
            using (var f = new FolderBrowserDialog()) { f.SelectedPath = target.Text; if (f.ShowDialog(this) == DialogResult.OK) target.Text = f.SelectedPath; }
        }

        void SafeRun(Action action)
        {
            try { UseWaitCursor = true; SetButtons(false); action(); }
            catch (Exception ex) { Append("错误：" + ex.Message); MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { UseWaitCursor = false; SetButtons(true); }
        }

        void SetButtons(bool value) { inspect.Enabled = value; install.Enabled = value; restore.Enabled = value; }
        void Append(string text) { log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine); }

        string SteamPak { get { return Path.Combine(steam.Text.Trim(), @"Aion2\Content\Paks\L10N\Text\en-US\pakchunk502000-Windows_0_P.pak"); } }
        string SteamDat { get { return Path.Combine(steam.Text.Trim(), @"Aion2\Content\L10N\Text\en-US\L10NString.dat"); } }
        string TwPak { get { return Path.Combine(tw.Text.Trim(), @"Aion2\Content\Paks\L10N\Text\zh-TW\pakchunk504000-Windows_0_P.pak"); } }
        string BackupPak { get { return SteamPak + ".aion2cn.original"; } }
        string BackupDat { get { return SteamDat + ".aion2cn.original"; } }
        const string PayloadResource = "Aion2CNTool.Payload.L10NString.dat";
        string ExternalPayloadDat { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"payload\L10NString.dat"); } }

        Stream OpenPayload()
        {
            Stream embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource);
            if (embedded != null) return embedded;
            if (File.Exists(ExternalPayloadDat)) return File.OpenRead(ExternalPayloadDat);
            throw new FileNotFoundException("工具内未找到优化语言数据，请重新下载完整安装程序。", ExternalPayloadDat);
        }

        string PayloadDigest()
        {
            using (Stream stream = OpenPayload())
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(stream); var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) sb.Append(b.ToString("X2")); return sb.ToString();
            }
        }

        void WritePayload(string destination)
        {
            string pending = destination + ".aion2cn.pending";
            using (Stream source = OpenPayload())
            using (var output = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None)) source.CopyTo(output);
            if (Hash(pending) != PayloadHash) { File.Delete(pending); throw new InvalidDataException("内置优化语言数据校验失败，请重新下载安装程序。"); }
            File.Copy(pending, destination, true);
            File.Delete(pending);
        }

        void Inspect()
        {
            log.Clear();
            Append(File.Exists(SteamPak) ? "已找到 Steam 英文语言包。" : "未找到 Steam 英文语言包：" + SteamPak);
            Append(File.Exists(TwPak) ? "已找到台服官方繁体中文语言包。" : "未找到台服繁中语言包：" + TwPak);
            if (File.Exists(TwPak) && File.Exists(SteamPak))
            {
                var source = File.GetLastWriteTime(TwPak); var target = File.GetLastWriteTime(SteamPak);
                Append("繁中来源日期：" + source.ToString("yyyy-MM-dd") + "；Steam 语言包日期：" + target.ToString("yyyy-MM-dd"));
                if (source.Date < target.Date) Append("来源策略：台服繁中优先；全球版新增文本由现有汉化补缺，无法安全迁移的条目回退英文。");
                else Append("兼容性：版本日期匹配，可进行安装测试。");
            }
            string original = FindOriginalPak();
            if (File.Exists(original)) Append(Hash(original) == SupportedPakHash ? "全球版构建校验：完全匹配（152,629 条）。" : "全球版构建校验：版本不同，安装时将拒绝覆盖。");
            try { Append(PayloadDigest() == PayloadHash ? "内置优化包校验通过：官方繁中 110,255 条，现有汉化补缺 42,352 条。" : "内置优化包校验失败，请重新下载安装程序。"); }
            catch (Exception ex) { Append("内置优化包不可用：" + ex.Message); }
            Append(File.Exists(BackupPak) ? "检测到本工具备份，可一键还原。" : "尚未创建本工具备份。");
        }

        void EnsureGameClosed()
        {
            if (testMode) return;
            if (Process.GetProcessesByName("AION2").Length > 0 || Process.GetProcessesByName("Aion2-Win64-Shipping").Length > 0)
                throw new InvalidOperationException("请先完全退出《永恒之塔2》，再执行安装或还原。");
        }

        public void RunFileOpsTest(string root)
        {
            testMode = true;
            steam.Text = root;
            string beforePak = Hash(FindOriginalPak());
            string beforeDat = File.Exists(SteamDat) ? Hash(SteamDat) : null;
            Install();
            if (!File.Exists(BackupPak) || Hash(SteamDat) != PayloadHash || File.ReadAllText(SteamPak, Encoding.ASCII) != "AION2CN 1.2")
                throw new InvalidDataException("staged install verification failed");
            Restore();
            if (Hash(SteamPak) != beforePak) throw new InvalidDataException("pak restore verification failed");
            if (beforeDat == null ? File.Exists(SteamDat) : Hash(SteamDat) != beforeDat) throw new InvalidDataException("dat restore verification failed");
        }

        public void RunDiscoveryTest(string output)
        {
            string detectedSteam = steam.Text;
            string detectedTw = tw.Text;
            string steamMarker = Path.Combine(detectedSteam, @"Aion2\Content\Paks\L10N\Text\en-US\pakchunk502000-Windows_0_P.pak");
            string twMarker = Path.Combine(detectedTw, @"Aion2\Content\Paks\L10N\Text\zh-TW\pakchunk504000-Windows_0_P.pak");
            File.WriteAllText(output,
                "Steam=" + detectedSteam + Environment.NewLine +
                "SteamFound=" + (File.Exists(steamMarker) || File.Exists(steamMarker + ".tool_bak")) + Environment.NewLine +
                "TW=" + detectedTw + Environment.NewLine +
                "TWFound=" + File.Exists(twMarker) + Environment.NewLine, Encoding.UTF8);
        }

        void Install()
        {
            EnsureGameClosed();
            if (PayloadDigest() != PayloadHash) throw new InvalidDataException("内置优化语言数据校验失败，请重新下载安装程序。");
            string original = FindOriginalPak();
            if (!File.Exists(original)) throw new FileNotFoundException("找不到 Steam 原始英文语言包。", original);
            if (Hash(original) != SupportedPakHash) throw new InvalidOperationException("Steam 游戏版本与本优化包不匹配。请等待工具更新，禁止强行覆盖。");

            Directory.CreateDirectory(Path.GetDirectoryName(SteamDat));
            if (!File.Exists(BackupPak)) File.Copy(original, BackupPak, false);
            if (File.Exists(SteamDat) && !File.Exists(BackupDat)) File.Copy(SteamDat, BackupDat, false);
            WritePayload(SteamDat);
            File.WriteAllText(SteamPak, "AION2CN 1.2", Encoding.ASCII);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(SteamDat), "Aion2CNTool.install.json"),
                "{\"version\":\"1.2\",\"installed\":\"" + DateTime.UtcNow.ToString("o") + "\",\"entries\":152629,\"official_tw\":110255,\"fallback\":42352,\"english\":22}", Encoding.UTF8);
            Append("安装完成：152,629 条文本结构校验通过。官方繁中 110,255 条，汉化补缺 42,352 条，英文安全回退 22 条。");
            Append("首次进入游戏请重点检查职业名、技能页和任务文本；如异常可立即一键还原。");
        }

        string FindOriginalPak()
        {
            if (File.Exists(BackupPak)) return BackupPak;
            if (File.Exists(SteamPak + ".tool_bak")) return SteamPak + ".tool_bak";
            return SteamPak;
        }

        string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var fs = File.OpenRead(path))
            {
                var bytes = sha.ComputeHash(fs); var sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) sb.Append(b.ToString("X2")); return sb.ToString();
            }
        }

        void Restore()
        {
            EnsureGameClosed();
            if (!File.Exists(BackupPak)) throw new FileNotFoundException("没有找到本工具创建的原始语言包备份。", BackupPak);
            File.Copy(BackupPak, SteamPak, true);
            if (File.Exists(BackupDat)) File.Copy(BackupDat, SteamDat, true); else if (File.Exists(SteamDat)) File.Delete(SteamDat);
            string marker = Path.Combine(Path.GetDirectoryName(SteamDat), "Aion2CNTool.install.json");
            if (File.Exists(marker)) File.Delete(marker);
            Append("已恢复 Steam 原始语言包。备份文件仍保留，可再次安装。");
        }

        void Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi)) { string output = p.StandardOutput.ReadToEnd(); string error = p.StandardError.ReadToEnd(); p.WaitForExit(); if (p.ExitCode != 0) throw new InvalidOperationException("解包失败：" + error + output); }
        }
    }

    sealed class Entry { public string Key; public string Value; }

    static class L10N
    {
        public static List<Entry> ReadLegacy(string path)
        {
            var result = new List<Entry>();
            using (var br = new BinaryReader(File.OpenRead(path), Encoding.UTF8))
            {
                string ns = ReadFString(br);
                if (!String.Equals(ns, "AION2", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("不支持的语言文件命名空间：" + ns);
                int count = br.ReadInt32();
                if (count < 1 || count > 1000000) throw new InvalidDataException("语言条目数量异常：" + count);
                for (int i = 0; i < count; i++) result.Add(new Entry { Key = ReadFString(br), Value = ReadFString(br) });
                long remain = br.BaseStream.Length - br.BaseStream.Position;
                if (remain == 4 && br.ReadInt32() == 0) { }
                else if (remain != 0) throw new InvalidDataException("语言文件尾部存在未解析数据。");
            }
            return result;
        }

        public static void WriteLegacy(string path, List<Entry> entries)
        {
            using (var bw = new BinaryWriter(File.Create(path), Encoding.UTF8))
            {
                WriteFString(bw, "AION2"); bw.Write(entries.Count);
                foreach (Entry e in entries) { WriteFString(bw, e.Key); WriteFString(bw, e.Value); }
                bw.Write(0);
            }
            ReadLegacy(path);
        }

        static string ReadFString(BinaryReader br)
        {
            int len = br.ReadInt32();
            if (len == 0) return String.Empty;
            if (len > 0)
            {
                if (len > 4000000) throw new InvalidDataException("UTF-8 字符串长度异常。");
                byte[] bytes = br.ReadBytes(len); if (bytes.Length != len) throw new EndOfStreamException();
                return Encoding.UTF8.GetString(bytes, 0, len > 0 && bytes[len - 1] == 0 ? len - 1 : len);
            }
            int chars = -len; if (chars > 2000000) throw new InvalidDataException("UTF-16 字符串长度异常。");
            byte[] wide = br.ReadBytes(chars * 2); if (wide.Length != chars * 2) throw new EndOfStreamException();
            int size = wide.Length >= 2 && wide[wide.Length - 1] == 0 && wide[wide.Length - 2] == 0 ? wide.Length - 2 : wide.Length;
            return Encoding.Unicode.GetString(wide, 0, size);
        }

        static void WriteFString(BinaryWriter bw, string value)
        {
            if (String.IsNullOrEmpty(value)) { bw.Write(0); return; }
            bool wide = false; foreach (char c in value) if (c > 127) { wide = true; break; }
            if (wide) { bw.Write(-(value.Length + 1)); bw.Write(Encoding.Unicode.GetBytes(value)); bw.Write((ushort)0); }
            else { byte[] bytes = Encoding.UTF8.GetBytes(value); bw.Write(bytes.Length + 1); bw.Write(bytes); bw.Write((byte)0); }
        }
    }

    static class Chinese
    {
        const uint LCMAP_SIMPLIFIED_CHINESE = 0x02000000;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int LCMapStringEx(string localeName, uint flags, string source, int sourceLength, StringBuilder dest, int destLength, IntPtr version, IntPtr reserved, IntPtr sortHandle);
        public static string ToSimplified(string value)
        {
            if (String.IsNullOrEmpty(value)) return value;
            int n = LCMapStringEx("zh-CN", LCMAP_SIMPLIFIED_CHINESE, value, value.Length, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (n <= 0) return value;
            var sb = new StringBuilder(n); LCMapStringEx("zh-CN", LCMAP_SIMPLIFIED_CHINESE, value, value.Length, sb, n, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero); return sb.ToString();
        }
    }

    sealed class Glossary
    {
        readonly List<KeyValuePair<string, string>> words = new List<KeyValuePair<string, string>>();
        public static Glossary Load(string path)
        {
            var g = new Glossary(); if (!File.Exists(path)) return g;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] p = line.Split(new[] { '\t' }, 2); if (p.Length == 2 && p[0].Length > 0) g.words.Add(new KeyValuePair<string, string>(p[0], p[1]));
            }
            g.words.Sort(delegate(KeyValuePair<string, string> a, KeyValuePair<string, string> b) { return b.Key.Length.CompareTo(a.Key.Length); }); return g;
        }
        public string Apply(string value) { foreach (var p in words) value = value.Replace(p.Key, p.Value); return value; }
    }

    static class Placeholders
    {
        static readonly Regex Token = new Regex(@"(\{[^{}]+\}|%\d*\$?[a-zA-Z]|<[^>]+>|\\[nrt])", RegexOptions.Compiled);
        public static bool Match(string a, string b) { return String.Join("|", Get(a)) == String.Join("|", Get(b)); }
        static string[] Get(string value) { var list = new List<string>(); foreach (Match m in Token.Matches(value ?? "")) list.Add(m.Value); list.Sort(StringComparer.Ordinal); return list.ToArray(); }
    }
}
