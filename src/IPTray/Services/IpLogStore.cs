using System.Globalization;
using System.Text;

namespace IPTray.Services;

internal sealed record LogEntry(DateTime Timestamp, string Ip, string CountryCode, string CountryName, string Isp)
{
    public bool IsOffline => Ip.Length == 0;
}

/// <summary>Timestamped history of every public-IP change, stored as CSV next to the settings.</summary>
internal static class IpLogStore
{
    private const string Header = "Timestamp,IP,CountryCode,Country,ISP";
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
    private const long MaxBytes = 2 * 1024 * 1024;

    private static readonly object Gate = new();

    /// <summary>
    /// UTF-8 with a byte-order mark, so the CSV opens with the right characters in Excel and
    /// Notepad. The mark is only emitted when the file is created.
    /// </summary>
    private static readonly UTF8Encoding FileEncoding = new(encoderShouldEmitUTF8Identifier: true);

    public static void Append(LogEntry entry)
    {
        lock (Gate)
        {
            try
            {
                Rotate();

                bool needsHeader = !File.Exists(AppPaths.LogFile) || new FileInfo(AppPaths.LogFile).Length == 0;
                var sb = new StringBuilder();
                if (needsHeader)
                {
                    sb.AppendLine(Header);
                }

                sb.Append(Escape(entry.Timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture))).Append(',')
                  .Append(Escape(entry.Ip)).Append(',')
                  .Append(Escape(entry.CountryCode)).Append(',')
                  .Append(Escape(entry.CountryName)).Append(',')
                  .Append(Escape(entry.Isp)).AppendLine();

                File.AppendAllText(AppPaths.LogFile, sb.ToString(), FileEncoding);
            }
            catch (Exception ex)
            {
                CrashReporter.Write(ex);
            }
        }
    }

    /// <summary>Reads the log newest entry first.</summary>
    public static List<LogEntry> Read()
    {
        var entries = new List<LogEntry>();

        lock (Gate)
        {
            try
            {
                if (!File.Exists(AppPaths.LogFile))
                {
                    return entries;
                }

                foreach (string line in File.ReadLines(AppPaths.LogFile, Encoding.UTF8))
                {
                    if (line.Length == 0 || line.StartsWith("Timestamp,", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string[] fields = ParseCsvLine(line);
                    if (fields.Length < 5)
                    {
                        continue;
                    }

                    if (!DateTime.TryParseExact(fields[0], TimestampFormat, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime timestamp))
                    {
                        continue;
                    }

                    entries.Add(new LogEntry(timestamp, fields[1], fields[2], fields[3], fields[4]));
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Write(ex);
            }
        }

        entries.Reverse();
        return entries;
    }

    public static void Clear()
    {
        lock (Gate)
        {
            try
            {
                File.WriteAllText(AppPaths.LogFile, Header + Environment.NewLine, FileEncoding);
            }
            catch (Exception ex)
            {
                CrashReporter.Write(ex);
            }
        }
    }

    private static void Rotate()
    {
        var info = new FileInfo(AppPaths.LogFile);
        if (!info.Exists || info.Length < MaxBytes)
        {
            return;
        }

        File.Move(AppPaths.LogFile, AppPaths.ArchivedLogFile, overwrite: true);
    }

    private static string Escape(string value)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ');

        if (value.IndexOfAny(new[] { ',', '"' }) < 0)
        {
            return value;
        }

        return '"' + value.Replace("\"", "\"\"") + '"';
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
