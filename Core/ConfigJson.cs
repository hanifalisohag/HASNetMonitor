using System.Text;

namespace HASNetMonitor.Core;

/// <summary>
/// Raised when a config file cannot be read. The callers treat it as "use defaults",
/// so the message never reaches the user; it exists to keep the two backends honest.
/// </summary>
internal sealed class ConfigFormatException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Config JSON, on two backends.
///
/// The modern target uses System.Text.Json. .NET Framework has no such library, and
/// pulling its NuGet package would put several extra DLLs next to the exe - exactly what
/// the small single-file build is trying to avoid. So that target uses the in-box
/// System.Web.Script.Serialization (JavaScriptSerializer), with a small pre-pass that
/// strips comments and trailing commas so both backends accept the same hand-edited
/// config.
///
/// Both backends read and write the same flat set of public properties, and both are
/// covered by the same round-trip tests, so the config on disk is identical either way.
/// </summary>
internal static class ConfigJson
{
#if NETFRAMEWORK
    /// <summary>
    /// JavaScriptSerializer is documented as not thread safe, and its reflection caches
    /// are process-wide, so concurrent calls can interfere even with one instance per
    /// call. Config reads and writes are rare, so a single lock is the cheapest way to
    /// make this backend safe when the widget and a settings dialog touch it at once.
    /// </summary>
    private static readonly object Gate = new();

    public static string Serialize(MonitorConfig config)
    {
        lock (Gate)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
            };

            // The in-box serializer is compact and unordered; indent it so the file
            // stays readable and hand-editable, which is a documented feature.
            string compact = serializer.Serialize(config);
            return Indent(compact);
        }
    }

    public static MonitorConfig Deserialize(string json)
    {
        try
        {
            string cleaned = StripCommentsAndTrailingCommas(json);

            lock (Gate)
            {
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer
                {
                    MaxJsonLength = int.MaxValue,
                };

                return serializer.Deserialize<MonitorConfig>(cleaned) ?? new MonitorConfig();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Runtime.Serialization.SerializationException or FormatException)
        {
            throw new ConfigFormatException("the config is not valid JSON", ex);
        }
    }

    /// <summary>
    /// Removes <c>//</c> and <c>/* */</c> comments and a comma that directly precedes a
    /// closing brace or bracket, which is what the modern backend is configured to
    /// allow. String contents are left exactly as they are, so a hex colour or a path
    /// containing slashes survives.
    /// </summary>
    private static string StripCommentsAndTrailingCommas(string json)
    {
        var output = new StringBuilder(json.Length);
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (inString)
            {
                output.Append(c);
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                output.Append(c);
                continue;
            }

            // Line comment: drop through the end of the line, keeping the newline.
            if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                while (i < json.Length && json[i] != '\n')
                {
                    i++;
                }

                output.Append('\n');
                continue;
            }

            // Block comment: drop through the closing marker.
            if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/'))
                {
                    i++;
                }

                i++;
                continue;
            }

            // A comma with only whitespace between it and } or ] is a trailing comma.
            if (c == ',')
            {
                int look = i + 1;
                while (look < json.Length && char.IsWhiteSpace(json[look]))
                {
                    look++;
                }

                if (look < json.Length && (json[look] == '}' || json[look] == ']'))
                {
                    continue;
                }
            }

            output.Append(c);
        }

        return output.ToString();
    }

    /// <summary>
    /// Indents the compact output of the in-box serializer, one property per line, so
    /// the file reads like the System.Text.Json output.
    /// </summary>
    private static string Indent(string compact)
    {
        var output = new StringBuilder(compact.Length * 2);
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < compact.Length; i++)
        {
            char c = compact[i];

            if (inString)
            {
                output.Append(c);
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    output.Append(c);
                    break;

                case '{' or '[':
                    depth++;
                    output.Append(c).Append('\n').Append(' ', depth * 2);
                    break;

                case '}' or ']':
                    depth--;
                    output.Append('\n').Append(' ', Math.Max(0, depth * 2)).Append(c);
                    break;

                case ',':
                    output.Append(c).Append('\n').Append(' ', Math.Max(0, depth * 2));
                    break;

                default:
                    output.Append(c);
                    break;
            }
        }

        return output.ToString();
    }
#else
    private static readonly System.Text.Json.JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string Serialize(MonitorConfig config) =>
        System.Text.Json.JsonSerializer.Serialize(config, SerializerOptions);

    public static MonitorConfig Deserialize(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<MonitorConfig>(json, SerializerOptions)
                ?? new MonitorConfig();
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new ConfigFormatException("the config is not valid JSON", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new ConfigFormatException("the config could not be read", ex);
        }
    }
#endif
}
