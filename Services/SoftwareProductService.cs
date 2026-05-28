#nullable enable
using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Models;

namespace SoftwareProductsManager.Services
{
    /// <summary>
    /// Сервис для работы с программными продуктами
    /// </summary>
    public class SoftwareProductService
    {
        private readonly DatabaseService _databaseService;
        private readonly ILogger<SoftwareProductService> _logger;

        public SoftwareProductService(DatabaseService databaseService, ILogger<SoftwareProductService> logger)
        {
            _databaseService = databaseService;
            _logger = logger;
        }

        /// <summary>
        /// Получить все активные программные продукты
        /// </summary>
        public async Task<List<SoftwareProduct>> GetAllProductsAsync()
        {
            var products = new List<SoftwareProduct>();

            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, Version, Developer, Category, 
                           Price, ReleaseDate, License, SystemRequirements, 
                           IsActive, CreatedDate, UpdatedDate
                    FROM SoftwareProducts 
                    WHERE IsActive = ?
                    ORDER BY Name";

                using var command = new OleDbCommand(query, connection);
                command.Parameters.Add("@IsActive", OleDbType.Boolean).Value = true;
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new SoftwareProduct
                    {
                        Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                        Name = reader["Name"]?.ToString() ?? string.Empty,
                        Description = reader["Description"]?.ToString() ?? string.Empty,
                        Version = reader["Version"]?.ToString() ?? string.Empty,
                        Developer = reader["Developer"]?.ToString() ?? string.Empty,
                        Category = reader["Category"]?.ToString() ?? string.Empty,
                        Price = reader["Price"] != DBNull.Value ? Convert.ToDecimal(reader["Price"]) : 0,
                        ReleaseDate = reader["ReleaseDate"] != DBNull.Value ? Convert.ToDateTime(reader["ReleaseDate"]) : DateTime.Now,
                        License = reader["License"]?.ToString() ?? string.Empty,
                        SystemRequirements = reader["SystemRequirements"]?.ToString() ?? string.Empty,
                        IsActive = reader["IsActive"] != DBNull.Value ? Convert.ToBoolean(reader["IsActive"]) : true,
                        CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : DateTime.Now,
                        UpdatedDate = reader["UpdatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["UpdatedDate"]) : null
                    });
                }

                _logger.LogInformation($"Загружено {products.Count} программных продуктов");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке программных продуктов");
                throw;
            }

