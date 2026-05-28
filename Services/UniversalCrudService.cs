#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SoftwareProductsManager.Services
{
    /// <summary>
    /// Универсальный сервис для CRUD операций с любыми таблицами
    /// </summary>
    public class UniversalCrudService
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<UniversalCrudService> _logger;

        public UniversalCrudService(DatabaseService databaseService, ILogger<UniversalCrudService> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        /// <summary>
        /// Получить структуру таблицы (колонки и их типы)
        /// </summary>
        public async Task<List<ColumnInfo>> GetTableStructureAsync(string tableName)
        {
            var columns = new List<ColumnInfo>();
            
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, 
                    new object?[] { null, null, tableName, null });
                
                // Получаем информацию о первичных ключах
                var primaryKeys = new HashSet<string>();
                try
                {
                    var pkSchema = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Primary_Keys, 
                        new object?[] { null, null, tableName });
                    if (pkSchema != null)
                    {
                        foreach (DataRow pkRow in pkSchema.Rows)
                        {
                            var pkColumn = pkRow["COLUMN_NAME"]?.ToString();
                            if (!string.IsNullOrEmpty(pkColumn))
                            {
                                primaryKeys.Add(pkColumn);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось получить информацию о первичных ключах для таблицы {TableName}", tableName);
                }
                
                if (schemaTable != null)
                {
                    foreach (DataRow row in schemaTable.Rows)
                    {
                        var columnName = row["COLUMN_NAME"]?.ToString();
                        var dataType = row["DATA_TYPE"]?.ToString();
                        var isNullable = row["IS_NULLABLE"]?.ToString() == "YES";
                        var isAutoIncrement = row["COLUMN_FLAGS"]?.ToString()?.Contains("16") == true;
                        var isPrimaryKey = primaryKeys.Contains(columnName ?? "");
                        
                        if (!string.IsNullOrEmpty(columnName))
                        {
                            columns.Add(new ColumnInfo
                            {
                                Name = columnName,
                                DataType = dataType ?? "VARCHAR",
                                IsNullable = isNullable,
                                IsAutoIncrement = isAutoIncrement,
                                IsPrimaryKey = isPrimaryKey
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении структуры таблицы {tableName}");
                throw;
            }

            return columns;
        }

        /// <summary>
        /// Добавить запись в таблицу
        /// </summary>
        public async Task<int> InsertRecordAsync(string tableName, Dictionary<string, object> values)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var columns = await GetTableStructureAsync(tableName);
                var insertColumns = new List<string>();
                var insertValues = new List<string>();
                var parameters = new List<OleDbParameter>();

                foreach (var column in columns)
                {
                    // Пропускаем автоинкрементные поля и первичные ключи
                    if (column.IsAutoIncrement || column.IsPrimaryKey) continue;
                    
                    if (values.ContainsKey(column.Name))
                    {
                        var value = values[column.Name];
                        
                        // Пропускаем пустые значения для необязательных полей
                        if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
                        {
                            if (column.IsNullable)
                                continue; // Пропускаем пустые значения для nullable полей
                        }
                        
                        // Проверяем внешние ключи (но не для последовательных ID полей)
                        if (IsForeignKeyColumn(column.Name) && !IsSequentialIdField(column.Name))
                        {
                            var isValid = await CheckForeignKeyConstraintAsync(tableName, column.Name, value!);
                            if (!isValid)
                            {
                                var parentTable = GetParentTableName(column.Name);
                                throw new InvalidOperationException($"Для обеспечения целостности данных необходимо наличие связанной записи в таблице '{parentTable}'");
                            }
                        }
                        
                        insertColumns.Add($"[{column.Name}]");
                        insertValues.Add("?");
                        
                        var param = new OleDbParameter($"@{column.Name}", GetOleDbTypeFromValue(value!));
                        
                        // Преобразуем булевые строки в правильные значения
                        if (value is string strValue && IsBooleanString(strValue))
                        {
                            param.Value = ConvertBooleanString(strValue);
                        }
                        else
                        {
                            param.Value = value ?? DBNull.Value;
                        }
                        
                        parameters.Add(param);
                    }
                }

                // Проверяем, нет ли уже записи с такими же значениями
                if (insertColumns.Count > 0)
                {
                    var checkQuery = $"SELECT COUNT(*) FROM [{tableName}] WHERE ";
                    var checkConditions = new List<string>();
                    var checkParams = new List<OleDbParameter>();
                    
                    for (int i = 0; i < insertColumns.Count; i++)
                    {
                        var columnName = insertColumns[i].Trim('[', ']');
                        checkConditions.Add($"[{columnName}] = ?");
                        
                        // Создаем новые параметры для проверки
                        var originalParam = parameters[i];
                        var checkParam = new OleDbParameter($"@check_{columnName}", originalParam.OleDbType);
                        checkParam.Value = originalParam.Value;
                        checkParams.Add(checkParam);
                    }
                    
                    checkQuery += string.Join(" AND ", checkConditions);
                    
                    using var checkCommand = new OleDbCommand(checkQuery, connection);
                    checkCommand.Parameters.AddRange(checkParams.ToArray());
                    
                    var existingCount = (int)(await checkCommand.ExecuteScalarAsync() ?? 0);
                    if (existingCount > 0)
                    {
                        _logger.LogWarning("Запись с такими же значениями уже существует в таблице {TableName}", tableName);
                        throw new InvalidOperationException("Запись с такими же значениями уже существует");
                    }
                }

                var query = $"INSERT INTO [{tableName}] ({string.Join(", ", insertColumns)}) VALUES ({string.Join(", ", insertValues)})";
                
                _logger.LogInformation("Выполняем INSERT запрос: {Query}", query);
                _logger.LogInformation("Параметры: {Parameters}", string.Join(", ", parameters.Select(p => $"{p.ParameterName}={p.Value}")));
                
                using var command = new OleDbCommand(query, connection);
                command.Parameters.AddRange(parameters.ToArray());

                await command.ExecuteNonQueryAsync();

                // Получаем ID новой записи
                var getIdQuery = "SELECT @@IDENTITY";
                using var getIdCommand = new OleDbCommand(getIdQuery, connection);
                var newId = await getIdCommand.ExecuteScalarAsync();
                return Convert.ToInt32(newId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при добавлении записи в таблицу {tableName}");
                throw;
            }
        }

        /// <summary>
        /// Обновить запись в таблице
        /// </summary>
        public async Task<bool> UpdateRecordAsync(string tableName, Dictionary<string, object> values, string primaryKeyColumn, object primaryKeyValue)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var columns = await GetTableStructureAsync(tableName);
                var updateColumns = new List<string>();
                var parameters = new List<OleDbParameter>();

                foreach (var column in columns)
                {
                    // Пропускаем автоинкрементные поля и первичный ключ
                    if (column.IsAutoIncrement || column.Name == primaryKeyColumn) continue;
                    
                    if (values.ContainsKey(column.Name))
                    {
                        updateColumns.Add($"[{column.Name}] = ?");
                        
                        var value = values[column.Name];
                        var param = new OleDbParameter($"@{column.Name}", GetOleDbTypeFromValue(value!));
                        
                        // Преобразуем булевые строки в правильные значения
                        if (value is string strValue && IsBooleanString(strValue))
                        {
                            param.Value = ConvertBooleanString(strValue);
                        }
                        else
                        {
                            param.Value = value ?? DBNull.Value;
                        }
                        
                        parameters.Add(param);
                    }
                }

                var query = $"UPDATE [{tableName}] SET {string.Join(", ", updateColumns)} WHERE [{primaryKeyColumn}] = ?";
                
                using var command = new OleDbCommand(query, connection);
                command.Parameters.AddRange(parameters.ToArray());
                
                var pkParam = new OleDbParameter($"@{primaryKeyColumn}", GetOleDbTypeFromValue(primaryKeyValue));
                pkParam.Value = primaryKeyValue;
                command.Parameters.Add(pkParam);

                var rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при обновлении записи в таблице {tableName}");
                throw;
            }
        }

        /// <summary>
        /// Получить запись по ID
        /// </summary>
        public async Task<Dictionary<string, object>?> GetRecordByIdAsync(string tableName, string primaryKeyColumn, object primaryKeyValue)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var query = $"SELECT * FROM [{tableName}] WHERE [{primaryKeyColumn}] = ?";
                using var command = new OleDbCommand(query, connection);
                
                var pkParam = new OleDbParameter($"@{primaryKeyColumn}", GetOleDbTypeFromValue(primaryKeyValue));
                pkParam.Value = primaryKeyValue;
                command.Parameters.Add(pkParam);

                using var reader = await command.ExecuteReaderAsync();
                
                if (await reader.ReadAsync())
                {
                    var record = new Dictionary<string, object>();
                    
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        var value = reader.GetValue(i);
                        record[columnName] = value == DBNull.Value ? null! : value;
                    }
                    
                    return record;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении записи из таблицы {tableName}");
                throw;
            }
        }

        /// <summary>
        /// Удалить запись из таблицы
        /// </summary>
        public async Task<bool> DeleteRecordAsync(string tableName, string primaryKeyColumn, object primaryKeyValue)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var columns = await GetTableStructureAsync(tableName);
                var pkColumn = columns.FirstOrDefault(c => c.Name == primaryKeyColumn);
                
                if (pkColumn == null)
                {
                    throw new ArgumentException($"Колонка {primaryKeyColumn} не найдена в таблице {tableName}");
                }

                var query = $"DELETE FROM [{tableName}] WHERE [{primaryKeyColumn}] = ?";
                
                using var command = new OleDbCommand(query, connection);
                var param = new OleDbParameter($"@{primaryKeyColumn}", GetOleDbTypeFromValue(primaryKeyValue));
                param.Value = primaryKeyValue;
                command.Parameters.Add(param);

                var rowsAffected = await command.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при удалении записи из таблицы {tableName}");
                throw;
            }
        }

        /// <summary>
        /// Получить первичный ключ таблицы
        /// </summary>
        public async Task<string?> GetPrimaryKeyColumnAsync(string tableName)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Primary_Keys, 
                    new object?[] { null, null, tableName });
                
                if (schemaTable != null && schemaTable.Rows.Count > 0)
                {
                    return schemaTable.Rows[0]["COLUMN_NAME"]?.ToString();
                }

                // Если первичный ключ не найден, ищем автоинкрементное поле
                var columns = await GetTableStructureAsync(tableName);
                var autoIncrementColumn = columns.FirstOrDefault(c => c.IsAutoIncrement);
                return autoIncrementColumn?.Name;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении первичного ключа таблицы {tableName}");
                return null;
            }
        }

        /// <summary>
        /// Получить строку подключения к базе данных
        /// </summary>
        public string GetConnectionString()
        {
            return _databaseService.GetConnectionString();
        }

        /// <summary>
        /// Получить следующее последовательное целочисленное значение (MAX+1) для указанной колонки таблицы
        /// </summary>
        public async Task<int?> GetNextSequentialIntAsync(string tableName, string columnName)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                // Получаем максимальное значение
                var maxQuery = $"SELECT MAX([{columnName}]) FROM [{tableName}]";
                using var maxCommand = new OleDbCommand(maxQuery, connection);
                var maxResult = await maxCommand.ExecuteScalarAsync();

                _logger.LogInformation($"MAX запрос для {tableName}.{columnName}: {maxQuery}");
                _logger.LogInformation($"MAX результат: {maxResult}");

                int nextValue = 1;
                if (maxResult != DBNull.Value && maxResult != null)
                {
                    if (int.TryParse(maxResult.ToString(), out var maxVal))
                    {
                        nextValue = maxVal + 1;
                        _logger.LogInformation($"Найден MAX: {maxVal}, следующее значение: {nextValue}");
                    }
                    else
                    {
                        _logger.LogWarning($"Не удалось распарсить MAX результат: {maxResult}");
                    }
                }
                else
                {
                    _logger.LogInformation("MAX результат NULL или DBNull, используем значение по умолчанию: 1");
                }

                // Дополнительная проверка: убеждаемся, что это значение не существует
                var checkQuery = $"SELECT COUNT(*) FROM [{tableName}] WHERE [{columnName}] = ?";
                using var checkCommand = new OleDbCommand(checkQuery, connection);
                checkCommand.Parameters.Add($"@{columnName}", OleDbType.Integer).Value = nextValue;
                
                var exists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                _logger.LogInformation($"Проверяем существование значения {nextValue}: найдено {exists} записей");
                
                while (exists > 0)
                {
                    nextValue++;
                    _logger.LogInformation($"Значение {nextValue-1} уже существует, пробуем {nextValue}");
                    checkCommand.Parameters.Clear();
                    checkCommand.Parameters.Add($"@{columnName}", OleDbType.Integer).Value = nextValue;
                    exists = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());
                    _logger.LogInformation($"Проверяем существование значения {nextValue}: найдено {exists} записей");
                }

                _logger.LogInformation($"Следующее значение для {tableName}.{columnName}: {nextValue}");
                return nextValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении следующего значения для {tableName}.{columnName}");
                return null;
            }
        }

        /// <summary>
        /// Получить максимальное существующее целочисленное значение в указанной таблице и колонке
        /// </summary>
        public async Task<int?> GetMaxExistingIntAsync(string tableName, string columnName)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var maxQuery = $"SELECT MAX([{columnName}]) FROM [{tableName}]";
                using var maxCommand = new OleDbCommand(maxQuery, connection);
                var maxResult = await maxCommand.ExecuteScalarAsync();

                if (maxResult == DBNull.Value || maxResult == null)
                {
                    return null;
                }

                if (int.TryParse(maxResult.ToString(), out var maxVal))
                {
                    return maxVal;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении MAX для {tableName}.{columnName}");
                return null;
            }
        }

        /// <summary>
        /// Преобразовать тип данных Access в OleDbType
        /// </summary>
        private OleDbType GetOleDbType(string dataType)
        {
            if (string.IsNullOrEmpty(dataType))
                return OleDbType.VarChar;

            var upperType = dataType.ToUpper();
            
            return upperType switch
            {
                "INTEGER" or "COUNTER" or "AUTOINCREMENT" => OleDbType.Integer,
                "VARCHAR" or "TEXT" or "CHAR" => OleDbType.VarChar,
                "MEMO" or "LONGTEXT" or "LONGCHAR" => OleDbType.LongVarChar,
                "CURRENCY" or "MONEY" => OleDbType.Currency,
                "DATETIME" or "DATE" or "TIME" or "TIMESTAMP" => OleDbType.Date,
                "BOOLEAN" or "YESNO" or "BIT" or "LOGICAL" => OleDbType.Boolean,
                "DOUBLE" or "FLOAT" or "SINGLE" or "REAL" => OleDbType.Double,
                "DECIMAL" or "NUMERIC" => OleDbType.Decimal,
                "SMALLINT" or "TINYINT" => OleDbType.SmallInt,
                "BIGINT" => OleDbType.BigInt,
                _ => OleDbType.VarChar
            };
        }

        /// <summary>
        /// Получить правильный тип данных для параметра на основе значения
        /// </summary>
        private OleDbType GetOleDbTypeFromValue(object value)
        {
            return value switch
            {
                int or long or short => OleDbType.Integer,
                string str when str.Length > 255 => OleDbType.LongVarChar,
                string str when IsBooleanString(str) => OleDbType.Boolean, // Булевые строки
                string => OleDbType.VarChar,
                decimal or double or float => OleDbType.Currency,
                DateTime => OleDbType.Date,
                bool => OleDbType.Boolean,
                null => OleDbType.VarChar,
                _ => OleDbType.VarChar
            };
        }

        /// <summary>
        /// Проверить, является ли строка булевым значением
        /// </summary>
        private bool IsBooleanString(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            
            var lowerValue = value.ToLower().Trim();
            return lowerValue is "true" or "false" or "да" or "нет" or "yes" or "no" or "1" or "0" or "on" or "off";
        }

        /// <summary>
        /// Преобразовать булевую строку в булево значение
        /// </summary>
        private bool ConvertBooleanString(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            
            var lowerValue = value.ToLower().Trim();
            return lowerValue switch
            {
                "true" or "да" or "yes" or "1" or "on" => true,
                "false" or "нет" or "no" or "0" or "off" => false,
                _ => false
            };
        }

        /// <summary>
        /// Проверить существование связанной записи в родительской таблице
        /// </summary>
        public async Task<bool> CheckForeignKeyConstraintAsync(string tableName, string foreignKeyColumn, object foreignKeyValue)
        {
            try
            {
                // Находим родительскую таблицу динамически
                var parentTableName = await ResolveParentTableNameAsync(foreignKeyColumn);
                if (string.IsNullOrEmpty(parentTableName))
                {
                    _logger.LogWarning("Не удалось определить родительскую таблицу для внешнего ключа {ForeignKey}", foreignKeyColumn);
                    return true; // Если не можем определить таблицу, считаем что ограничение выполнено
                }

                // Проверяем, существует ли родительская таблица
                var allTables = await _databaseService.GetAllTablesAsync();
                if (!allTables.Contains(parentTableName, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"Родительская таблица '{parentTableName}' не существует для внешнего ключа '{foreignKeyColumn}'");
                    return true; // Если таблицы нет, считаем что ограничение выполнено
                }

                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                // Получаем первичный ключ родительской таблицы
                var parentPkColumn = await GetPrimaryKeyColumnAsync(parentTableName) ?? "Id";
                
                var query = $"SELECT COUNT(*) FROM [{parentTableName}] WHERE [{parentPkColumn}] = ?";
                using var command = new OleDbCommand(query, connection);
                
                var paramType = GetOleDbTypeFromValue(foreignKeyValue);
                command.Parameters.Add($"@{parentPkColumn}", paramType).Value = foreignKeyValue;
                
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при проверке внешнего ключа {foreignKeyColumn} = {foreignKeyValue}");
                return false;
            }
        }

        /// <summary>
        /// Получить список доступных значений для внешнего ключа
        /// </summary>
        public async Task<List<KeyValuePair<object, string>>> GetForeignKeyOptionsAsync(string tableName, string foreignKeyColumn)
        {
            var options = new List<KeyValuePair<object, string>>();
            
            try
            {
                // Определяем родительскую таблицу
                var parentTableName = await ResolveParentTableNameAsync(foreignKeyColumn);
                if (string.IsNullOrEmpty(parentTableName))
                {
                    options.Add(new KeyValuePair<object, string>("", "Нет доступных значений"));
                    return options;
                }

                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var parentPkColumn = await GetPrimaryKeyColumnAsync(parentTableName) ?? "Id";
                
                // Пытаемся найти колонку с названием для отображения
                var displayColumn = await GetDisplayColumnAsync(parentTableName);
                
                var query = $"SELECT [{parentPkColumn}], [{displayColumn}] FROM [{parentTableName}] ORDER BY [{displayColumn}]";
                using var command = new OleDbCommand(query, connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var key = reader[0];
                    var display = reader[1]?.ToString() ?? $"ID: {key}";
                    options.Add(new KeyValuePair<object, string>(key, display));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении опций для внешнего ключа {foreignKeyColumn}");
            }

            return options;
        }

        /// <summary>
        /// Попытаться определить родительскую таблицу по названию внешнего ключа и наличию таблиц в базе
        /// </summary>
        private async Task<string?> ResolveParentTableNameAsync(string foreignKeyColumn)
        {
            var lower = foreignKeyColumn.ToLower().Replace(" ", "").Replace("_", "");

            // 1) Приоритетные кандидаты по ключу
            var candidatesByKey = new List<string>();
            if (lower.Contains("кодустановки")) candidatesByKey.AddRange(new[] { "Установки" });
            if (lower.Contains("кодпрограммы") || lower.Contains("programid") || lower.Contains("productid") || lower.Contains("softwareid"))
                candidatesByKey.AddRange(new[] { "Программы", "SoftwareProducts", "Products" });
            if (lower.Contains("кодкатегории") || lower.Contains("categoryid")) candidatesByKey.AddRange(new[] { "Категории", "Categories" });
            if (lower.Contains("кодпользователя") || lower.Contains("userid")) candidatesByKey.AddRange(new[] { "Пользователи", "Users" });
            if (lower.Contains("кодудаления")) candidatesByKey.AddRange(new[] { "Удаления", "Deletions" });
            if (lower.Contains("кодобновления")) candidatesByKey.AddRange(new[] { "Обновления", "Updates" });
            if (lower.Contains("кодлицензии")) candidatesByKey.AddRange(new[] { "Лицензии", "Licenses" });
            if (lower.Contains("кодзаписи")) candidatesByKey.AddRange(new[] { "Записи", "Records" });

            var allTables = await _databaseService.GetAllTablesAsync();

            // 2) Прямые совпадения
            foreach (var preferred in candidatesByKey)
            {
                var found = allTables.FirstOrDefault(t => string.Equals(t, preferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(found)) return found;
            }

            // 3) По подстроке-ключевому слову
            string[] keywordOrder =
            {
                "установ", "програм", "software", "product", "категор", "category", "пользоват", "user",
                "удален", "deletion", "обновлен", "update", "лиценз", "license", "запис", "record"
            };

            foreach (var kw in keywordOrder)
            {
                var found = allTables.FirstOrDefault(t => t.Contains(kw, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(found)) return found;
            }

            return null;
        }

        /// <summary>
        /// Получить колонку для отображения в списке опций
        /// </summary>
        private async Task<string> GetDisplayColumnAsync(string tableName)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var columns = await GetTableStructureAsync(tableName);
                
                // Ищем подходящую колонку для отображения
                var displayColumn = columns.FirstOrDefault(c => 
                    c.Name.ToLower().Contains("название") || 
                    c.Name.ToLower().Contains("имя") || 
                    c.Name.ToLower().Contains("name") ||
                    c.Name.ToLower().Contains("наименование"))?.Name;
                
                if (string.IsNullOrEmpty(displayColumn))
                {
                    displayColumn = columns.FirstOrDefault(c => 
                        c.DataType.ToUpper().Contains("VARCHAR") || 
                        c.DataType.ToUpper().Contains("TEXT"))?.Name;
                }
                
                return displayColumn ?? "Id";
            }
            catch
            {
                return "Id";
            }
        }


        /// <summary>
        /// Проверить, является ли колонка внешним ключом
        /// </summary>
        private bool IsForeignKeyColumn(string columnName)
        {
            var lowerName = columnName.ToLower();
            lowerName = lowerName.Replace(" ", "").Replace("_", "");
            return lowerName.Contains("programid") ||
                   lowerName.Contains("productid") ||
                   lowerName.Contains("softwareid") ||
                   (lowerName.EndsWith("id") && !IsSequentialIdField(columnName));
        }

        /// <summary>
        /// Получить название родительской таблицы для внешнего ключа (быстрый эвристический вариант)
        /// </summary>
        private string GetParentTableName(string columnName)
        {
            var lowerName = columnName.ToLower().Replace(" ", "").Replace("_", "");
            return lowerName switch
            {
                var name when name.Contains("кодустановки") || name.Contains("код_установки") => "Установки",
                var name when name.Contains("кодпрограммы") || name.Contains("код_программы") || name.Contains("programid") || name.Contains("productid") || name.Contains("softwareid") => "Программы",
                var name when name.Contains("кодкатегории") || name.Contains("код_категории") => "Категории",
                var name when name.Contains("кодудаления") || name.Contains("код_удаления") => "Удаления",
                var name when name.Contains("кодпользователя") || name.Contains("код_пользователя") => "Пользователи",
                var name when name.Contains("кодобновления") || name.Contains("код_обновления") => "Обновления",
                var name when name.Contains("кодлицензии") || name.Contains("код_лицензии") => "Лицензии",
                var name when name.Contains("кодзаписи") || name.Contains("код_записи") => "Записи",
                _ => "Установки"
            };
        }

        /// <summary>
        /// Проверить, является ли поле последовательным ID (не требует внешнего ключа)
        /// </summary>
        private bool IsSequentialIdField(string columnName)
        {
            var lowerName = columnName.ToLower().Replace(" ", "").Replace("_", "");
            return lowerName.StartsWith("код") || 
                   lowerName.Contains("код") ||
                   lowerName.EndsWith("код") ||
                   lowerName.Equals("кодустановки") ||
                   lowerName.Equals("код_установки") ||
                   lowerName.Equals("кодудаления") ||
                   lowerName.Equals("код_удаления") ||
                   lowerName.Equals("кодпрограммы") ||
                   lowerName.Equals("код_программы") ||
                   lowerName.Equals("кодпользователя") ||
                   lowerName.Equals("код_пользователя") ||
                   lowerName.Equals("кодобновления") ||
                   lowerName.Equals("код_обновления") ||
                   lowerName.Equals("кодлицензии") ||
                   lowerName.Equals("код_лицензии") ||
                   lowerName.Equals("кодзаписи") ||
                   lowerName.Equals("код_записи");
        }
    }

    /// <summary>
    /// Информация о колонке таблицы
    /// </summary>
    public class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public bool IsNullable { get; set; }
        public bool IsAutoIncrement { get; set; }
        public bool IsPrimaryKey { get; set; }
    }
}
