using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Data.SqlTypes;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.Server;

public static class SisulaRenderer
{
    [SqlFunction(DataAccess = DataAccessKind.Read, SystemDataAccess = SystemDataAccessKind.Read, IsDeterministic = false, IsPrecise = true)]
    public static SqlString fn_sisulate(SqlString template, SqlString bindingsJson)
    {
        if (template.IsNull) return SqlString.Null;
        var ctxJson = bindingsJson.IsNull ? string.Empty : bindingsJson.Value ?? string.Empty;
        return new SqlString(Render(template.Value, ctxJson));
    }

    // Split outer SQL and /*~ ... ~*/ blocks
    private static string Render(string tpl, string ctxJson)
    {
        return Render(tpl, ctxJson, null);
    }

    // Overload that carries loop variables to support nested foreach
    private static string Render(string tpl, string ctxJson, Dictionary<string, string> loopVars)
    {
        if (string.IsNullOrEmpty(tpl)) return string.Empty;
        var sb = new StringBuilder();
        int i = 0;
        while (i < tpl.Length)
        {
            int open = tpl.IndexOf("/*~", i, StringComparison.Ordinal);
            if (open < 0)
            {
                // No more blocks; append rest as literal
                sb.Append(tpl, i, tpl.Length - i);
                break;
            }
            // Append literal text before the block
            if (open > i) sb.Append(tpl, i, open - i);

            // Find matching close with nesting awareness
            int pos = open + 3; // after '/*~'
            int depth = 1;
            while (pos < tpl.Length && depth > 0)
            {
                int nextOpen = tpl.IndexOf("/*~", pos, StringComparison.Ordinal);
                int nextClose = tpl.IndexOf("~*/", pos, StringComparison.Ordinal);
                if (nextClose < 0)
                {
                    // Unbalanced; treat remainder as literal
                    sb.Append(tpl, open, tpl.Length - open);
                    i = tpl.Length;
                    goto ContinueOuter;
                }
                if (nextOpen >= 0 && nextOpen < nextClose)
                {
                    depth++;
                    pos = nextOpen + 3;
                }
                else
                {
                    depth--;
                    pos = nextClose + 3;
                }
            }
            int close = pos - 3; // position of '~*/'
            if (depth == 0)
            {
                var blockContent = tpl.Substring(open + 3, close - (open + 3));
                sb.Append(RenderBlock(blockContent, ctxJson, loopVars));
                i = close + 3;
            }
            else
            {
                // Unbalanced; append rest and stop
                sb.Append(tpl, open, tpl.Length - open);
                i = tpl.Length;
            }

        ContinueOuter:;
        }
        return sb.ToString();
    }

    private static string RenderBlock(string block, string ctxJson, Dictionary<string, string> loopVars)
    {
        return RenderScript(block, ctxJson, loopVars);
    }

    // Parse a simple line-based script with directives (supports optional "$/" prefix):
    //   foreach <var> in <path>
    //   end
    // Other lines are treated as content and expanded inline.
    private static string RenderScript(string text, string ctxJson, Dictionary<string, string> loopVars)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Require $/ prefix for all directives
        var reForeach = new Regex("^\\s*\\$/\\s*foreach\\s+(\\w+)\\s+in\\s+(.+?)\\s*$", RegexOptions.IgnoreCase);
        var reEnd = new Regex("^\\s*\\$/\\s*end\\s*$", RegexOptions.IgnoreCase);

        var sb = new StringBuilder();
        int pos = 0;

        while (pos < text.Length)
        {
            int lineEnd = text.IndexOf('\n', pos);
            bool hasNewline = lineEnd >= 0;
            int lineStop = hasNewline ? lineEnd : text.Length;
            // Handle CRLF
            int lineStopTrim = lineStop;
            if (lineStopTrim > pos && text[lineStopTrim - 1] == '\r') lineStopTrim--;
            var line = text.Substring(pos, lineStopTrim - pos);

            var mFor = reForeach.Match(line);
            if (mFor.Success)
            {
                // Capture body until matching end
                pos = hasNewline ? (lineEnd + 1) : text.Length;
                var bodyStart = pos;
                int depth = 1;
                while (pos < text.Length && depth > 0)
                {
                    int nextLineEnd = text.IndexOf('\n', pos);
                    bool nl = nextLineEnd >= 0;
                    int stop = nl ? nextLineEnd : text.Length;
                    int stopTrim = stop;
                    if (stopTrim > pos && text[stopTrim - 1] == '\r') stopTrim--;
                    var innerLine = text.Substring(pos, stopTrim - pos);

                    if (reForeach.IsMatch(innerLine)) depth++;
                    else if (reEnd.IsMatch(innerLine)) { depth--; if (depth == 0) { pos = nl ? (nextLineEnd + 1) : text.Length; break; } }

                    if (depth > 0)
                    {
                        pos = nl ? (nextLineEnd + 1) : text.Length;
                    }
                }
                var body = text.Substring(bodyStart, Math.Max(0, pos - bodyStart - 0));

                var varName = mFor.Groups[1].Value;
                var path = mFor.Groups[2].Value.Trim();
                var items = EnumerateJsonArray(ctxJson, path, loopVars);
                foreach (var itemJson in items)
                {
                    var childVars = loopVars != null
                        ? new Dictionary<string, string>(loopVars)
                        : new Dictionary<string, string>();
                    childVars[varName] = itemJson;
                    sb.Append(RenderScript(body, ctxJson, childVars));
                }
                continue;
            }

            // If this line is a standalone end (at top level), swallow it
            if (reEnd.IsMatch(line))
            {
                pos = hasNewline ? (lineEnd + 1) : text.Length;
                continue;
            }

            // Content line: expand tokens inline and preserve newline
            sb.Append(RenderInline(line, ctxJson, loopVars));
            if (hasNewline) sb.Append('\n');
            pos = hasNewline ? (lineEnd + 1) : text.Length;
        }

