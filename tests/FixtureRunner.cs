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
        if (args[0] == "--sql") return WriteSqlTest(args);
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

    // Writes the fixtures as a T-SQL script that runs each one through dbo.fn_sisulate on a real
    // server and reports PASS or FAIL. The text is ASCII, and line breaks and other control or
    // non-ASCII characters are written as NCHAR(n), so that nothing an editor or git does to line
    // endings can change what a case means. Comparison is exact: binary collation, and the same length,
    // because T-SQL's = ignores trailing spaces and, by default, case.
    private static int WriteSqlTest(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("usage: FixtureRunner --sql <fixtures directory> <output file>"); return 2; }
        var sb = new StringBuilder();
        var files = Directory.GetFiles(args[1], "*.json");
        Array.Sort(files, StringComparer.Ordinal);
        int cases = 0;
        var body = new StringBuilder();
        foreach (var file in files)
        {
            var json = File.ReadAllText(file, Encoding.UTF8);
            var root = MiniJson.Parse(json);
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
                cases++;
                var fileLiteral = SqlText(Path.GetFileName(file));
                var nameLiteral = SqlText(name);
                body.Append("-- ").Append(Path.GetFileName(file)).Append(": ").AppendLine(AsciiComment(name));
                body.Append("SET @t = ").Append(SqlText(template)).AppendLine(";");
                body.Append("SET @b = ").Append(SqlText(bindings)).AppendLine(";");
                if (error == null)
                {
                    body.Append("SET @e = ").Append(SqlText(expected)).AppendLine(";");
                    body.AppendLine("BEGIN TRY");
                    body.AppendLine("    SET @a = dbo.fn_sisulate(@t, @b);");
                    body.AppendLine("END TRY");
                    body.AppendLine("BEGIN CATCH");
                    body.AppendLine("    SET @a = N'threw: ' + ERROR_MESSAGE();");
                    body.AppendLine("END CATCH;");
                    body.Append("INSERT @results (file_name, case_name, status, expected, actual) VALUES (")
                        .Append(fileLiteral).Append(", ").Append(nameLiteral)
                        .AppendLine(", CASE WHEN @a COLLATE Latin1_General_BIN2 = @e COLLATE Latin1_General_BIN2 AND DATALENGTH(@a) = DATALENGTH(@e) THEN 'PASS' ELSE 'FAIL' END, @e, @a);");
                }
                else
                {
                    body.Append("SET @e = ").Append(SqlText(error)).AppendLine(";");
                    body.AppendLine("BEGIN TRY");
                    body.AppendLine("    SET @a = dbo.fn_sisulate(@t, @b);");
                    body.Append("    INSERT @results (file_name, case_name, status, expected, actual) VALUES (")
                        .Append(fileLiteral).Append(", ").Append(nameLiteral)
                        .AppendLine(", 'FAIL', N'error containing ' + @e, @a);");
                    body.AppendLine("END TRY");
                    body.AppendLine("BEGIN CATCH");
                    body.Append("    INSERT @results (file_name, case_name, status, expected, actual) VALUES (")
                        .Append(fileLiteral).Append(", ").Append(nameLiteral)
                        .AppendLine(", CASE WHEN CHARINDEX(@e, ERROR_MESSAGE()) > 0 THEN 'PASS' ELSE 'FAIL' END, N'error containing ' + @e, ERROR_MESSAGE());");
                    body.AppendLine("END CATCH;");
                }
                body.AppendLine();
            }
        }
        sb.AppendLine("/*");
        sb.AppendLine("    The shared Sisula fixtures, run through dbo.fn_sisulate on this server.");
        sb.AppendLine();
        sb.AppendLine("    GENERATED by tests\\run-fixtures.ps1 -WriteSqlTest from tests/fixtures in the sisula repository. Do not edit.");
        sb.AppendLine("    " + cases + " cases. Run it in a database where the assembly and dbo.fn_sisulate are installed.");
        sb.AppendLine();
        sb.AppendLine("    The first result set lists the cases that fail, if any, then the ones that pass; the second");
        sb.AppendLine("    counts them. Every case must pass. A case that throws is reported as a failure that says so,");
        sb.AppendLine("    and the run goes on. The cases about more than ten items check that an array keeps its order,");
        sb.AppendLine("    which depends on how the rows of OPENJSON are sorted; the emulation used by run-fixtures.ps1");
        sb.AppendLine("    cannot check that, only a server can.");
        sb.AppendLine("*/");
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine("DECLARE @results TABLE (id int IDENTITY(1,1), file_name nvarchar(100), case_name nvarchar(400), status varchar(4), expected nvarchar(max), actual nvarchar(max));");
        sb.AppendLine("DECLARE @t nvarchar(max), @b nvarchar(max), @e nvarchar(max), @a nvarchar(max);");
        sb.AppendLine();
        sb.Append(body);
        sb.AppendLine("SELECT file_name, case_name, status, expected, actual FROM @results ORDER BY CASE status WHEN 'FAIL' THEN 0 ELSE 1 END, id;");
        sb.AppendLine("SELECT SUM(CASE WHEN status = 'PASS' THEN 1 ELSE 0 END) AS passed, SUM(CASE WHEN status = 'FAIL' THEN 1 ELSE 0 END) AS failed FROM @results;");
        File.WriteAllText(args[2], sb.ToString(), new ASCIIEncoding());
        Console.WriteLine("Wrote " + args[2] + ": " + cases + " cases");
        return 0;
    }

    // A Unicode string literal, written as a concatenation where a character needs NCHAR(n).
    private static string SqlText(string s)
    {
        if (s == null) return "NULL";
        var sb = new StringBuilder("N'");
        bool open = true;
        foreach (var ch in s)
        {
            if (ch < 32 || ch > 126)
            {
                sb.Append(open ? "' + " : " + ").Append("NCHAR(").Append((int)ch).Append(")");
                open = false;
                continue;
            }
            if (!open) { sb.Append(" + N'"); open = true; }
            if (ch == '\'') sb.Append("''"); else sb.Append(ch);
        }
        if (open) sb.Append("'");
        var text = sb.ToString();
        // N'' + NCHAR(10) is clearer without the empty literal in front of it.
        return text.StartsWith("N'' + ", StringComparison.Ordinal) ? text.Substring(6) : text;
    }

    // Comments are ASCII too, and a comment holds one line.
    private static string AsciiComment(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s) sb.Append(ch < 32 || ch > 126 ? '?' : ch);
        return sb.ToString();
    }

    private static string Quote(string s)
    {
        if (s == null) return "null";
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }
}
