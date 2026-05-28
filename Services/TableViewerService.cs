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
    /// Сервис для просмотра всех таблиц в базе данных
    /// </summary>
    public class TableViewerService
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<TableViewerService> _logger;

        public TableViewerService(DatabaseService databaseService, ILogger<TableViewerService> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        /// <summary>
        /// Получить список всех таблиц
        /// </summary>
        public async Task<List<string>> GetAllTablesAsync()
        {
            var tables = new List<string>();
            
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null);
                
                if (schemaTable != null)
                {
                    foreach (DataRow row in schemaTable.Rows)
                    {
                        var tableName = row["TABLE_NAME"]?.ToString();
                        var tableType = row["TABLE_TYPE"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(tableName) && 
                            tableType == "TABLE" && 
                            !tableName.StartsWith("MSys"))
                        {
                            tables.Add(tableName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка таблиц");
                throw;
            }

            return tables;
        }

        /// <summary>
        /// Получить данные из указанной таблицы
        /// </summary>
        public async Task<List<Dictionary<string, object>>> GetTableDataAsync(string tableName, int maxRows = 100)
        {
            var data = new List<Dictionary<string, object>>();
            
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var query = $"SELECT TOP {maxRows} * FROM [{tableName}]";
                using var command = new OleDbCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var fieldName = reader.GetName(i);
                        var fieldValue = reader[i];
                        row[fieldName!] = fieldValue == DBNull.Value ? null : fieldValue;
                    }
                    data.Add(row);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении данных из таблицы {tableName}");
                throw;
            }

            return data;
        }

        /// <summary>
        /// Получить структуру таблицы (названия колонок)
        /// </summary>
        public async Task<List<string>> GetTableColumnsAsync(string tableName)
        {
            var columns = new List<string>();

            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns,
                    new object?[] { null, null, tableName, null });

                if (schemaTable != null && schemaTable.Rows.Count > 0)
                {
                    var tmp = new List<(string Name, int Ordinal)>();
                    foreach (DataRow row in schemaTable.Rows)
                    {
                        var columnName = row["COLUMN_NAME"]?.ToString();
                        if (string.IsNullOrEmpty(columnName))
                            continue;

                        var ordVal = row["ORDINAL_POSITION"];
                        var ordinal = ordVal switch
                        {
                            int i => i,
                            long l => (int)l,
                            short s => s,
                            _ => int.MaxValue
                        };
                        tmp.Add((columnName, ordinal));
                    }

                    foreach (var pair in tmp.OrderBy(t => t.Ordinal))
                        columns.Add(pair.Name);
                }

                if (columns.Count == 0)
                {
                    var fromReader = await GetColumnNamesFromReaderAsync(connection, tableName);
                    columns.AddRange(fromReader);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении структуры таблицы {tableName}");
                throw;
            }

            return columns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Резервный способ получить имена полей (для Access схема Columns иногда пустая).
        /// </summary>
        private static async Task<List<string>> GetColumnNamesFromReaderAsync(OleDbConnection connection, string tableName)
        {
            var list = new List<string>();
            var query = $"SELECT TOP 1 * FROM [{tableName}]";
            using var command = new OleDbCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                if (!string.IsNullOrEmpty(name))
                    list.Add(name);
            }

            return list;
        }

        /// <summary>
        /// Получить количество записей в таблице
        /// </summary>
        public async Task<int> GetTableRowCountAsync(string tableName)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var query = $"SELECT COUNT(*) FROM [{tableName}]";
                using var command = new OleDbCommand(query, connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при подсчете записей в таблице {tableName}");
                return 0;
            }
        }
    }
}
