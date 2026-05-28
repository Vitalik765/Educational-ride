#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SoftwareProductsManager.Services
{
    /// <summary>
    /// Сервис мониторинга и отчётности (вторая часть приложения).
    /// </summary>
    public class MonitoringReportingService
    {
        private readonly DatabaseService _databaseService;
        private readonly TableViewerService _tableViewerService;
        private readonly ILogger<MonitoringReportingService> _logger;

        public MonitoringReportingService(
            DatabaseService databaseService,
            TableViewerService tableViewerService,
            ILogger<MonitoringReportingService> logger)
        {
            _databaseService = databaseService;
            _tableViewerService = tableViewerService;
            _logger = logger;
        }

        public async Task<IReadOnlyList<TableSummaryRow>> GetTableSummariesAsync(bool includeUsersTable)
        {
            var tables = await _tableViewerService.GetAllTablesAsync();
            var rows = new List<TableSummaryRow>();

            foreach (var name in tables.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
            {
                if (!includeUsersTable &&
                    name.Equals("Users", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var count = await _tableViewerService.GetTableRowCountAsync(name);
                    var columns = await _tableViewerService.GetTableColumnsAsync(name);
                    var dateCol = SuggestDateColumn(columns);
                    var resultCol = SuggestResultColumn(columns);

                    rows.Add(new TableSummaryRow(
                        name,
                        count,
                        dateCol,
                        resultCol,
                        dateCol != null && resultCol != null,
                        null));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось построить сводку для таблицы {Table}", name);
                    rows.Add(new TableSummaryRow(name, 0, null, null, false, ex.Message));
                }
            }

            return rows;
        }

        /// <summary>
        /// Отчёт по распределению значений поля результата за период (колонки задаются явно).
        /// </summary>
        public async Task<IReadOnlyList<Dictionary<string, object>>> GetResultDistributionAsync(
            string tableName,
            string dateColumn,
            string resultColumn,
            DateTime dateFrom,
            DateTime dateToInclusive)
        {
            var columns = await _tableViewerService.GetTableColumnsAsync(tableName);
            var dateActual = ResolveActualColumnName(columns, dateColumn)
                ?? throw new InvalidOperationException($"Колонка даты «{dateColumn}» не найдена в таблице «{tableName}».");
            var resultActual = ResolveActualColumnName(columns, resultColumn)
                ?? throw new InvalidOperationException($"Колонка результата «{resultColumn}» не найдена в таблице «{tableName}».");

            return await ExecuteResultDistributionQueryAsync(tableName, dateActual, resultActual, dateFrom, dateToInclusive);
        }

        /// <summary>
        /// Отчёт с авто-подбором колонок (как раньше).
        /// </summary>
        public async Task<IReadOnlyList<Dictionary<string, object>>> GetResultDistributionAsync(
            string tableName,
            DateTime dateFrom,
            DateTime dateToInclusive)
        {
            var columns = await _tableViewerService.GetTableColumnsAsync(tableName);
            var dateCol = SuggestDateColumn(columns);
            var resultCol = SuggestResultColumn(columns);

            if (dateCol == null || resultCol == null)
                return Array.Empty<Dictionary<string, object>>();

            return await ExecuteResultDistributionQueryAsync(tableName, dateCol, resultCol, dateFrom, dateToInclusive);
        }

        /// <summary>
        /// Распределение по полю результата по всей таблице (без фильтра по дате).
        /// </summary>
        public async Task<IReadOnlyList<Dictionary<string, object>>> GetResultDistributionWithoutDateFilterAsync(
            string tableName,
            string resultColumn)
        {
            var columns = await _tableViewerService.GetTableColumnsAsync(tableName);
            var resultActual = ResolveActualColumnName(columns, resultColumn)
                ?? throw new InvalidOperationException($"Колонка результата «{resultColumn}» не найдена в таблице «{tableName}».");

            return await ExecuteResultDistributionNoDateQueryAsync(tableName, resultActual);
        }

        private async Task<IReadOnlyList<Dictionary<string, object>>> ExecuteResultDistributionNoDateQueryAsync(
            string tableName,
            string resultCol)
        {
            var sqlWithCStr = $@"
SELECT CStr([{resultCol}]) AS Результат, COUNT(*) AS Количество
FROM [{tableName}]
GROUP BY CStr([{resultCol}])
ORDER BY COUNT(*) DESC";

            var sqlPlain = $@"
SELECT [{resultCol}] AS Результат, COUNT(*) AS Количество
FROM [{tableName}]
GROUP BY [{resultCol}]
ORDER BY COUNT(*) DESC";

            try
            {
                return await ExecuteReportReaderNoParamsAsync(sqlWithCStr);
            }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Отчёт без даты с CStr не выполнен, повтор без CStr для таблицы {Table}", tableName);
                return await ExecuteReportReaderNoParamsAsync(sqlPlain);
            }
        }

        private async Task<IReadOnlyList<Dictionary<string, object>>> ExecuteResultDistributionQueryAsync(
            string tableName,
            string dateCol,
            string resultCol,
            DateTime dateFrom,
            DateTime dateToInclusive)
        {
            var from = dateFrom.Date;
            var to = dateToInclusive.Date.AddDays(1).AddSeconds(-1);

            // Сначала CStr в GROUP BY (MEMO в Access); при ошибке — обычное поле
            var sqlWithCStr = $@"
SELECT CStr([{resultCol}]) AS Результат, COUNT(*) AS Количество
FROM [{tableName}]
WHERE [{dateCol}] >= ? AND [{dateCol}] <= ?
GROUP BY CStr([{resultCol}])
ORDER BY COUNT(*) DESC";

            var sqlPlain = $@"
SELECT [{resultCol}] AS Результат, COUNT(*) AS Количество
FROM [{tableName}]
WHERE [{dateCol}] >= ? AND [{dateCol}] <= ?
GROUP BY [{resultCol}]
ORDER BY COUNT(*) DESC";

            try
            {
                return await ExecuteReportReaderAsync(sqlWithCStr, from, to);
            }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Отчёт с CStr не выполнен, повтор без CStr для таблицы {Table}", tableName);
                return await ExecuteReportReaderAsync(sqlPlain, from, to);
            }
        }

        private async Task<IReadOnlyList<Dictionary<string, object>>> ExecuteReportReaderAsync(
            string sql,
            DateTime from,
            DateTime to)
        {
            var result = new List<Dictionary<string, object>>();

            using var connection = new OleDbConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            using var command = new OleDbCommand(sql, connection);
            command.Parameters.Add(new OleDbParameter { OleDbType = OleDbType.Date, Value = from });
            command.Parameters.Add(new OleDbParameter { OleDbType = OleDbType.Date, Value = to });

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var val = reader[i];
                    row[name] = val == DBNull.Value ? null! : val;
                }

                result.Add(row);
            }

            return result;
        }

        private async Task<IReadOnlyList<Dictionary<string, object>>> ExecuteReportReaderNoParamsAsync(string sql)
        {
            var result = new List<Dictionary<string, object>>();

            using var connection = new OleDbConnection(_databaseService.GetConnectionString());
            await connection.OpenAsync();
            using var command = new OleDbCommand(sql, connection);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var val = reader[i];
                    row[name] = val == DBNull.Value ? null! : val;
                }

                result.Add(row);
            }

            return result;
        }

        public static string? SuggestDateColumn(IReadOnlyList<string> columns)
            => ResolveDateColumn(columns);

        public static string? SuggestResultColumn(IReadOnlyList<string> columns)
            => ResolveResultColumn(columns);

        private static string? ResolveActualColumnName(IReadOnlyList<string> columns, string requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return null;

            return columns.FirstOrDefault(c => c.Equals(requested.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static string? ResolveDateColumn(IReadOnlyList<string> columns)
        {
            if (columns.Count == 0)
                return null;

            var list = columns.ToList();
            string? Exact(params string[] names) =>
                names.Select(n => list.FirstOrDefault(c => c.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault(x => x != null);

            var hit = Exact(
                "Дата", "Date", "ДатаВремя", "Дата_время", "CreatedDate", "ДатаСоздания",
                "ДатаОперации", "Дата операции", "Дата проведения", "ДатаПлатежа", "Дата платежа",
                "ВремяОперации", "Время операции", "Timestamp", "EventDate", "OperationDate",
                "ДатаРегистрации", "Дата_регистрации", "ДатаВыдачи", "Дата_выдачи", "ДатаОткрытия",
                "Дата_открытия", "ДатаАктивации", "Дата_активации", "ДатаОкончания", "Дата_окончания");
            if (hit != null)
                return hit;

            static bool Has(StringComparison cmp, string col, params string[] subs)
            {
                foreach (var s in subs)
                {
                    if (col.IndexOf(s, cmp) >= 0)
                        return true;
                }

                return false;
            }

            var cmp = StringComparison.OrdinalIgnoreCase;
            hit = list.FirstOrDefault(c =>
                Has(cmp, c, "дата") ||
                Has(cmp, c, "date") ||
                (Has(cmp, c, "время") && c.IndexOf("интер", cmp) < 0) ||
                (Has(cmp, c, "time") && c.IndexOf("date", cmp) >= 0));

            return hit;
        }

        private static string? ResolveResultColumn(IReadOnlyList<string> columns)
        {
            if (columns.Count == 0)
                return null;

            var list = columns.ToList();
            string? Exact(params string[] names) =>
                names.Select(n => list.FirstOrDefault(c => c.Equals(n, StringComparison.OrdinalIgnoreCase)))
                    .FirstOrDefault(x => x != null);

            var hit = Exact(
                "Результат", "Result", "Статус", "Status", "Исход", "КодРезультата", "Код результата",
                "Результат операции", "Статус операции", "СтатусОперации", "РезультатОперации",
                "КодОтвета", "Код ответа", "Ответ", "Outcome");
            if (hit != null)
                return hit;

            var cmp = StringComparison.OrdinalIgnoreCase;
            hit = list.FirstOrDefault(c =>
                c.IndexOf("результат", cmp) >= 0 ||
                c.IndexOf("статус", cmp) >= 0 ||
                c.IndexOf("status", cmp) >= 0 ||
                c.IndexOf("result", cmp) >= 0 ||
                c.IndexOf("исход", cmp) >= 0);

            return hit;
        }
    }

    public sealed record TableSummaryRow(
        string TableName,
        int RowCount,
        string? DateColumnName,
        string? ResultColumnName,
        bool SupportsResultReport,
        string? LoadError);
}