        return sb.ToString();
    }

    private static string RenderInline(string text, string ctxJson, Dictionary<string, string> loopVars)
    {
        // Tokens: $path.to.value$ or ${path.to.value}$
        // Path grammar: identifier segments separated by dots, each segment may have an optional numeric index [0]
        // Example matches: S_SCHEMA, source.qualified, source.parts[0].name
        text = Regex.Replace(text,
            @"\$\{?([A-Za-z0-9_]+(?:\[\d+\])?(?:\.[A-Za-z0-9_]+(?:\[\d+\])?)*)\}?\$",
            m => ReadPath(ctxJson, loopVars, m.Groups[1].Value),
            RegexOptions.Singleline);

        return text;
    }

    private static string ReadPath(string ctxJson, Dictionary<string, string> loopVars, string path)
    {
        try
        {
            // Determine if the path targets a loop variable first
            if (loopVars != null)
            {
                foreach (var kvp in loopVars)
                {
                    var v = kvp.Key;
                    var itemJson = kvp.Value ?? string.Empty;
                    if (string.Equals(path, v, StringComparison.Ordinal))
                    {
                        // Entire item
                        return JsonRead(itemJson, "$");
                    }
                    if (path.StartsWith(v + ".", StringComparison.Ordinal))
                    {
                        var inner = "$." + path.Substring(v.Length + 1);
                        return JsonRead(itemJson, inner);
                    }
                }
            }

            // Fallback to global context
            var jsonPath = "$." + path;
            return JsonRead(ctxJson, jsonPath);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string JsonRead(string json, string jsonPath)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;
        // Prefer scalar via JSON_VALUE (NVARCHAR(4000)); otherwise fall back to JSON_QUERY (NVARCHAR(MAX))
        var scalar = ExecScalar("SELECT JSON_VALUE(@j, @p)", json, jsonPath);
        if (!string.IsNullOrEmpty(scalar)) return scalar;
        var complex = ExecScalar("SELECT JSON_QUERY(@j, @p)", json, jsonPath);
        return complex ?? string.Empty;
    }

    private static IEnumerable<string> EnumerateJsonArray(string ctxJson, string path, Dictionary<string, string> loopVars)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(ctxJson)) ctxJson = string.Empty;

        // Resolve against loop variable if referenced; otherwise use global context
        string baseJson = ctxJson;
        string jsonPath;
        if (loopVars != null)
        {
            foreach (var kvp in loopVars)
            {
                var v = kvp.Key;
                if (string.Equals(path, v, StringComparison.Ordinal))
                {
                    baseJson = kvp.Value ?? string.Empty;
                    jsonPath = "$";
                    goto HavePath;
                }
                if (path.StartsWith(v + ".", StringComparison.Ordinal))
                {
                    baseJson = kvp.Value ?? string.Empty;
                    jsonPath = "$." + path.Substring(v.Length + 1);
                    goto HavePath;
                }
            }
        }
        jsonPath = path.StartsWith("$", StringComparison.Ordinal) ? path : ("$." + path);

    HavePath:
        using (var conn = new SqlConnection("context connection=true"))
        using (var cmd = conn.CreateCommand())
        {
            conn.Open();
            cmd.CommandText = "SELECT [value] FROM OPENJSON(@j, @p) ORDER BY [key]";
            var pj = cmd.Parameters.Add("@j", SqlDbType.NVarChar, -1); pj.Value = (object)baseJson ?? string.Empty;
            var pp = cmd.Parameters.Add("@p", SqlDbType.NVarChar, 4000); pp.Value = (object)jsonPath ?? string.Empty;
            using (var rdr = cmd.ExecuteReader())
            {
                while (rdr.Read())
                {
                    // OPENJSON returns NVARCHAR(MAX) for value in default schema
                    list.Add(rdr.IsDBNull(0) ? string.Empty : rdr.GetString(0));
                }
            }
        }
        return list;
    }

    private static string ExecScalar(string sql, string json, string path)
    {
        using (var conn = new SqlConnection("context connection=true"))
        using (var cmd = conn.CreateCommand())
        {
            conn.Open();
            cmd.CommandText = sql;
            var pj = cmd.Parameters.Add("@j", SqlDbType.NVarChar, -1); pj.Value = (object)json ?? string.Empty;
            var pp = cmd.Parameters.Add("@p", SqlDbType.NVarChar, 4000); pp.Value = (object)path ?? string.Empty;
            var result = cmd.ExecuteScalar();
            return result == null || result is DBNull ? null : (string)result;
        }
    }
}
