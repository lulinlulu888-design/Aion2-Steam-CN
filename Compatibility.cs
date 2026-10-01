using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Aion2CNTool
{
    sealed class BackupMigration : IDisposable
    {
        readonly List<KeyValuePair<string, string>> moved = new List<KeyValuePair<string, string>>();
        bool committed;
        internal string Archive;
        internal BackupMigration() { }
        internal BackupMigration(string[] paths, string looseDat)
        {
            Archive = Path.Combine(Path.GetDirectoryName(paths[0]), "cn-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Archive);
            try
            {
                if (File.Exists(looseDat)) File.Copy(looseDat, Path.Combine(Archive, "previous-installed-L10NString.dat"));
                foreach (string path in paths)
                    if (File.Exists(path))
                    {
                        string archived = Path.Combine(Archive, Path.GetFileName(path));
                        File.Move(path, archived); moved.Add(new KeyValuePair<string, string>(path, archived));
                    }
            }
            catch { Dispose(); throw; }
        }
        internal void Commit() { committed = true; }
        public void Dispose()
        {
            if (committed) return;
            for (int i = moved.Count - 1; i >= 0; i--)
            {
                var pair = moved[i];
                // Only the exact state/backup paths moved by this transaction.
                if (File.Exists(pair.Key)) File.Delete(pair.Key);
                File.Move(pair.Value, pair.Key);
            }
            moved.Clear();
        }
    }

    sealed class LocalizationTable
    {
        internal byte[] Prefix;
        internal List<Entry> Rows = new List<Entry>();
    }

    sealed class CompatibilityResult
    {
        internal byte[] Payload;
        internal int Total, Reused, Changed, Added, Deleted, Unsafe;
        internal string Summary
        {
            get { return "当前 " + Total + " 条；复用 " + Reused + " 条；源文变化 " + Changed + " 条，新增 " + Added + " 条，删除 " + Deleted + " 条，变量/标签不匹配 " + Unsafe + " 条。新增、变化或不安全条目保留新版原文。"; }
        }
    }

    sealed class CompatibilityEngine
    {
        const int MaxBytes = 256 * 1024 * 1024;
        readonly byte[] pakKey, aesKey, headerKey;
        internal readonly Dictionary<string, byte[]> Sources = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly UnicodeEncoding Utf16 = new UnicodeEncoding(false, false, true);
        static readonly Regex Tokens = new Regex(@"<[^>]*>|\{[^}]*\}|%\d*\$?[a-zA-Z]|\\[nrt]", RegexOptions.CultureInvariant);

        internal CompatibilityEngine(Stream reference)
        {
            using (var gzip = new GZipStream(reference, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gzip, Utf8))
            {
                if (Encoding.ASCII.GetString(Exact(reader, 5)) != "A2CR\x01") throw new InvalidDataException("兼容性参考格式错误。");
                pakKey = Exact(reader, 32); aesKey = Exact(reader, 32); headerKey = Exact(reader, 16);
                int count = reader.ReadInt32(); CheckCount(count);
                for (int i = 0; i < count; i++)
                {
                    int length = reader.ReadInt32();
                    if (length < 1 || length > 65536) throw new InvalidDataException("参考键长度异常。");
                    Sources.Add(Utf8.GetString(Exact(reader, length)), Exact(reader, 32));
                }
                if (reader.BaseStream.ReadByte() != -1) throw new InvalidDataException("参考数据尾部异常。");
            }
        }

        internal static CompatibilityEngine Load()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aion2CNTool.Compatibility.Reference.gz");
            if (stream == null) throw new InvalidDataException("缺少兼容性参考数据，请下载完整工具。");
            using (stream) return new CompatibilityEngine(stream);
        }

        static byte[] Exact(BinaryReader reader, int count)
        {
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException();
            return bytes;
        }
        static void CheckCount(int count) { if (count < 1 || count > 1000000) throw new InvalidDataException("语言条目数量异常。"); }
        internal static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
        internal static byte[] Fingerprint(string value)
        {
            using (var sha = SHA256.Create()) return sha.ComputeHash(Utf8.GetBytes(value));
        }
        static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        byte[] Crypt(byte[] bytes, bool encrypt)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = aesKey; aes.Mode = CipherMode.ECB; aes.Padding = PaddingMode.None;
                using (var transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor())
                    return transform.TransformFinalBlock(bytes, 0, bytes.Length);
            }
        }
        static string ReadString(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count == 0) return "";
            if (count == Int32.MinValue || Math.Abs(count) > 4000000) throw new InvalidDataException("字符串长度异常。");
            byte[] bytes = Exact(reader, count > 0 ? count : checked(-count * 2));
            int terminator = count > 0 ? 1 : 2;
            for (int i = 1; i <= terminator; i++) if (bytes[bytes.Length - i] != 0) throw new InvalidDataException("字符串未终止。");
            return (count > 0 ? (Encoding)Utf8 : Utf16).GetString(bytes, 0, bytes.Length - terminator);
        }
        static void WriteString(BinaryWriter writer, string value)
        {
            if (value.Length == 0) { writer.Write(0); return; }
            bool ascii = true; foreach (char c in value) if (c > 127) { ascii = false; break; }
            if (ascii) { byte[] bytes = Utf8.GetBytes(value); writer.Write(bytes.Length + 1); writer.Write(bytes); writer.Write((byte)0); }
            else { writer.Write(-(value.Length + 1)); writer.Write(Utf16.GetBytes(value)); writer.Write((ushort)0); }
        }

        internal LocalizationTable Decode(byte[] data)
        {
            if (data.Length < 36 || data.Length > MaxBytes || BitConverter.ToInt32(data, 0) != 2) throw new InvalidDataException("不支持的语言容器。");
            byte[] header = new byte[16];
            for (int i = 0; i < 16; i++) header[i] = (byte)(data[i + 4] ^ headerKey[i]);
            int packed = BitConverter.ToInt32(header, 0), type = BitConverter.ToInt32(header, 4), aligned = BitConverter.ToInt32(header, 8), rawSize = BitConverter.ToInt32(header, 12);
            if (type != 2 || aligned < 32 || aligned % 16 != 0 || aligned != data.Length - 20 || packed < 1 || packed > aligned - 32 || rawSize < 16 || rawSize > MaxBytes)
                throw new InvalidDataException("语言容器格式或密钥不兼容，已拒绝安装。");
            byte[] encrypted = new byte[aligned]; Buffer.BlockCopy(data, 20, encrypted, 0, aligned);
            byte[] clear = Crypt(encrypted, false);
            var table = new LocalizationTable { Prefix = new byte[32] }; Buffer.BlockCopy(clear, 0, table.Prefix, 0, 32);
            byte[] raw = Lz4.Decode(clear, 32, packed, rawSize);
            using (var reader = new BinaryReader(new MemoryStream(raw), Utf8))
            {
                if (reader.ReadInt32() != 1 || ReadString(reader) != "AION2") throw new InvalidDataException("语言表结构不兼容。");
                int count = reader.ReadInt32(); CheckCount(count);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    string key = ReadString(reader), value = ReadString(reader);
                    if (key.Length == 0 || !keys.Add(key)) throw new InvalidDataException("语言表含空键或重复键。");
                    table.Rows.Add(new Entry { Key = key, Value = value });
                }
                long remain = reader.BaseStream.Length - reader.BaseStream.Position;
                if (remain == 4 && reader.ReadInt32() == 0) { }
                else if (remain != 0) throw new InvalidDataException("语言表尾部异常。");
            }
            return table;
        }

        internal byte[] Encode(LocalizationTable table)
        {
            byte[] raw;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    writer.Write(1); WriteString(writer, "AION2"); writer.Write(table.Rows.Count);
                    foreach (Entry entry in table.Rows) { WriteString(writer, entry.Key); WriteString(writer, entry.Value); }
                    writer.Write(0);
                }
                raw = stream.ToArray();
            }
            if (raw.Length > MaxBytes - 1024 * 1024) throw new InvalidDataException("语言表超过安全大小限制。");
            byte[] packed = Lz4.Encode(raw);
            byte[] clear = new byte[(32 + packed.Length + 15) & ~15];
            Buffer.BlockCopy(table.Prefix, 0, clear, 0, 32); Buffer.BlockCopy(packed, 0, clear, 32, packed.Length);
            using (var output = new MemoryStream())
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(2);
                byte[] header = new byte[16];
                Buffer.BlockCopy(BitConverter.GetBytes(packed.Length), 0, header, 0, 4); Buffer.BlockCopy(BitConverter.GetBytes(2), 0, header, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(clear.Length), 0, header, 8, 4); Buffer.BlockCopy(BitConverter.GetBytes(raw.Length), 0, header, 12, 4);
                for (int i = 0; i < 16; i++) header[i] ^= headerKey[i];
                writer.Write(header); writer.Write(Crypt(clear, true)); return output.ToArray();
            }
        }

        static bool SafeTokens(string source, string translated)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Match token in Tokens.Matches(source)) { int count; counts.TryGetValue(token.Value, out count); counts[token.Value] = count + 1; }
            foreach (Match token in Tokens.Matches(translated)) { int count; if (!counts.TryGetValue(token.Value, out count) || count == 0) return false; counts[token.Value] = count - 1; }
            foreach (int count in counts.Values) if (count != 0) return false;
            return true;
        }

        internal CompatibilityResult Merge(LocalizationTable current, LocalizationTable translated)
        {
            var translations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Entry entry in translated.Rows) translations.Add(entry.Key, entry.Value);
            var result = new CompatibilityResult { Total = current.Rows.Count };
            var output = new LocalizationTable { Prefix = current.Prefix };
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (Entry entry in current.Rows)
            {
                present.Add(entry.Key); string value = entry.Value, translation; byte[] original;
                if (!Sources.TryGetValue(entry.Key, out original)) result.Added++;
                else if (!Equal(original, Fingerprint(entry.Value))) result.Changed++;
                else if (translations.TryGetValue(entry.Key, out translation))
                {
                    if (SafeTokens(entry.Value, translation)) { value = translation; result.Reused++; }
                    else result.Unsafe++;
                }
                output.Rows.Add(new Entry { Key = entry.Key, Value = value });
            }
            foreach (string key in Sources.Keys) if (!present.Contains(key)) result.Deleted++;
            result.Payload = Encode(output);
            LocalizationTable checkedTable = Decode(result.Payload);
            if (!Equal(current.Prefix, checkedTable.Prefix) || checkedTable.Rows.Count != output.Rows.Count) throw new InvalidDataException("动态语言包回读失败。");
            for (int i = 0; i < output.Rows.Count; i++)
                if (output.Rows[i].Key != checkedTable.Rows[i].Key || output.Rows[i].Value != checkedTable.Rows[i].Value) throw new InvalidDataException("动态语言包回读不一致。");
            return result;
        }

        internal byte[] ReadPak(string pak, string gameRoot)
        {
            string directory = Path.Combine(Path.GetTempPath(), "Aion2CN-Decode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                // Do not redistribute Oodle or let the helper silently fetch a DLL.
                string dll = FindOodle(gameRoot);
                if (dll == null) throw new InvalidOperationException("新版本文本检测需要本地 Oodle 解码组件。请将合法取得的 oo2core_9_win64.dll 放在工具旁的 dependencies 文件夹；没有组件时不会绕过校验。已验证版本仍可直接安装。");
                File.Copy(dll, Path.Combine(directory, "oo2core_9_win64.dll"));
                string helper = Path.Combine(directory, "repak.exe");
                using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Aion2CNTool.Compatibility.Repak.exe"))
                {
                    if (source == null) throw new InvalidDataException("缺少解包组件。");
                    using (var output = File.Create(helper)) source.CopyTo(output);
                }
                string key = BitConverter.ToString(pakKey).Replace("-", "");
                var start = new ProcessStartInfo(helper, "--aes-key " + key + " get \"" + pak + "\" \"AION2/Content/L10N/Text/en-US/L10NString.dat\"")
                { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var process = Process.Start(start))
                {
                    // Drain both pipes, bound output, and terminate on timeout.
                    var outputTask = Task.Factory.StartNew(delegate
                    {
                        using (var output = new MemoryStream())
                        {
                            byte[] buffer = new byte[65536]; int read;
                            while ((read = process.StandardOutput.BaseStream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (output.Length + read > MaxBytes) throw new InvalidDataException("解包文件过大。");
                                output.Write(buffer, 0, read);
                            }
                            return output.ToArray();
                        }
                    });
                    var errorTask = process.StandardError.ReadToEndAsync();
                    try
                    {
                        if (!Task.WaitAll(new Task[] { outputTask, errorTask }, 60000)) throw new TimeoutException("语言解包超时。");
                        if (!process.WaitForExit(1000) || process.ExitCode != 0) throw new InvalidDataException("当前语言 PAK 无法解包，格式或密钥可能已改变。已停止安装。");
                        return outputTask.Result;
                    }
                    finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } }
                }
            }
            finally { try { Directory.Delete(directory, true); } catch { } }
        }

        static string FindOodle(string root)
        {
            string[] paths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "oo2core_9_win64.dll"),
                Path.Combine(root, "Aion2", "Binaries", "Win64", "oo2core_9_win64.dll")
            };
            foreach (string path in paths)
                if (File.Exists(path))
                {
                    string hash = Digest(File.ReadAllBytes(path));
                    // Known local SDK 2.9.10 and Steam-installed game copies.
                    if (hash == "6F5D41A7892EA6B2DB420F2458DAD2F84A63901C9A93CE9497337B16C195F457" ||
                        hash == "8595A4795F1E0C7F548598F3E2AA528B6BE5456C6D934C665182EAECB04156C0") return path;
                }
            return null;
        }
    }

    static class Lz4
    {
        static int Length(byte[] input, ref int position, int end, int initial)
        {
            int length = initial;
            if (initial == 15) { int extra; do { if (position >= end) throw new InvalidDataException("LZ4 长度截断。"); extra = input[position++]; length = checked(length + extra); } while (extra == 255); }
            return length;
        }
        internal static byte[] Decode(byte[] input, int start, int count, int size)
        {
            byte[] output = new byte[size]; int end = checked(start + count), position = start, write = 0;
            while (position < end)
            {
                int token = input[position++], literals = Length(input, ref position, end, token >> 4);
                if (literals > end - position || literals > size - write) throw new InvalidDataException("LZ4 字面量越界。");
                Buffer.BlockCopy(input, position, output, write, literals); position += literals; write += literals;
                if (position == end) break;
                if (end - position < 2) throw new InvalidDataException("LZ4 偏移截断。");
                int offset = input[position++] | input[position++] << 8;
                int match = checked(Length(input, ref position, end, token & 15) + 4);
                if (offset == 0 || offset > write || match > size - write) throw new InvalidDataException("LZ4 匹配越界。");
                for (int i = 0; i < match; i++) { output[write] = output[write - offset]; write++; }
            }
            if (write != size) throw new InvalidDataException("LZ4 输出大小不一致。");
            return output;
        }
        static void Extra(Stream output, int length) { while (length >= 255) { output.WriteByte(255); length -= 255; } output.WriteByte((byte)length); }
        internal static byte[] Encode(byte[] input)
        {
            // Literal-only valid LZ4 block: no native compression dependency.
            using (var output = new MemoryStream())
            {
                output.WriteByte((byte)(Math.Min(input.Length, 15) << 4));
                if (input.Length >= 15) Extra(output, input.Length - 15);
                output.Write(input, 0, input.Length); return output.ToArray();
            }
        }
    }
}
