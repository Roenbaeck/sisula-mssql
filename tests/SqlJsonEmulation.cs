using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Test build only (defines SISULA_TEST). Supplies the two members of SisulaRenderer that talk to
// SQL Server, JsonRead and OpenJsonValues, by emulating the T-SQL JSON functions they call:
//
//   JSON_VALUE(@j, @p)   a scalar as text; NULL for an object, an array, null or a missing path,
//                        and for a string longer than 4000 characters (lax mode)
//   JSON_QUERY(@j, @p)   an object or array as the original text; NULL for anything else
//   OPENJSON(@j, @p)     one row per element of an array, or per property of an object; the value
//                        as text, null as NULL, an object or array as the original text
//
// The emulation is only as good as this description. The SQL files in sql/ run the same cases on
// a real server.
public static partial class SisulaRenderer
{
    private static string JsonRead(string json, string jsonPath)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;
        // The same two-step lookup as the SQL Server build: JSON_VALUE, then JSON_QUERY.
        var scalar = JsonValue(json, jsonPath);
        if (!string.IsNullOrEmpty(scalar)) return scalar;
        var complex = JsonQuery(json, jsonPath);
        return complex ?? string.Empty;
    }

    private static List<string> OpenJsonValues(string baseJson, string jsonPath)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(baseJson)) return list;
        var root = ParseCached(baseJson);
        var node = MiniJson.Select(root, jsonPath);
        if (node == null) return list;
        if (node.Kind == 'a')
        {
            foreach (var item in node.Items) list.Add(MiniJson.OpenJsonText(baseJson, item));
        }
        else if (node.Kind == 'o')
        {
            foreach (var member in node.Members) list.Add(MiniJson.OpenJsonText(baseJson, member.Value));
        }
        return list;
    }

    // SQL Server parses the text on every call; a template that reads thousands of values from a large
    // document would make the emulation needlessly slow, so parsed documents are kept for reuse.
    // Test build only: a SAFE assembly may not have mutable static fields.
    private static readonly Dictionary<string, JsonNode> ParsedDocuments = new Dictionary<string, JsonNode>(StringComparer.Ordinal);

    private static JsonNode ParseCached(string json)
    {
        JsonNode node;
        if (ParsedDocuments.TryGetValue(json, out node)) return node;
        if (ParsedDocuments.Count >= 2048) ParsedDocuments.Clear();
        node = MiniJson.Parse(json);
        ParsedDocuments[json] = node;
        return node;
    }

    private static string JsonValue(string json, string jsonPath)
    {
        var node = MiniJson.Select(ParseCached(json), jsonPath);
        if (node == null) return null;
        string text;
        switch (node.Kind)
        {
            case 's': text = node.Text; break;
            case 'n':
            case 'b': text = json.Substring(node.Start, node.End - node.Start); break;
            default: return null;
        }
        return text.Length > 4000 ? null : text;
    }

    private static string JsonQuery(string json, string jsonPath)
    {
        var node = MiniJson.Select(ParseCached(json), jsonPath);
        if (node == null) return null;
        if (node.Kind != 'o' && node.Kind != 'a') return null;
        return json.Substring(node.Start, node.End - node.Start);
    }
}

// A JSON value with its position in the source, so the original text can be returned.
public sealed class JsonNode
{
    public char Kind;   // o object, a array, s string, n number, b boolean, z null
    public int Start;
    public int End;     // exclusive
    public string Text; // the unescaped value of a string
    public List<JsonNode> Items = new List<JsonNode>();
    public List<KeyValuePair<string, JsonNode>> Members = new List<KeyValuePair<string, JsonNode>>();
}

public static class MiniJson
{
    public static JsonNode Parse(string json)
    {
        int pos = 0;
        var node = ParseValue(json, ref pos);
        SkipWhite(json, ref pos);
        if (pos != json.Length) throw new ArgumentException("JSON text is not properly formatted: unexpected character at " + pos);
        return node;
    }

