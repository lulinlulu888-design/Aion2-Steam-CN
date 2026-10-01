using System;
using System.Collections.Generic;
using System.IO;
using Aion2CNTool;

static class CompatibilityTests
{
    static int assertions;
    static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
    static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch { rejected = true; } Assert(rejected, name); }
    static LocalizationTable Copy(LocalizationTable input)
    {
        var output = new LocalizationTable { Prefix = (byte[])input.Prefix.Clone() };
        foreach (Entry row in input.Rows) output.Rows.Add(new Entry { Key = row.Key, Value = row.Value });
        return output;
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var engine = CompatibilityEngine.Load();
            var current = engine.Decode(File.ReadAllBytes(args[0]));
            var translated = engine.Decode(File.ReadAllBytes(args[1]));
            Assert(current.Rows.Count == 152629, "real source count");
            var unchanged = engine.Merge(current, translated);
            Assert(unchanged.Changed == 0 && unchanged.Added == 0 && unchanged.Deleted == 0, "unchanged source fingerprint match");
            Assert(unchanged.Reused > 140000, "real translation reuse");
            Console.WriteLine(unchanged.Summary);
            var altered = Copy(current);
            string removed = altered.Rows[0].Key, changed = altered.Rows[1].Key;
            altered.Rows.RemoveAt(0);
            altered.Rows[0].Value += " UPDATED {new_variable}";
            altered.Rows.Add(new Entry { Key = "TEST_NEW_KEY", Value = "New source {player}" });
            altered.Prefix[0] ^= 0x5A;
            altered.Rows.Reverse();
            var merged = engine.Merge(altered, translated);
            var read = engine.Decode(merged.Payload);
            Assert(merged.Changed == 1 && merged.Added == 1 && merged.Deleted == 1, "added/changed/deleted classification");
            Assert(read.Rows[0].Key == "TEST_NEW_KEY" && read.Rows[0].Value == "New source {player}", "new source retained and current order used");
            Assert(read.Prefix[0] == altered.Prefix[0], "new prefix preserved");
            bool hasRemoved = false, hasChanged = false;
            foreach (Entry row in read.Rows) { hasRemoved |= row.Key == removed; if (row.Key == changed) hasChanged = row.Value.EndsWith(" UPDATED {new_variable}"); }
            Assert(!hasRemoved && hasChanged, "removed keys omitted; changed source not stale-translated");
            // Independent tiny table exercises placeholders and strict source equality.
            var tiny = new LocalizationTable { Prefix = current.Prefix };
            tiny.Rows.Add(new Entry { Key = "test_safe", Value = "Hello {player} <b>world</b>" });
            tiny.Rows.Add(new Entry { Key = "test_unsafe", Value = "Damage {amount}" });
            tiny.Rows.Add(new Entry { Key = "test_exact", Value = "Exact source" });
            foreach (Entry row in tiny.Rows) engine.Sources[row.Key] = CompatibilityEngine.Fingerprint(row.Value);
            var tinyTranslation = Copy(tiny);
            tinyTranslation.Rows[0].Value = "你好 {player} <b>世界</b>";
            tinyTranslation.Rows[1].Value = "伤害";
            tinyTranslation.Rows[2].Value = "精确原文";
            tiny.Rows[2].Value += " ";
            var small = engine.Merge(tiny, tinyTranslation);
            var smallRead = engine.Decode(small.Payload);
            Assert(small.Reused == 1 && small.Unsafe == 1 && small.Changed == 1, "token mismatch and exact-value fallback");
            Assert(smallRead.Rows[0].Value == tinyTranslation.Rows[0].Value && smallRead.Rows[1].Value == tiny.Rows[1].Value && smallRead.Rows[2].Value == tiny.Rows[2].Value, "safe translation only");
            byte[] corrupt = (byte[])merged.Payload.Clone(); corrupt[8] ^= 128;
            Reject(delegate { engine.Decode(corrupt); }, "unsupported/corrupt header rejected");
            Reject(delegate { engine.Decode(new byte[20]); }, "truncated container rejected");
            var duplicates = Copy(tiny); duplicates.Rows.Add(duplicates.Rows[0]);
            Reject(delegate { engine.Decode(engine.Encode(duplicates)); }, "duplicate keys rejected");
            Reject(delegate { Lz4.Decode(new byte[] { 0, 0, 0 }, 0, 3, 4); }, "invalid LZ4 offset rejected");
            foreach (int length in new[] { 1, 14, 15, 270, 65536 })
            {
                byte[] raw = new byte[length]; new Random(length).NextBytes(raw);
                byte[] packed = Lz4.Encode(raw), decoded = Lz4.Decode(packed, 0, packed.Length, length);
                Assert(CompatibilityEngine.Digest(raw) == CompatibilityEngine.Digest(decoded), "LZ4 roundtrip " + length);
            }
            if (args.Length > 2)
            {
                byte[] extracted = engine.ReadPak(args[2], Path.GetTempPath());
                Assert(CompatibilityEngine.Digest(extracted) == CompatibilityEngine.Digest(File.ReadAllBytes(args[0])), "real encrypted Oodle PAK extraction");
                string dll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dependencies", "oo2core_9_win64.dll");
                string savedDll = dll + ".test-save";
                File.Move(dll, savedDll);
                try
                {
                    Reject(delegate { engine.ReadPak(args[2], Path.GetTempPath()); }, "missing Oodle stops before helper launch");
                    File.WriteAllBytes(dll, new byte[] { 1, 2, 3 });
                    Reject(delegate { engine.ReadPak(args[2], Path.GetTempPath()); }, "unverified Oodle stops before helper launch");
                }
                finally { if (File.Exists(dll)) File.Delete(dll); File.Move(savedDll, dll); }
            }
            File.WriteAllBytes(args[0] + ".compatibility-roundtrip.dat", unchanged.Payload);
            File.WriteAllBytes(args[0] + ".compatibility-source.dat", engine.Encode(altered));
            Console.WriteLine("PASS " + assertions + " compatibility assertions; no game files modified.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