            return products;
        }

        /// <summary>
        /// Получить программный продукт по ID
        /// </summary>
        public async Task<SoftwareProduct?> GetProductByIdAsync(int id)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, Version, Developer, Category, 
                           Price, ReleaseDate, License, SystemRequirements, 
                           IsActive, CreatedDate, UpdatedDate
                    FROM SoftwareProducts 
                    WHERE Id = ? AND IsActive = ?";

                using var command = new OleDbCommand(query, connection);
                command.Parameters.Add("@Id", OleDbType.Integer).Value = id;
                command.Parameters.Add("@IsActive", OleDbType.Boolean).Value = true;
                using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return new SoftwareProduct
                    {
                        Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                        Name = reader["Name"]?.ToString() ?? string.Empty,
                        Description = reader["Description"]?.ToString() ?? string.Empty,
                        Version = reader["Version"]?.ToString() ?? string.Empty,
                        Developer = reader["Developer"]?.ToString() ?? string.Empty,
                        Category = reader["Category"]?.ToString() ?? string.Empty,
                        Price = reader["Price"] != DBNull.Value ? Convert.ToDecimal(reader["Price"]) : 0,
                        ReleaseDate = reader["ReleaseDate"] != DBNull.Value ? Convert.ToDateTime(reader["ReleaseDate"]) : DateTime.Now,
                        License = reader["License"]?.ToString() ?? string.Empty,
                        SystemRequirements = reader["SystemRequirements"]?.ToString() ?? string.Empty,
                        IsActive = reader["IsActive"] != DBNull.Value ? Convert.ToBoolean(reader["IsActive"]) : true,
                        CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : DateTime.Now,
                        UpdatedDate = reader["UpdatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["UpdatedDate"]) : null
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении программного продукта с ID {id}");
                throw;
            }
        }

        /// <summary>
        /// Поиск программных продуктов
        /// </summary>
        public async Task<List<SoftwareProduct>> SearchProductsAsync(string searchTerm)
        {
            var products = new List<SoftwareProduct>();

            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var query = @"
                    SELECT Id, Name, Description, Version, Developer, Category, 
                           Price, ReleaseDate, License, SystemRequirements, 
                           IsActive, CreatedDate, UpdatedDate
                    FROM SoftwareProducts 
                    WHERE IsActive = ? 
                    AND (Name LIKE ? OR Description LIKE ? OR Developer LIKE ? OR Category LIKE ?)
                    ORDER BY Name";

                using var command = new OleDbCommand(query, connection);
                command.Parameters.Add("@IsActive", OleDbType.Boolean).Value = true;
                var searchPattern = $"%{searchTerm}%";
                command.Parameters.Add("@Name", OleDbType.VarChar).Value = searchPattern;
                command.Parameters.Add("@Description", OleDbType.VarChar).Value = searchPattern;
                command.Parameters.Add("@Developer", OleDbType.VarChar).Value = searchPattern;
                command.Parameters.Add("@Category", OleDbType.VarChar).Value = searchPattern;

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    products.Add(new SoftwareProduct
                    {
                        Id = reader["Id"] != DBNull.Value ? Convert.ToInt32(reader["Id"]) : 0,
                        Name = reader["Name"]?.ToString() ?? string.Empty,
                        Description = reader["Description"]?.ToString() ?? string.Empty,
                        Version = reader["Version"]?.ToString() ?? string.Empty,
                        Developer = reader["Developer"]?.ToString() ?? string.Empty,
                        Category = reader["Category"]?.ToString() ?? string.Empty,
                        Price = reader["Price"] != DBNull.Value ? Convert.ToDecimal(reader["Price"]) : 0,
                        ReleaseDate = reader["ReleaseDate"] != DBNull.Value ? Convert.ToDateTime(reader["ReleaseDate"]) : DateTime.Now,
                        License = reader["License"]?.ToString() ?? string.Empty,
                        SystemRequirements = reader["SystemRequirements"]?.ToString() ?? string.Empty,
                        IsActive = reader["IsActive"] != DBNull.Value ? Convert.ToBoolean(reader["IsActive"]) : true,
                        CreatedDate = reader["CreatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["CreatedDate"]) : DateTime.Now,
                        UpdatedDate = reader["UpdatedDate"] != DBNull.Value ? Convert.ToDateTime(reader["UpdatedDate"]) : null
                    });
                }

                _logger.LogInformation($"Найдено {products.Count} программных продуктов по запросу '{searchTerm}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при поиске программных продуктов: {searchTerm}");
                throw;
            }

            return products;
        }

        /// <summary>
        /// Удалить программный продукт
        /// </summary>
        public async Task<bool> DeleteProductAsync(int id)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var query = "UPDATE SoftwareProducts SET IsActive = ? WHERE Id = ?";
                using var command = new OleDbCommand(query, connection);
                command.Parameters.Add("@IsActive", OleDbType.Boolean).Value = false;
                command.Parameters.Add("@Id", OleDbType.Integer).Value = id;

                var rowsAffected = await command.ExecuteNonQueryAsync();
                _logger.LogInformation($"Программный продукт с ID {id} помечен как неактивный");

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при удалении программного продукта с ID {id}");
                throw;
            }
        }

        /// <summary>
        /// Добавить новый программный продукт
        /// </summary>
        public async Task<int> AddProductAsync(SoftwareProduct product)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var query = @"
                    INSERT INTO SoftwareProducts (Name, Description, Version, Developer, Category, 
                                                Price, ReleaseDate, License, SystemRequirements, 
                                                IsActive, CreatedDate, UpdatedDate)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

                using var command = new OleDbCommand(query, connection);
                command.Parameters.Add("@Name", OleDbType.VarChar).Value = product.Name;
                command.Parameters.Add("@Description", OleDbType.LongVarChar).Value = product.Description;
                command.Parameters.Add("@Version", OleDbType.VarChar).Value = product.Version;
                command.Parameters.Add("@Developer", OleDbType.VarChar).Value = product.Developer;
                command.Parameters.Add("@Category", OleDbType.VarChar).Value = product.Category;
                command.Parameters.Add("@Price", OleDbType.Currency).Value = product.Price;
                command.Parameters.Add("@ReleaseDate", OleDbType.Date).Value = product.ReleaseDate;
                command.Parameters.Add("@License", OleDbType.VarChar).Value = product.License;
                command.Parameters.Add("@SystemRequirements", OleDbType.LongVarChar).Value = product.SystemRequirements;
                command.Parameters.Add("@IsActive", OleDbType.Boolean).Value = product.IsActive;
                command.Parameters.Add("@CreatedDate", OleDbType.Date).Value = product.CreatedDate;
                command.Parameters.Add("@UpdatedDate", OleDbType.Date).Value = product.UpdatedDate;

                await command.ExecuteNonQueryAsync();

                // Получаем ID нового продукта
                var getIdQuery = "SELECT @@IDENTITY";
                using var getIdCommand = new OleDbCommand(getIdQuery, connection);
                var newId = await getIdCommand.ExecuteScalarAsync();
                var productId = Convert.ToInt32(newId);

                _logger.LogInformation($"Добавлен новый программный продукт с ID {productId}");
                return productId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при добавлении программного продукта");
                throw;
            }
        }

        /// <summary>
        /// Обновить программный продукт
        /// </summary>
        public async Task<bool> UpdateProductAsync(SoftwareProduct product)
        {
            try
            {
                using var connection = new OleDbConnection(_databaseService.GetConnectionString());
                await connection.OpenAsync();

                var query = @"
                    UPDATE SoftwareProducts 
                    SET Name = ?, Description = ?, Version = ?, Developer = ?, Category = ?, 
                        Price = ?, ReleaseDate = ?, License = ?, SystemRequirements = ?, 
                        IsActive = ?, UpdatedDate = ?
                    WHERE Id = ?";

                using var command = new OleDbCommand(query, connection);
                command.Parameters.Add("@Name", OleDbType.VarChar).Value = product.Name;
                command.Parameters.Add("@Description", OleDbType.LongVarChar).Value = product.Description;
                command.Parameters.Add("@Version", OleDbType.VarChar).Value = product.Version;
                command.Parameters.Add("@Developer", OleDbType.VarChar).Value = product.Developer;
                command.Parameters.Add("@Category", OleDbType.VarChar).Value = product.Category;
                command.Parameters.Add("@Price", OleDbType.Currency).Value = product.Price;
                command.Parameters.Add("@ReleaseDate", OleDbType.Date).Value = product.ReleaseDate;
                command.Parameters.Add("@License", OleDbType.VarChar).Value = product.License;
                command.Parameters.Add("@SystemRequirements", OleDbType.LongVarChar).Value = product.SystemRequirements;
                command.Parameters.Add("@IsActive", OleDbType.Boolean).Value = product.IsActive;
                command.Parameters.Add("@UpdatedDate", OleDbType.Date).Value = DateTime.Now;
                command.Parameters.Add("@Id", OleDbType.Integer).Value = product.Id;

                var rowsAffected = await command.ExecuteNonQueryAsync();
                _logger.LogInformation($"Обновлен программный продукт с ID {product.Id}");

                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при обновлении программного продукта с ID {product.Id}");
                throw;
            }
        }
    }
}