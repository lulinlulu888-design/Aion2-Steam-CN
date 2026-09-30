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
[assembly: AssemblyVersion("2.1.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

namespace Aion2CNTool
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
#if DEBUG
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
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    sealed class MainForm : Form
    {
        const string ToolVersion = "2.1.0";
        const string PayloadVersion = "2026.10.01.3";
        const string SupportedGameBuild = "global-152629-2026.10.01";
        const string SupportedPakHash = "5BFCDEC64CED073002C9E58210A3C956CCF321A6A69E9474575C79C133B37733";
        const string PayloadHash = "5F4FC5443B496F4D7DEB8A214AE807EF87C6EC78CB8405CD390D8FDB5EBE118F";
        readonly TextBox steam = new TextBox();
        readonly TextBox log = new TextBox();
        readonly Button install = new Button();
        readonly Button restore = new Button();
        readonly Button inspect = new Button();
        readonly Label statusBanner = new Label();
#if DEBUG
        bool testMode;
        bool failAfterPayloadForTest;
#endif

        public MainForm()
        {
            Text = "Aion2-Steam-CN v" + ToolVersion;
            ClientSize = new Size(760, 530);
            MinimumSize = new Size(720, 510);
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;

            var title = new Label { Text = "永恒之塔2 Steam版简体中文工具", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), AutoSize = true, Left = 20, Top = 18 };
            var sub = new Label { Text = "工具 v" + ToolVersion + " · 语言包 " + PayloadVersion + " · 自动备份并支持一键还原", AutoSize = true, Left = 22, Top = 58, ForeColor = Color.DimGray };
            Controls.Add(title); Controls.Add(sub);

            AddPathRow("Steam / Global 客户端", steam, 108, BrowseSteam);

            inspect.Text = "重新检测"; inspect.SetBounds(20, 158, 135, 38); inspect.Click += delegate { SafeRun(Inspect); };
            install.Text = "一键安装 / 更新"; install.SetBounds(170, 158, 160, 38); install.BackColor = Color.FromArgb(38, 116, 221); install.ForeColor = Color.White; install.FlatStyle = FlatStyle.Flat; install.Click += delegate { SafeRun(Install); };
            restore.Text = "一键还原"; restore.SetBounds(345, 158, 135, 38); restore.Click += delegate { SafeRun(Restore); };
            Controls.Add(inspect); Controls.Add(install); Controls.Add(restore);

            statusBanner.SetBounds(20, 210, 720, 48); statusBanner.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            statusBanner.TextAlign = ContentAlignment.MiddleCenter; statusBanner.Font = new Font(Font.FontFamily, 11F, FontStyle.Bold);
            statusBanner.BorderStyle = BorderStyle.FixedSingle; Controls.Add(statusBanner);
            ShowStatus("准备就绪，请先确认兼容性检测结果。", false);

            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.SetBounds(20, 274, 720, 230); log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            log.BackColor = Color.FromArgb(248, 249, 251); Controls.Add(log);

            steam.Text = DiscoverSteamClient();
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
        void BrowseInto(TextBox target)
        {
            using (var f = new FolderBrowserDialog())
            {
                f.Description = "请选择 Steam 的 AION2 游戏根目录";
                f.SelectedPath = Directory.Exists(target.Text) ? target.Text : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = NormalizeClientPath(f.SelectedPath);
                    SafeRun(Inspect);
                }
            }
        }

        string NormalizeClientPath(string selected)
        {
            if (String.IsNullOrWhiteSpace(selected)) return selected;
            DirectoryInfo current;
            try { current = new DirectoryInfo(Path.GetFullPath(selected.Trim().Trim('"'))); }
            catch { return selected; }
            for (int i = 0; i < 7 && current != null; i++, current = current.Parent)
            {
                if (Directory.Exists(Path.Combine(current.FullName, @"Aion2\Content"))) return current.FullName;
                string child = Path.Combine(current.FullName, "AION2");
                if (Directory.Exists(Path.Combine(child, @"Aion2\Content"))) return child;
            }
            return selected;
        }

        void SafeRun(Action action)
        {
            try { UseWaitCursor = true; SetButtons(false); action(); }
            catch (Exception ex) { ShowStatus("操作失败：" + ex.Message, null); Append("错误：" + ex.Message); MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { UseWaitCursor = false; SetButtons(true); }
        }

        void SetButtons(bool value) { inspect.Enabled = value; install.Enabled = value; restore.Enabled = value && File.Exists(StateFile) && File.Exists(BackupPak); }
        void Append(string text) { log.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine); }
        void ShowStatus(string text, bool? success)
        {
            statusBanner.Text = text;
            if (success == true) { statusBanner.BackColor = Color.FromArgb(224, 247, 232); statusBanner.ForeColor = Color.FromArgb(20, 108, 55); }
            else if (success == false) { statusBanner.BackColor = Color.FromArgb(242, 244, 247); statusBanner.ForeColor = Color.FromArgb(74, 85, 104); }
            else { statusBanner.BackColor = Color.FromArgb(255, 235, 235); statusBanner.ForeColor = Color.FromArgb(170, 30, 30); }
        }

        string SteamPak { get { return Path.Combine(steam.Text.Trim(), @"Aion2\Content\Paks\L10N\Text\en-US\pakchunk502000-Windows_0_P.pak"); } }
        string SteamDat { get { return Path.Combine(steam.Text.Trim(), @"Aion2\Content\L10N\Text\en-US\L10NString.dat"); } }
        string BackupPak { get { return SteamPak + ".aion2cn.v2.backup"; } }
        string BackupDat { get { return SteamDat + ".aion2cn.v2.backup"; } }
        string LegacyBackupPak { get { return SteamPak + ".aion2cn.original"; } }
        string LegacyBackupDat { get { return SteamDat + ".aion2cn.original"; } }
        string LegacyStateFile { get { return Path.Combine(Path.GetDirectoryName(SteamDat), "Aion2CNTool.install.json"); } }
        string StateFile { get { return Path.Combine(Path.GetDirectoryName(SteamDat), "Aion2CNTool.state"); } }
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
            try
            {
                using (Stream source = OpenPayload())
                using (var output = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None)) { source.CopyTo(output); output.Flush(true); }
                if (Hash(pending) != PayloadHash) throw new InvalidDataException("内置优化语言数据校验失败，请重新下载安装程序。");
                AtomicReplace(pending, destination);
            }
            finally { TryDelete(pending); }
        }

        void WriteTextAtomic(string destination, string value)
        {
            string pending = destination + ".aion2cn.pending";
            try
            {
                File.WriteAllText(pending, value, new UTF8Encoding(false));
                AtomicReplace(pending, destination);
            }
            finally { TryDelete(pending); }
        }

        static void AtomicReplace(string pending, string destination)
        {
            if (File.Exists(destination))
            {
                try { File.Replace(pending, destination, null, true); return; }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
            }
            File.Copy(pending, destination, true);
        }

        static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

        Dictionary<string, string> ReadState()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(StateFile)) return values;
            foreach (string raw in File.ReadAllLines(StateFile, Encoding.UTF8))
            {
                int at = raw.IndexOf('=');
                if (at > 0) values[raw.Substring(0, at).Trim()] = raw.Substring(at + 1).Trim();
            }
            return values;
        }

        void WriteState(string status, bool hadDat, string prePakHash, string preDatHash)
        {
            string content =
                "format=2\r\n" +
                "status=" + status + "\r\n" +
                "tool_version=" + ToolVersion + "\r\n" +
                "payload_version=" + PayloadVersion + "\r\n" +
                "game_build=" + SupportedGameBuild + "\r\n" +
                "installed_utc=" + DateTime.UtcNow.ToString("o") + "\r\n" +
                "had_dat=" + hadDat.ToString() + "\r\n" +
                "pre_pak_hash=" + (prePakHash ?? "") + "\r\n" +
                "pre_dat_hash=" + (preDatHash ?? "") + "\r\n" +
                "payload_hash=" + PayloadHash + "\r\n";
            WriteTextAtomic(StateFile, content);
        }

        static bool StateBool(Dictionary<string, string> state, string key)
        {
            string value; return state.TryGetValue(key, out value) && String.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
        }

        void Inspect()
        {
            log.Clear();
            steam.Text = NormalizeClientPath(steam.Text);
            Append("工具版本：" + ToolVersion + "；语言包版本：" + PayloadVersion + "；目标构建：" + SupportedGameBuild + "。");
            if (!Directory.Exists(steam.Text.Trim())) Append("未找到 Steam 游戏目录。请点击“浏览…”手工选择 AION2 根目录。");
            else Append("游戏目录：" + steam.Text.Trim());
            Append(File.Exists(SteamPak) ? "已找到 Steam 语言包入口。" : "未找到语言包入口：" + SteamPak);
            string original = FindCompatiblePak();
            if (original != null) Append("全球版构建校验：完全匹配（152,629 条）。");
            else if (File.Exists(SteamPak)) Append("全球版构建校验：版本不同或缺少原始包，安装时将拒绝覆盖。");
            try { Append(PayloadDigest() == PayloadHash ? "内置优化包校验通过：152,629 条；地图、剧情、物品、技能和 NPC 术语已统一校对。" : "内置优化包校验失败，请重新下载安装程序。"); }
            catch (Exception ex) { Append("内置优化包不可用：" + ex.Message); }
            var state = ReadState(); string status;
            if (state.TryGetValue("status", out status))
            {
                string installedPayload; state.TryGetValue("payload_version", out installedPayload);
                if (status == "installed") Append("已安装：语言包 " + installedPayload + "。可更新或一键还原。");
                else if (status == "restored") Append("当前已还原到安装前状态；可再次安装，原始备份仍受校验保护。");
                else Append("检测到上次操作未完成；再次安装会自动修复，也可一键还原。");
            }
            else if (File.Exists(LegacyStateFile)) Append("检测到旧版工具状态；安装 v" + ToolVersion + " 时会迁移原始备份。");
            else Append("当前未安装本工具。首次安装会保存安装前状态。");
            install.Text = state.Count > 0 && status != "restored" ? "修复 / 更新" : "一键安装";
            restore.Enabled = File.Exists(StateFile) && File.Exists(BackupPak);
        }

        void EnsureGameClosed()
        {
#if DEBUG
            if (testMode) return;
#endif
            if (Process.GetProcessesByName("AION2").Length > 0 || Process.GetProcessesByName("Aion2-Win64-Shipping").Length > 0)
                throw new InvalidOperationException("请先完全退出《永恒之塔2》，再执行安装或还原。");
        }

