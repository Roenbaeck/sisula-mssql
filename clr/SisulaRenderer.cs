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
        // If no explicit blocks are present, treat the whole template as a Sisula script
        if (tpl == null) return string.Empty;
        if (tpl.IndexOf("/*~", StringComparison.Ordinal) < 0)
        {
            return RenderScript(tpl, ctxJson, null);
        }
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
    var reEndFor = new Regex("^\\s*\\$/\\s*endfor\\s*$", RegexOptions.IgnoreCase);
    var reEndIf = new Regex("^\\s*\\$/\\s*endif\\s*$", RegexOptions.IgnoreCase); // For future if-blocks

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
                // Capture body until matching endfor (only $/ endfor closes foreach)
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
                    else if (reEndFor.IsMatch(innerLine)) { depth--; if (depth == 0) { pos = nl ? (nextLineEnd + 1) : text.Length; break; } }
                    // $/ endif does NOT close foreach

                    if (depth > 0)
                    {
                        pos = nl ? (nextLineEnd + 1) : text.Length;
                    }
                }
                var body = text.Substring(bodyStart, Math.Max(0, pos - bodyStart - 0));

                var varName = mFor.Groups[1].Value;
                var spec = mFor.Groups[2].Value.Trim();
                string path, whereExpr, orderPath; bool orderDesc;
                ParseForeachSpec(spec, out path, out whereExpr, out orderPath, out orderDesc);

                var items = new List<string>(EnumerateJsonArray(ctxJson, path, loopVars));
                // Filter
                if (!string.IsNullOrEmpty(whereExpr))
                {
                    var filtered = new List<string>(items.Count);
                    foreach (var it in items)
                    {
                        if (EvalConditionOnItem(it, varName, whereExpr)) filtered.Add(it);
                    }
                    items = filtered;
                }
                // Sort
                if (!string.IsNullOrEmpty(orderPath))
                {
                    items.Sort((a, b) => {
                        var ka = GetOrderKey(a, varName, orderPath);
                        var kb = GetOrderKey(b, varName, orderPath);
                        int cmp;
                        double da, db;
                        if (ka == null && kb == null) cmp = 0;
                        else if (ka == null) cmp = 1;
                        else if (kb == null) cmp = -1;
                        else if (TryParseDoubleInvariant(ka, out da) && TryParseDoubleInvariant(kb, out db)) cmp = da.CompareTo(db);
                        else cmp = string.Compare(ka, kb, StringComparison.OrdinalIgnoreCase);
                        return orderDesc ? -cmp : cmp;
                    });
                }

                int n = items.Count;
                for (int i = 0; i < n; i++)
                {
                    var itemJson = items[i];
                    var childVars = loopVars != null
                        ? new Dictionary<string, string>(loopVars)
                        : new Dictionary<string, string>();
                    childVars[varName] = itemJson;
                    // Add $LOOP variable as JSON (manual construction, no third-party JSON, C# 5 compatible)
                    var loopJson = string.Format("{{\"index\":{0},\"count\":{1},\"first\":{2},\"last\":{3}}}",
                        i, n, (i == 0 ? "true" : "false"), (i == n - 1 ? "true" : "false"));
                    childVars["LOOP"] = loopJson;
                    sb.Append(RenderScript(body, ctxJson, childVars));
                }
                continue;
            }

            // If this line is a standalone endfor (at top level), swallow it
            if (reEndFor.IsMatch(line))
            {
                pos = hasNewline ? (lineEnd + 1) : text.Length;
                continue;
            }
            // If this line is a standalone endif (at top level), swallow it (future if-blocks)
            if (reEndIf.IsMatch(line))
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

    private static void ParseForeachSpec(string spec, out string path, out string whereExpr, out string orderPath, out bool orderDesc)
    {
        whereExpr = null; orderPath = null; orderDesc = false; path = spec;
        if (string.IsNullOrEmpty(spec)) { path = string.Empty; return; }
        // Find ORDER BY (last occurrence) and WHERE (before it)
        var specLower = spec.ToLowerInvariant();
        int obIdx = specLower.LastIndexOf(" order by ", StringComparison.Ordinal);
        string left = spec; string right = null;
        if (obIdx >= 0)
        {
            left = spec.Substring(0, obIdx).TrimEnd();
            right = spec.Substring(obIdx + 10).Trim(); // after ' order by '
            if (!string.IsNullOrEmpty(right))
            {
                var parts = right.Split(new char[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    orderPath = parts[0];
                    if (parts.Length > 1) { orderDesc = parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase); }
                }
            }
        }
        int wIdx = left.ToLowerInvariant().LastIndexOf(" where ", StringComparison.Ordinal);
        if (wIdx >= 0)
        {
            path = left.Substring(0, wIdx).TrimEnd();
            whereExpr = left.Substring(wIdx + 7).Trim(); // after ' where '
        }
        else
        {
            path = left.Trim();
        }
    }

    // Reusable: evaluate a simple boolean expression on an item (for foreach where; later for if)
    private static bool EvalConditionOnItem(string itemJson, string varName, string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return true;
        expr = expr.Trim();

        // Function call: contains(x,'y'), startswith(x,'y'), endswith(x,'y')
        var mFunc = Regex.Match(expr, @"^(\w+)\s*\((.*)\)$", RegexOptions.Singleline);
        if (mFunc.Success)
        {
            var fname = mFunc.Groups[1].Value.ToLowerInvariant();
            var argSpan = mFunc.Groups[2].Value;
            var args = SplitArgs(argSpan);
            if (args.Length >= 2)
            {
                var left = ResolveOperand(args[0], itemJson, varName);
                var right = ResolveOperand(args[1], itemJson, varName);
                var ls = ToStringOrNull(left);
                var rs = ToStringOrNull(right);
                if (ls == null || rs == null) return false;
                switch (fname)
                {
                    case "contains": return ls.IndexOf(rs, StringComparison.OrdinalIgnoreCase) >= 0;
                    case "startswith": return ls.StartsWith(rs, StringComparison.OrdinalIgnoreCase);
                    case "endswith": return ls.EndsWith(rs, StringComparison.OrdinalIgnoreCase);
                }
            }
            return false;
        }

        // Comparison operators: ==, !=, >=, <=, >, <
        string op = null; int idx = -1;
        foreach (var cand in new[] {"==","!=",">=","<=",">","<"})
        {
            idx = IndexOfOp(expr, cand);
            if (idx >= 0) { op = cand; break; }
        }
        if (op != null)
        {
            var left = expr.Substring(0, idx).Trim();
            var right = expr.Substring(idx + op.Length).Trim();
            var lv = ResolveOperand(left, itemJson, varName);
            var rv = ResolveOperand(right, itemJson, varName);
            return CompareOperands(lv, rv, op);
        }

        // Fallback: truthy check on a path (relative to varName)
        var val = ResolvePathValue(expr, itemJson, varName);
        return Truthy(val);
    }

    private static string GetOrderKey(string itemJson, string varName, string orderPath)
    {
        if (string.IsNullOrEmpty(orderPath)) return null;
        string inner;
        if (orderPath.StartsWith(varName + ".", StringComparison.Ordinal)) inner = orderPath.Substring(varName.Length + 1);
        else if (orderPath.Equals(varName, StringComparison.Ordinal)) inner = string.Empty;
        else inner = orderPath;
        return string.IsNullOrEmpty(inner) ? JsonRead(itemJson, "$") : JsonRead(itemJson, "$." + inner);
    }

    private static bool Truthy(string v)
    {
        if (v == null) return false;
        var s = v.Trim();
        if (s.Length == 0) return false;
        if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
        if (s == "0") return false;
        if (string.Equals(s, "null", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    // Helpers for conditions
    private static int IndexOfOp(string expr, string op)
    {
        // Find operator outside quotes (simple scan)
        bool inStr = false; char prev = '\0';
        for (int i = 0; i <= expr.Length - op.Length; i++)
        {
            var ch = expr[i];
            if (ch == '\'' && prev != '\\') inStr = !inStr;
            if (!inStr && string.Compare(expr, i, op, 0, op.Length, StringComparison.Ordinal) == 0) return i;
            prev = ch;
        }
        return -1;
    }

    private static string[] SplitArgs(string argList)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        bool inStr = false; char prev = '\0';
        for (int i = 0; i < argList.Length; i++)
        {
            var ch = argList[i];
            if (ch == '\'' && prev != '\\') { inStr = !inStr; sb.Append(ch); prev = ch; continue; }
            if (!inStr && ch == ',') { parts.Add(sb.ToString().Trim()); sb.Clear(); prev = ch; continue; }
            sb.Append(ch); prev = ch;
        }
        if (sb.Length > 0) parts.Add(sb.ToString().Trim());
        return parts.ToArray();
    }

    private static object ResolveOperand(string token, string itemJson, string varName)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        token = token.Trim();
        // String literal in single quotes ('' -> ')
        if (token.Length >= 2 && token[0] == '\'' && token[token.Length - 1] == '\'')
        {
            var inner = token.Substring(1, token.Length - 2).Replace("''", "'");
            return inner;
        }
        // true/false/null
        if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(token, "null", StringComparison.OrdinalIgnoreCase)) return null;
        // number
        double d;
        if (TryParseDoubleInvariant(token, out d)) return d;
        // Otherwise: treat as path relative to varName
        var s = ResolvePathValue(token, itemJson, varName);
        // If path yields numeric, return double
        if (TryParseDoubleInvariant(s, out d)) return d;
        // Booleans from path
        if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(s, "null", StringComparison.OrdinalIgnoreCase)) return null;
        return s;
    }

    private static string ResolvePathValue(string token, string itemJson, string varName)
    {
        string inner;
        if (token.StartsWith(varName + ".", StringComparison.Ordinal)) inner = token.Substring(varName.Length + 1);
        else if (token.Equals(varName, StringComparison.Ordinal)) inner = string.Empty;
        else inner = token;
        return string.IsNullOrEmpty(inner) ? JsonRead(itemJson, "$") : JsonRead(itemJson, "$." + inner);
    }

    private static bool CompareOperands(object lv, object rv, string op)
    {
        // Null handling
        if (lv == null || rv == null)
        {
            switch (op)
            {
                case "==": return lv == null && rv == null;
                case "!=": return !(lv == null && rv == null);
                default: return false; // ordering comparisons with null -> false
            }
        }

        // Numeric compare if both doubles
        if (lv is double && rv is double)
        {
            var ld = (double)lv;
            var rd = (double)rv;
            int c = ld.CompareTo(rd);
            switch (op)
            {
                case "==": return c == 0;
                case "!=": return c != 0;
                case ">": return c > 0;
                case ">=": return c >= 0;
                case "<": return c < 0;
                case "<=": return c <= 0;
            }
        }

        // String compare (case-insensitive)
        var ls = ToStringOrNull(lv) ?? string.Empty;
        var rs = ToStringOrNull(rv) ?? string.Empty;
        int cmp = string.Compare(ls, rs, StringComparison.OrdinalIgnoreCase);
        switch (op)
        {
            case "==": return cmp == 0;
            case "!=": return cmp != 0;
            case ">": return cmp > 0;
            case ">=": return cmp >= 0;
            case "<": return cmp < 0;
            case "<=": return cmp <= 0;
        }
        return false;
    }

    private static bool TryParseDoubleInvariant(string s, out double d)
    {
        return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
    }

    private static string ToStringOrNull(object v)
    {
        if (v == null) return null;
        var ss = v as string; if (ss != null) return ss;
        if (v is bool) { var bb = (bool)v; return bb ? "true" : "false"; }
        if (v is double) { var dd = (double)v; return dd.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        return v.ToString();
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
