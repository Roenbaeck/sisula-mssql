using System;
using System.Data.SqlTypes;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.Server;
using Newtonsoft.Json.Linq;

public static class SisulaRenderer
{
    [SqlFunction(IsDeterministic = false, IsPrecise = true)]
    public static SqlString fn_sisulate(SqlString template, SqlString bindingsJson)
    {
        if (template.IsNull) return SqlString.Null;
        var ctx = string.IsNullOrWhiteSpace(bindingsJson.IsNull ? null : bindingsJson.Value)
            ? new JObject()
            : JObject.Parse(bindingsJson.Value);
        return new SqlString(Render(template.Value, ctx));
    }

    // Split outer SQL and /*~ ... ~*/ blocks
    private static string Render(string tpl, JObject ctx)
    {
        var parts = Regex.Split(tpl, @"/\*~|\~\*/", RegexOptions.Singleline);
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0) { sb.Append(parts[i]); continue; } // passthrough SQL
            sb.Append(RenderBlock(parts[i], ctx));
        }
        return sb.ToString();
    }

    private static string RenderBlock(string block, JObject ctx)
    {
        // foreach: "foreach <var> in <path>\n...body...\nend"
        var foreachRe = new Regex(@"^\s*foreach\s+(\w+)\s+in\s+([^\r\n]+)\r?\n([\s\S]*?)\r?\n\s*end\s*$",
                                  RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var m = foreachRe.Match(block);
        if (m.Success)
        {
            var varName = m.Groups[1].Value;
            var path = m.Groups[2].Value.Trim();
            var body = m.Groups[3].Value;

            var token = SelectToken(ctx, path);
            if (token is JArray arr)
            {
                var sb = new StringBuilder();
                foreach (var item in arr)
                {
                    var child = (JObject)ctx.DeepClone();
                    child[varName] = item.DeepClone();
                    sb.Append(RenderInline(body, child));
                }
                return sb.ToString();
            }
            return string.Empty;
        }

        // inline expansions
        return RenderInline(block, ctx);
    }

    private static string RenderInline(string text, JObject ctx)
    {
        // Tokens: $path.to.value$ or ${path.to.value}$
        text = Regex.Replace(text,
            @"\$\{?([A-Za-z0-9_.$\[\]]+)\}?\$",
            m => ReadPath(ctx, m.Groups[1].Value),
            RegexOptions.Singleline);

        return text;
    }

    private static string ReadPath(JObject ctx, string path)
    {
        try
        {
            var token = SelectToken(ctx, path);
            if (token == null) return string.Empty;
            if (token.Type == JTokenType.String) return token.Value<string>();
            return token.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static JToken SelectToken(JObject ctx, string path)
    {
        // Allow bracket indexers in paths (e.g., source.parts[0].name)
        var norm = path.Replace("[", ".[" ).Trim();
        return ctx.SelectToken(norm, errorWhenNoMatch: false);
    }
}