#if DEBUG
        public void RunFileOpsTest(string root)
        {
            testMode = true;
            steam.Text = root;
            string beforePak = Hash(SteamPak);
            string beforeDat = File.Exists(SteamDat) ? Hash(SteamDat) : null;
            Install();
            if (!File.Exists(BackupPak) || Hash(SteamDat) != PayloadHash || !File.ReadAllText(SteamPak, Encoding.UTF8).StartsWith("AION2CN " + ToolVersion))
                throw new InvalidDataException("staged install verification failed");
            string installedPak = Hash(SteamPak); string installedDat = Hash(SteamDat);
            failAfterPayloadForTest = true;
            bool interruptedRejected = false;
            try { Install(); } catch (IOException) { interruptedRejected = true; }
            finally { failAfterPayloadForTest = false; }
            if (!interruptedRejected || Hash(SteamPak) != installedPak || Hash(SteamDat) != installedDat)
                throw new InvalidDataException("interrupted update rollback verification failed");
            Install();
            if (Hash(SteamDat) != PayloadHash) throw new InvalidDataException("staged update verification failed");
            Restore();
            if (Hash(SteamPak) != beforePak) throw new InvalidDataException("pak restore verification failed");
            if (beforeDat == null ? File.Exists(SteamDat) : Hash(SteamDat) != beforeDat) throw new InvalidDataException("dat restore verification failed");
            Install();
            Restore();
            if (Hash(SteamPak) != beforePak || (beforeDat == null ? File.Exists(SteamDat) : Hash(SteamDat) != beforeDat))
                throw new InvalidDataException("reinstall after restore verification failed");
        }

        public void RunDiscoveryTest(string output)
        {
            string detectedSteam = steam.Text;
            string steamMarker = Path.Combine(detectedSteam, @"Aion2\Content\Paks\L10N\Text\en-US\pakchunk502000-Windows_0_P.pak");
            File.WriteAllText(output,
                "Steam=" + detectedSteam + Environment.NewLine +
                "SteamFound=" + (File.Exists(steamMarker) || File.Exists(steamMarker + ".tool_bak")) + Environment.NewLine, Encoding.UTF8);
        }