    // Follows a path as the renderer writes them: $ then ."name" (a doubled quote stands for one),
    // .name or [n], in any combination.
    public static JsonNode Select(JsonNode root, string path)
    {
        var node = root;
        int i = 0;
        if (path == null || path.Length == 0 || path[0] != '$') throw new ArgumentException("JSON path must start with $: " + path);
        i = 1;
        while (i < path.Length && node != null)
        {
            if (path[i] == '[')
            {
                int close = path.IndexOf(']', i);
                int index;
                if (close < 0 || !int.TryParse(path.Substring(i + 1, close - i - 1), NumberStyles.None, CultureInfo.InvariantCulture, out index))
                    throw new ArgumentException("Bad array index in JSON path: " + path);
                node = (node.Kind == 'a' && index < node.Items.Count) ? node.Items[index] : null;
                i = close + 1;
            }
            else if (path[i] == '.')
            {
                i++;
                string name;
                if (i < path.Length && path[i] == '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (true)
                    {
                        if (i >= path.Length) throw new ArgumentException("Unterminated name in JSON path: " + path);
                        if (path[i] == '"')
                        {
                            if (i + 1 < path.Length && path[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                            i++;
                            break;
                        }
                        sb.Append(path[i]); i++;
                    }
                    name = sb.ToString();
                }
                else
                {
                    int start = i;
                    while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                    name = path.Substring(start, i - start);
                }
                JsonNode found = null;
                if (node.Kind == 'o')
                {
                    foreach (var member in node.Members)
                    {
                        if (string.Equals(member.Key, name, StringComparison.Ordinal)) { found = member.Value; break; }
                    }
                }
                node = found;
            }
            else
            {
                throw new ArgumentException("Unexpected character in JSON path: " + path);
            }
        }
        return node;
    }

    // The value column of OPENJSON, as the renderer reads it: text, or the original JSON for
    // an object or array. A JSON null is NULL, which the renderer turns into an empty string.
    public static string OpenJsonText(string json, JsonNode node)
    {
        switch (node.Kind)
        {
            case 's': return node.Text;
            case 'z': return string.Empty;
            default: return json.Substring(node.Start, node.End - node.Start);
        }
    }

    // Writes a value as compact JSON, the way JSON.stringify does for the JavaScript runner.
    public static string Compact(string json, JsonNode node)
    {
        var sb = new StringBuilder();
        WriteCompact(json, node, sb);
        return sb.ToString();
    }

    private static void WriteCompact(string json, JsonNode node, StringBuilder sb)
    {
        switch (node.Kind)
        {
            case 'o':
                sb.Append('{');
                for (int i = 0; i < node.Members.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteString(node.Members[i].Key, sb);
                    sb.Append(':');
                    WriteCompact(json, node.Members[i].Value, sb);
                }
                sb.Append('}');
                break;
            case 'a':
                sb.Append('[');
                for (int i = 0; i < node.Items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteCompact(json, node.Items[i], sb);
                }
                sb.Append(']');
                break;
            case 's':
                WriteString(node.Text, sb);
                break;
            default:
                sb.Append(json, node.Start, node.End - node.Start);
                break;
        }
    }

    private static void WriteString(string value, StringBuilder sb)
    {
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 32) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }

    private static void SkipWhite(string s, ref int pos)
    {
        while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\r' || s[pos] == '\n')) pos++;
    }

    private static JsonNode ParseValue(string s, ref int pos)
    {
        SkipWhite(s, ref pos);
        if (pos >= s.Length) throw new ArgumentException("JSON text is not properly formatted: unexpected end");
        var node = new JsonNode();
        node.Start = pos;
        char c = s[pos];
        if (c == '{')
        {
            node.Kind = 'o';
            pos++;
            SkipWhite(s, ref pos);
            if (pos < s.Length && s[pos] == '}') { pos++; }
            else
            {
                while (true)
                {
                    SkipWhite(s, ref pos);
                    if (pos >= s.Length || s[pos] != '"') throw new ArgumentException("JSON text is not properly formatted: expected a property name at " + pos);
                    var name = ParseString(s, ref pos);
                    SkipWhite(s, ref pos);
                    if (pos >= s.Length || s[pos] != ':') throw new ArgumentException("JSON text is not properly formatted: expected ':' at " + pos);
                    pos++;
                    node.Members.Add(new KeyValuePair<string, JsonNode>(name, ParseValue(s, ref pos)));
                    SkipWhite(s, ref pos);
                    if (pos < s.Length && s[pos] == ',') { pos++; continue; }
                    if (pos < s.Length && s[pos] == '}') { pos++; break; }
                    throw new ArgumentException("JSON text is not properly formatted: expected ',' or '}' at " + pos);
                }
            }
        }
        else if (c == '[')
        {
            node.Kind = 'a';
            pos++;
            SkipWhite(s, ref pos);
            if (pos < s.Length && s[pos] == ']') { pos++; }
            else
            {
                while (true)
                {
                    node.Items.Add(ParseValue(s, ref pos));
                    SkipWhite(s, ref pos);
                    if (pos < s.Length && s[pos] == ',') { pos++; continue; }
                    if (pos < s.Length && s[pos] == ']') { pos++; break; }
                    throw new ArgumentException("JSON text is not properly formatted: expected ',' or ']' at " + pos);
                }
            }
        }
        else if (c == '"')
        {
            node.Kind = 's';
            node.Text = ParseString(s, ref pos);
        }
        else if (string.CompareOrdinal(s, pos, "true", 0, 4) == 0) { node.Kind = 'b'; pos += 4; }
        else if (string.CompareOrdinal(s, pos, "false", 0, 5) == 0) { node.Kind = 'b'; pos += 5; }
        else if (string.CompareOrdinal(s, pos, "null", 0, 4) == 0) { node.Kind = 'z'; pos += 4; }
        else if (c == '-' || (c >= '0' && c <= '9'))
        {
            node.Kind = 'n';
            pos++;
            while (pos < s.Length && ((s[pos] >= '0' && s[pos] <= '9') || s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E' || s[pos] == '+' || s[pos] == '-')) pos++;
        }
        else throw new ArgumentException("JSON text is not properly formatted: unexpected character '" + c + "' at " + pos);
        node.End = pos;
        return node;
    }

    private static string ParseString(string s, ref int pos)
    {
        var sb = new StringBuilder();
        pos++; // opening quote
        while (true)
        {
            if (pos >= s.Length) throw new ArgumentException("JSON text is not properly formatted: unterminated string");
            char c = s[pos++];
            if (c == '"') break;
            if (c != '\\') { sb.Append(c); continue; }
            if (pos >= s.Length) throw new ArgumentException("JSON text is not properly formatted: unterminated escape");
            char e = s[pos++];
            switch (e)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (pos + 4 > s.Length) throw new ArgumentException("JSON text is not properly formatted: bad \\u escape");
                    sb.Append((char)int.Parse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    pos += 4;
                    break;
                default: throw new ArgumentException("JSON text is not properly formatted: bad escape \\" + e);
            }
        }
        return sb.ToString();
    }
}
