using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.IO;
using System.Text;

// Runs the shared Sisula conformance fixtures, tests/fixtures/*.json in the sisula repository,
// against SisulaRenderer.fn_sisulate, the function SQL Server calls. Test build only.
//
//   FixtureRunner.exe <fixtures directory> [name filter]
//
// A fixture is { name, template, bindings, expected }, or { name, template, bindings, error }.
// bindings is serialised compactly, as the JavaScript runner does with JSON.stringify.
// With "error", the case passes when rendering throws a message containing that text.
public static class FixtureRunner
{
    public static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("usage: FixtureRunner <fixtures directory> [name filter]\n       FixtureRunner --render <template file> <bindings file>"); return 2; }
        if (args[0] == "--render") return Render(args);
        string filter = args.Length > 1 ? args[1] : null;
        int passed = 0, failed = 0;
        var files = Directory.GetFiles(args[0], "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            var json = File.ReadAllText(file, Encoding.UTF8);
            var root = MiniJson.Parse(json);
            if (root.Kind != 'a') { Console.WriteLine("FAIL " + Path.GetFileName(file) + ": not an array of fixtures"); failed++; continue; }
            foreach (var fixture in root.Items)
            {
                string name = null, template = null, bindings = "{}", expected = null, error = null;
                foreach (var member in fixture.Members)
                {
                    switch (member.Key)
                    {
                        case "name": name = member.Value.Text; break;
                        case "template": template = member.Value.Text; break;
                        case "bindings": bindings = MiniJson.Compact(json, member.Value); break;
                        case "expected": expected = member.Value.Text; break;
                        case "error": error = member.Value.Text; break;
                    }
                }
                if (filter != null && (name == null || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)) continue;

                string actual = null, thrown = null;
                try { actual = SisulaRenderer.fn_sisulate(new SqlString(template), new SqlString(bindings)).Value; }
                catch (Exception e) { thrown = e.Message; }

                bool ok = error != null
                    ? (thrown != null && thrown.IndexOf(error, StringComparison.Ordinal) >= 0)
                    : (thrown == null && string.Equals(actual, expected, StringComparison.Ordinal));
                if (ok) { passed++; continue; }
                failed++;
                Console.WriteLine("FAIL " + Path.GetFileName(file) + ": " + name);
                Console.WriteLine("  expected: " + (error != null ? "error containing " + Quote(error) : Quote(expected)));
                Console.WriteLine("  actual:   " + (thrown != null ? "threw " + Quote(thrown) : Quote(actual)));
            }
        }
        Console.WriteLine(passed + " passed, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }

    // Renders one template file against one bindings file and writes the result to a file, byte for
    // byte (UTF-8, no byte order mark, no added line break), or to standard output without a file name.
    private static int Render(string[] args)
    {
        if (args.Length < 3 || args.Length > 4) { Console.Error.WriteLine("usage: FixtureRunner --render <template file> <bindings file> [output file]"); return 2; }
        var template = File.ReadAllText(args[1], Encoding.UTF8).Replace("\r\n", "\n");
        var bindings = File.ReadAllText(args[2], Encoding.UTF8);
        var rendered = SisulaRenderer.fn_sisulate(new SqlString(template), new SqlString(bindings)).Value;
        var bytes = new UTF8Encoding(false).GetBytes(rendered);
        if (args.Length == 4)
        {
            File.WriteAllBytes(args[3], bytes);
        }
        else
        {
            using (var stdout = Console.OpenStandardOutput()) stdout.Write(bytes, 0, bytes.Length);
        }
        return 0;
    }

    private static string Quote(string s)
    {
        if (s == null) return "null";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }
}