#endif

        void Install()
        {
            ShowStatus("正在安全安装语言包，请勿关闭程序……", false);
            EnsureGameClosed();
            if (PayloadDigest() != PayloadHash) throw new InvalidDataException("内置优化语言数据校验失败，请重新下载安装程序。");
            steam.Text = NormalizeClientPath(steam.Text);
            if (!Directory.Exists(steam.Text.Trim())) throw new DirectoryNotFoundException("找不到 Steam 游戏目录。请点击“浏览…”手工选择 AION2 根目录。");
            string original = FindCompatiblePak();
            if (original == null) throw new InvalidOperationException("Steam 游戏版本与语言包 " + PayloadVersion + " 不匹配，或找不到原始英文包。请在 Steam 校验游戏文件后等待工具更新，禁止强行覆盖。");
            Directory.CreateDirectory(Path.GetDirectoryName(SteamDat));
            EnsureFreeSpace();
            bool newState = !File.Exists(StateFile);
            bool hadDat = false; string prePakHash = null; string preDatHash = null;
            if (newState) PrepareBackups(out hadDat, out prePakHash, out preDatHash);
            else
            {
                var state = ReadState();
                hadDat = StateBool(state, "had_dat");
                state.TryGetValue("pre_pak_hash", out prePakHash); state.TryGetValue("pre_dat_hash", out preDatHash);
                ValidateBackups(state);
            }
            string rollbackPak = SteamPak + ".aion2cn.rollback";
            string rollbackDat = SteamDat + ".aion2cn.rollback";
            bool currentHadDat = File.Exists(SteamDat);
            File.Copy(SteamPak, rollbackPak, true);
            if (currentHadDat) File.Copy(SteamDat, rollbackDat, true); else TryDelete(rollbackDat);
            try
            {
                WriteState("preparing", hadDat, prePakHash, preDatHash);
                WritePayload(SteamDat);
#if DEBUG
                if (failAfterPayloadForTest) throw new IOException("simulated interruption after payload write");
#endif
                WriteTextAtomic(SteamPak, "AION2CN " + ToolVersion + " payload=" + PayloadVersion + "\r\n");
                if (Hash(SteamDat) != PayloadHash) throw new InvalidDataException("写入后的语言文件校验失败，已取消安装。");
                WriteState("installed", hadDat, prePakHash, preDatHash);
            }
            catch
            {
                File.Copy(rollbackPak, SteamPak, true);
                if (currentHadDat) File.Copy(rollbackDat, SteamDat, true); else TryDelete(SteamDat);
                if (newState) { TryDelete(StateFile); TryDelete(BackupPak); TryDelete(BackupDat); }
                throw;
            }
            finally { TryDelete(rollbackPak); TryDelete(rollbackDat); TryDelete(SteamPak + ".aion2cn.pending"); TryDelete(SteamDat + ".aion2cn.pending"); }
            TryDelete(LegacyStateFile);
            Inspect();
            Append("安装完成：工具 " + ToolVersion + "，语言包 " + PayloadVersion + "，游戏构建 " + SupportedGameBuild + "。");
            Append("152,629 条文本结构与占位符校验通过；地图、剧情、物品、技能和 NPC 术语已按国服惯用译名统一。");
            ShowStatus("✓ 安装完成，可以启动《永恒之塔2》", true);
#if DEBUG
            if (!testMode)
#endif
                MessageBox.Show(this, "汉化安装完成，可以启动游戏。\n\n如遇到异常，可随时运行本工具并点击“一键还原”。", "安装完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        string FindCompatiblePak()
        {
            string[] candidates = { SteamPak, SteamPak + ".tool_bak", BackupPak, LegacyBackupPak };
            foreach (string candidate in candidates)
                try { if (File.Exists(candidate) && Hash(candidate) == SupportedPakHash) return candidate; } catch { }
            return null;
        }

        void PrepareBackups(out bool hadDat, out string prePakHash, out string preDatHash)
        {
            if (File.Exists(BackupPak) || File.Exists(BackupDat))
                throw new InvalidOperationException("发现没有状态文件对应的 v2 备份。为防止覆盖未知备份，请先保留这些文件并联系维护者：" + BackupPak);
            try
            {
                if (File.Exists(LegacyStateFile) && File.Exists(LegacyBackupPak))
                {
                    File.Copy(LegacyBackupPak, BackupPak, false);
                    hadDat = File.Exists(LegacyBackupDat);
                    if (hadDat) File.Copy(LegacyBackupDat, BackupDat, false);
                    Append("已迁移 v1.x 原始备份，后续还原将回到旧版工具安装前状态。");
                }
                else
                {
                    if (!File.Exists(SteamPak)) throw new FileNotFoundException("找不到当前 Steam 语言包入口。", SteamPak);
                    File.Copy(SteamPak, BackupPak, false);
                    hadDat = File.Exists(SteamDat);
                    if (hadDat) File.Copy(SteamDat, BackupDat, false);
                }
                prePakHash = Hash(BackupPak);
                preDatHash = hadDat ? Hash(BackupDat) : null;
            }
            catch
            {
                TryDelete(BackupPak); TryDelete(BackupDat); throw;
            }
        }

        void ValidateBackups(Dictionary<string, string> state)
        {
            string format, status, expectedPak;
            if (!state.TryGetValue("format", out format) || format != "2" ||
                !state.TryGetValue("status", out status) || (status != "installed" && status != "preparing" && status != "restored") ||
                !state.TryGetValue("pre_pak_hash", out expectedPak) || String.IsNullOrWhiteSpace(expectedPak))
                throw new InvalidDataException("安装状态文件不完整或已损坏，已拒绝继续操作。请保留备份并联系维护者。");
            if (!File.Exists(BackupPak)) throw new FileNotFoundException("原始 PAK 备份缺失，已拒绝继续操作。", BackupPak);
            if (Hash(BackupPak) != expectedPak) throw new InvalidDataException("原始 PAK 备份校验失败，已拒绝继续操作。");
            if (StateBool(state, "had_dat"))
            {
                if (!File.Exists(BackupDat)) throw new FileNotFoundException("安装前已有语言文件，但对应备份缺失。", BackupDat);
                string expectedDat; state.TryGetValue("pre_dat_hash", out expectedDat);
                if (!String.IsNullOrEmpty(expectedDat) && Hash(BackupDat) != expectedDat) throw new InvalidDataException("原有语言文件备份校验失败，已拒绝继续操作。");
            }
        }

        void EnsureFreeSpace()
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(SteamDat));
                var drive = new DriveInfo(root);
                long required = 20L * 1024L * 1024L;
                if (drive.AvailableFreeSpace < required) throw new IOException("游戏所在磁盘可用空间不足 20 MiB，无法安全创建备份和临时文件。");
            }
            catch (IOException) { throw; }
            catch (Exception ex) { Append("提示：无法读取磁盘剩余空间（" + ex.Message + "），将继续依赖写入错误保护。"); }
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
            var state = ReadState();
            if (state.Count == 0 || !File.Exists(BackupPak)) throw new FileNotFoundException("没有找到完整的安装状态与备份，已拒绝猜测性还原。", BackupPak);
            ValidateBackups(state);
            EnsureFreeSpace();
            string rollbackPak = SteamPak + ".aion2cn.restore.rollback";
            string rollbackDat = SteamDat + ".aion2cn.restore.rollback";
            bool currentHadPak = File.Exists(SteamPak), currentHadDat = File.Exists(SteamDat);
            if (currentHadPak) File.Copy(SteamPak, rollbackPak, true);
            if (currentHadDat) File.Copy(SteamDat, rollbackDat, true);
            try
            {
                File.Copy(BackupPak, SteamPak, true);
                if (StateBool(state, "had_dat")) File.Copy(BackupDat, SteamDat, true); else TryDelete(SteamDat);
                string expectedPak; state.TryGetValue("pre_pak_hash", out expectedPak);
                if (Hash(SteamPak) != expectedPak) throw new InvalidDataException("还原后的 PAK 校验失败，备份文件仍保留。");
                string expectedDat; state.TryGetValue("pre_dat_hash", out expectedDat);
                if (StateBool(state, "had_dat") && Hash(SteamDat) != expectedDat) throw new InvalidDataException("还原后的原有语言文件校验失败，备份文件仍保留。");
                string prePakHash; state.TryGetValue("pre_pak_hash", out prePakHash);
                string preDatHash; state.TryGetValue("pre_dat_hash", out preDatHash);
                WriteState("restored", StateBool(state, "had_dat"), prePakHash, preDatHash);
            }
            catch
            {
                if (currentHadPak) File.Copy(rollbackPak, SteamPak, true); else TryDelete(SteamPak);
                if (currentHadDat) File.Copy(rollbackDat, SteamDat, true); else TryDelete(SteamDat);
                throw;
            }
            finally { TryDelete(rollbackPak); TryDelete(rollbackDat); }
            Inspect();
            Append("已逐字节恢复到首次安装本工具前的状态。备份文件仍保留。");
            ShowStatus("✓ 已还原到安装前状态", true);
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
