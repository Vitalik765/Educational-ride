#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Models;

namespace SoftwareProductsManager.Services
{
    /// <summary>
    /// Сервис для работы с базой данных Access
    /// </summary>
    public class DatabaseService
    {
        private readonly string _connectionString;
        private readonly ILogger<DatabaseService> _logger;

        public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection") 
                ?? throw new ArgumentNullException(nameof(configuration));
            
            // Если путь относительный, попробуем найти файл базы данных
            if (connectionString.Contains("Data Source="))
            {
                var dataSourceIndex = connectionString.IndexOf("Data Source=") + 12;
                var endIndex = connectionString.IndexOf(";", dataSourceIndex);
                if (endIndex == -1) endIndex = connectionString.Length;
                
                var dataSource = connectionString.Substring(dataSourceIndex, endIndex - dataSourceIndex);
                
                // Если это относительный путь, попробуем найти файл
                if (!System.IO.Path.IsPathRooted(dataSource))
                {
                    var currentDir = Directory.GetCurrentDirectory();
                    var possiblePaths = new[]
                    {
                        Path.Combine(currentDir, dataSource),
                        Path.Combine(currentDir, "..", "..", "..", dataSource),
                        Path.Combine(currentDir, "..", "..", "..", "..", dataSource),
                        Path.Combine(currentDir, "..", "..", "..", "..", "..", dataSource),
                        Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", dataSource),
                        Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "..", "..", "..", dataSource),
                        Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "..", "..", "..", "..", dataSource)
                    };
                    
                    logger.LogInformation($"Поиск файла базы данных: {dataSource}");
                    logger.LogInformation($"Текущая директория: {currentDir}");
                    
                    foreach (var path in possiblePaths)
                    {
                        var fullPath = Path.GetFullPath(path);
                        logger.LogInformation($"Проверяем путь: {fullPath}");
                        
                        if (File.Exists(fullPath))
                        {
                            dataSource = fullPath;
                            logger.LogInformation($"Файл базы данных найден: {fullPath}");
                            break;
                        }
                    }
                    
                    if (!File.Exists(dataSource))
                    {
                        logger.LogError($"Файл базы данных не найден: {dataSource}");
                        throw new FileNotFoundException($"Файл базы данных не найден: {dataSource}");
                    }
                }
                
