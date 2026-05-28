#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SoftwareProductsManager.Services
{
    public static class CsvExportHelper
    {
        public static void WriteDictionaryRows(string filePath, IReadOnlyList<Dictionary<string, object>> rows)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Путь к файлу не задан.", nameof(filePath));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? ".");

            using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            if (rows.Count == 0)
            {
                writer.WriteLine("Нет данных для экспорта");
                return;
            }

            var columns = rows[0].Keys.ToList();
            writer.WriteLine(string.Join(";", columns.Select(Escape)));

            foreach (var row in rows)
            {
                var line = string.Join(";", columns.Select(c => Escape(FormatValue(row.TryGetValue(c, out var v) ? v : null))));
                writer.WriteLine(line);
            }
        }

        private static string FormatValue(object? value)
        {
            if (value == null) return "";
            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            if (value is IFormattable fmt)
                return fmt.ToString(null, CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private static string Escape(string? value)
        {
            var s = value ?? "";
            if (s.Contains(';', StringComparison.Ordinal) || s.Contains('"', StringComparison.Ordinal) || s.Contains('\n', StringComparison.Ordinal) || s.Contains('\r', StringComparison.Ordinal))
                return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            return s;
        }
    }
}