                _connectionString = connectionString.Substring(0, dataSourceIndex) + dataSource + 
                                  connectionString.Substring(endIndex);
            }
            else
            {
                _connectionString = connectionString;
            }
            
            _logger = logger;
        }

        /// <summary>
        /// Проверка подключения к базе данных
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();
                _logger.LogInformation("Подключение к базе данных успешно установлено");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка подключения к базе данных");
                return false;
            }
        }

        /// <summary>
        /// Создание таблицы программных продуктов, если она не существует
        /// ОТКЛЮЧЕНО: Таблицы Categories и SoftwareProducts больше не создаются автоматически
        /// </summary>
        public async Task CreateTablesIfNotExistAsync()
        {
            try
            {
                _logger.LogInformation("Создание таблиц Categories и SoftwareProducts отключено");
                
                // КОД ОТКЛЮЧЕН - таблицы Categories и SoftwareProducts больше не создаются
                /*
                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем существование таблицы Categories
                if (!await TableExistsAsync(connection, "Categories"))
                {
                    var createCategoriesTable = @"
                        CREATE TABLE Categories (
                            Id AUTOINCREMENT PRIMARY KEY,
                            Name TEXT(100) NOT NULL,
                            Description MEMO,
                            IsActive YESNO DEFAULT True
                        )";

                    using var command = new OleDbCommand(createCategoriesTable, connection);
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation("Таблица Categories создана");
                }

                // Проверяем существование таблицы SoftwareProducts
                if (!await TableExistsAsync(connection, "SoftwareProducts"))
                {
                    var createProductsTable = @"
                        CREATE TABLE SoftwareProducts (
                            Id AUTOINCREMENT PRIMARY KEY,
                            Name TEXT(200) NOT NULL,
                            Description MEMO,
                            Version TEXT(50),
                            Developer TEXT(100),
                            Category TEXT(100),
                            Price CURRENCY,
                            ReleaseDate DATETIME,
                            License TEXT(100),
                            SystemRequirements MEMO,
                            IsActive YESNO DEFAULT True,
                            CreatedDate DATETIME DEFAULT Now(),
                            UpdatedDate DATETIME
                        )";

                    using var command = new OleDbCommand(createProductsTable, connection);
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation("Таблица SoftwareProducts создана");
                }

                _logger.LogInformation("Таблицы базы данных успешно созданы или уже существуют");
                */
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании таблиц базы данных");
                throw;
            }
        }

        /// <summary>
        /// Получить строку подключения к базе данных
        /// </summary>
        public string GetConnectionString()
        {
            return _connectionString;
        }

        /// <summary>
        /// Проверить данные в таблицах с русскими названиями
        /// </summary>
        public async Task CheckDataInRussianTablesAsync()
        {
            var russianTables = new[] { "Программирование", "Программы", "Приложения", "Программное", "Программ" };
            
            foreach (var tableName in russianTables)
            {
                try
                {
                    using var connection = new OleDbConnection(_connectionString);
                    await connection.OpenAsync();
                    
                    var countQuery = $"SELECT COUNT(*) FROM [{tableName}]";
                    using var countCommand = new OleDbCommand(countQuery, connection);
                    var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
                    
                    _logger.LogInformation($"Таблица '{tableName}': {count} записей");
                    
                    if (count > 0)
                    {
                        // Показываем первые несколько записей
                        var sampleQuery = $"SELECT TOP 3 * FROM [{tableName}]";
                        using var sampleCommand = new OleDbCommand(sampleQuery, connection);
                        using var reader = await sampleCommand.ExecuteReaderAsync();
                        
                        _logger.LogInformation($"Примеры данных из таблицы '{tableName}':");
                        while (await reader.ReadAsync())
                        {
                            var rowData = new List<string>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var fieldName = reader.GetName(i);
                                var fieldValue = reader[i]?.ToString() ?? "NULL";
                                rowData.Add($"{fieldName}: {fieldValue}");
                            }
                            _logger.LogInformation($"  {string.Join(", ", rowData)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при проверке таблицы '{tableName}'");
                }
            }
        }

        /// <summary>
        /// Получить список всех таблиц в базе данных
        /// </summary>
        public async Task<List<string>> GetAllTablesAsync()
        {
            var tables = new List<string>();
            
            try
            {
                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();

                // Получаем список таблиц через GetOleDbSchemaTable
                var schemaTable = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null);
                
                if (schemaTable != null)
                {
                    foreach (DataRow row in schemaTable.Rows)
                    {
                        var tableName = row["TABLE_NAME"]?.ToString();
                        var tableType = row["TABLE_TYPE"]?.ToString();
                        
                        // Фильтруем только пользовательские таблицы
                        if (!string.IsNullOrEmpty(tableName) && 
                            tableType == "TABLE" && 
                            !tableName.StartsWith("MSys"))
                        {
                            tables.Add(tableName);
                        }
                    }
                }

                _logger.LogInformation($"Найдено таблиц в базе данных: {tables.Count}");
                foreach (var table in tables)
                {
                    _logger.LogInformation($"Таблица: {table}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка таблиц");
                
                // Если не удалось получить список таблиц, попробуем проверить конкретные таблицы
                var knownTables = new[] { "Products", "Items" }; // Убраны Categories и SoftwareProducts
                foreach (var tableName in knownTables)
                {
                    try
                    {
                        using var connection = new OleDbConnection(_connectionString);
                        await connection.OpenAsync();
                        using var command = new OleDbCommand($"SELECT TOP 1 * FROM [{tableName}]", connection);
                        await command.ExecuteScalarAsync();
                        tables.Add(tableName);
                        _logger.LogInformation($"Найдена таблица: {tableName}");
                    }
                    catch
                    {
                        // Таблица не существует, пропускаем
                    }
                }
            }

            return tables;
        }

        /// <summary>
        /// Добавить тестовые данные, если таблицы пусты
        /// ОТКЛЮЧЕНО: Тестовые данные для SoftwareProducts больше не добавляются
        /// </summary>
        public async Task AddTestDataIfEmptyAsync()
        {
            try
            {
                _logger.LogInformation("Добавление тестовых данных для SoftwareProducts отключено");
                
                // КОД ОТКЛЮЧЕН - тестовые данные больше не добавляются
                /*
                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем, есть ли данные в таблице SoftwareProducts
                var checkQuery = "SELECT COUNT(*) FROM SoftwareProducts";
                using var checkCommand = new OleDbCommand(checkQuery, connection);
                var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                if (count == 0)
                {
                    _logger.LogInformation("Добавляем тестовые данные...");

                    var insertQuery = @"
                        INSERT INTO SoftwareProducts (Name, Description, Version, Developer, Category, 
                                                    Price, ReleaseDate, License, SystemRequirements, IsActive, CreatedDate)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

                    var testProducts = new[]
                    {
                        new { Name = "Microsoft Office 365", Description = "Полный пакет офисных приложений", Version = "2024", Developer = "Microsoft", Category = "Офисные программы", Price = 99.99m, ReleaseDate = DateTime.Parse("2024-01-15"), License = "Подписка", SystemRequirements = "Windows 10/11, 4GB RAM" },
                        new { Name = "Adobe Photoshop", Description = "Профессиональный редактор изображений", Version = "2024", Developer = "Adobe", Category = "Графика", Price = 239.99m, ReleaseDate = DateTime.Parse("2024-02-01"), License = "Подписка", SystemRequirements = "Windows 10/11, 8GB RAM, GPU" },
                        new { Name = "Visual Studio Code", Description = "Бесплатный редактор кода", Version = "1.85", Developer = "Microsoft", Category = "Разработка", Price = 0m, ReleaseDate = DateTime.Parse("2024-01-10"), License = "MIT", SystemRequirements = "Windows 7+, 1GB RAM" },
                        new { Name = "Google Chrome", Description = "Веб-браузер", Version = "120", Developer = "Google", Category = "Браузеры", Price = 0m, ReleaseDate = DateTime.Parse("2024-01-05"), License = "Бесплатно", SystemRequirements = "Windows 10/11, 2GB RAM" },
                        new { Name = "WinRAR", Description = "Архиватор файлов", Version = "6.24", Developer = "RARLAB", Category = "Утилиты", Price = 29.99m, ReleaseDate = DateTime.Parse("2024-01-20"), License = "Коммерческая", SystemRequirements = "Windows 7+, 100MB" }
                    };

                    foreach (var product in testProducts)
                    {
                        using var command = new OleDbCommand(insertQuery, connection);
                        command.Parameters.Add("@Name", OleDbType.VarChar).Value = product.Name;
                        command.Parameters.Add("@Description", OleDbType.LongVarChar).Value = product.Description;
                        command.Parameters.Add("@Version", OleDbType.VarChar).Value = product.Version;
                        command.Parameters.Add("@Developer", OleDbType.VarChar).Value = product.Developer;
                        command.Parameters.Add("@Category", OleDbType.VarChar).Value = product.Category;
                        command.Parameters.Add("@Price", OleDbType.Currency).Value = product.Price;
                        command.Parameters.Add("@ReleaseDate", OleDbType.Date).Value = product.ReleaseDate;
                        command.Parameters.Add("@License", OleDbType.VarChar).Value = product.License;
                        command.Parameters.Add("@SystemRequirements", OleDbType.LongVarChar).Value = product.SystemRequirements;
                        command.Parameters.Add("@IsActive", OleDbType.Boolean).Value = true;
                        command.Parameters.Add("@CreatedDate", OleDbType.Date).Value = DateTime.Now;

                        await command.ExecuteNonQueryAsync();
                    }

                    _logger.LogInformation("Тестовые данные успешно добавлены");
                }
                else
                {
                    _logger.LogInformation("В базе данных уже есть данные, тестовые данные не добавляются");
                }
                */
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении тестовых данных");
                throw;
            }
        }

        /// <summary>
        /// Проверка существования таблицы
        /// </summary>
        private async Task<bool> TableExistsAsync(OleDbConnection connection, string tableName)
        {
            try
            {
                var command = new OleDbCommand($"SELECT COUNT(*) FROM MSysObjects WHERE Name='{tableName}' AND Type=1", connection);
                var result = await command.ExecuteScalarAsync();
                return Convert.ToInt32(result) > 0;
            }
            catch
            {
                // Если не удалось проверить через MSysObjects, попробуем другой способ
                try
                {
                    var command = new OleDbCommand($"SELECT TOP 1 * FROM [{tableName}]", connection);
                    await command.ExecuteScalarAsync();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
