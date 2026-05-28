#nullable enable
using System;
using System.Data;
using System.Data.OleDb;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Models;

namespace SoftwareProductsManager.Services
{
    /// <summary>
    /// Сервис для аутентификации и авторизации пользователей
    /// </summary>
    public class AuthenticationService
    {
        private readonly string _connectionString;
        private readonly ILogger<AuthenticationService> _logger;
        private User? _currentUser;

        public AuthenticationService(string connectionString, ILogger<AuthenticationService> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        /// <summary>
        /// Текущий авторизованный пользователь
        /// </summary>
        public User? CurrentUser => _currentUser;

        /// <summary>
        /// Проверка, авторизован ли пользователь
        /// </summary>
        public bool IsAuthenticated => _currentUser != null;

        /// <summary>
        /// Проверка, является ли пользователь администратором
        /// </summary>
        public bool IsAdmin => _currentUser?.Role == "Admin";

        /// <summary>
        /// Создание таблицы пользователей, если она не существует
        /// </summary>
        public async Task CreateUsersTableIfNotExistsAsync()
        {
            try
            {
                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();

                // Проверяем существование таблицы Users
                if (!await TableExistsAsync(connection, "Users"))
                {
                    var createUsersTable = @"
                        CREATE TABLE Users (
                            Id AUTOINCREMENT PRIMARY KEY,
                            Username TEXT(50) NOT NULL UNIQUE,
                            Email TEXT(100) NOT NULL UNIQUE,
                            PasswordHash TEXT(255) NOT NULL,
                            FirstName TEXT(50),
                            LastName TEXT(50),
                            Role TEXT(20) DEFAULT 'User',
                            IsActive YESNO DEFAULT True,
                            CreatedDate DATETIME DEFAULT Now(),
                            LastLoginDate DATETIME,
                            UpdatedDate DATETIME
                        )";

                    using var command = new OleDbCommand(createUsersTable, connection);
                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation("Таблица Users создана");

                    // Создаем администратора по умолчанию
                    await CreateDefaultAdminAsync(connection);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании таблицы Users");
                throw;
            }
        }

        /// <summary>
        /// Создание администратора по умолчанию
        /// </summary>
        private async Task CreateDefaultAdminAsync(OleDbConnection connection)
        {
            try
            {
                // Проверяем, существует ли уже администратор с именем 'admin'
                var checkQuery = "SELECT COUNT(*) FROM Users WHERE Username = 'admin'";
                using var checkCommand = new OleDbCommand(checkQuery, connection);
                var count = Convert.ToInt32(await checkCommand.ExecuteScalarAsync());

                if (count == 0)
                {
                    var insertQuery = @"
                        INSERT INTO Users (Username, Email, PasswordHash, FirstName, LastName, Role, IsActive, CreatedDate)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?)";

                    using var command = new OleDbCommand(insertQuery, connection);
                    command.Parameters.Add("@Username", OleDbType.VarChar).Value = "admin";
                    command.Parameters.Add("@Email", OleDbType.VarChar).Value = "admin@example.com";
                    command.Parameters.Add("@PasswordHash", OleDbType.VarChar).Value = "admin";
                    command.Parameters.Add("@FirstName", OleDbType.VarChar).Value = "Администратор";
                    command.Parameters.Add("@LastName", OleDbType.VarChar).Value = "Системы";
                    command.Parameters.Add("@Role", OleDbType.VarChar).Value = "Admin";
                    command.Parameters.Add("@IsActive", OleDbType.Boolean).Value = true;
                    command.Parameters.Add("@CreatedDate", OleDbType.Date).Value = DateTime.Now;

                    await command.ExecuteNonQueryAsync();
                    _logger.LogInformation("Создан администратор по умолчанию: admin / admin");
                }
                else
                {
                    _logger.LogInformation("Администратор 'admin' уже существует");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при создании администратора по умолчанию");
            }
        }

        /// <summary>
        /// Регистрация нового пользователя
        /// </summary>
        public async Task<bool> RegisterAsync(string username, string email, string password, 
            string firstName = "", string lastName = "")
        {
            try
            {
                // Проверяем, что пользователь с таким именем или email не существует
                if (await UserExistsAsync(username, email))
                {
                    _logger.LogWarning($"Попытка регистрации существующего пользователя: {username}");
                    return false;
                }

                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();

                var insertQuery = @"
                    INSERT INTO Users (Username, Email, PasswordHash, FirstName, LastName, Role, IsActive, CreatedDate)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?)";

                using var command = new OleDbCommand(insertQuery, connection);
                command.Parameters.Add("@Username", OleDbType.VarChar).Value = username;
                command.Parameters.Add("@Email", OleDbType.VarChar).Value = email;
                command.Parameters.Add("@PasswordHash", OleDbType.VarChar).Value = password; // Храним пароль открыто
                command.Parameters.Add("@FirstName", OleDbType.VarChar).Value = firstName;
                command.Parameters.Add("@LastName", OleDbType.VarChar).Value = lastName;
                command.Parameters.Add("@Role", OleDbType.VarChar).Value = "User";
                command.Parameters.Add("@IsActive", OleDbType.Boolean).Value = true;
                command.Parameters.Add("@CreatedDate", OleDbType.Date).Value = DateTime.Now;

                await command.ExecuteNonQueryAsync();
                _logger.LogInformation($"Пользователь {username} успешно зарегистрирован");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при регистрации пользователя {username}");
                _logger.LogError(ex, $"Тип ошибки: {ex.GetType().Name}, Сообщение: {ex.Message}");
                if (ex.InnerException != null)
                {
                    _logger.LogError(ex.InnerException, $"Внутренняя ошибка: {ex.InnerException.Message}");
                }
                throw; // Пробрасываем исключение дальше для отображения пользователю
            }
        }

        /// <summary>
        /// Авторизация пользователя
        /// </summary>
        public async Task<bool> LoginAsync(string username, string password)
        {
            try
            {
                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();

                var selectQuery = @"
                    SELECT Id, Username, Email, PasswordHash, FirstName, LastName, Role, IsActive, 
                           CreatedDate, LastLoginDate, UpdatedDate
                    FROM Users 
                    WHERE Username = ? AND IsActive = True";

                using var command = new OleDbCommand(selectQuery, connection);
                command.Parameters.Add("@Username", OleDbType.VarChar).Value = username;

                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var storedPassword = reader["PasswordHash"].ToString() ?? "";
                    if (VerifyPassword(password, storedPassword))
                    {
                        _currentUser = new User
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Username = reader["Username"].ToString() ?? "",
                            Email = reader["Email"].ToString() ?? "",
                            PasswordHash = reader["PasswordHash"].ToString() ?? "",
                            FirstName = reader["FirstName"].ToString() ?? "",
                            LastName = reader["LastName"].ToString() ?? "",
                            Role = reader["Role"].ToString() ?? "User",
                            IsActive = Convert.ToBoolean(reader["IsActive"]),
                            CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                            LastLoginDate = reader["LastLoginDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["LastLoginDate"]),
                            UpdatedDate = reader["UpdatedDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["UpdatedDate"])
                        };

                        // Обновляем дату последнего входа
                        await UpdateLastLoginAsync(connection, _currentUser.Id);

                        _logger.LogInformation($"Пользователь {username} успешно авторизован");
                        return true;
                    }
                }

                _logger.LogWarning($"Неудачная попытка авторизации для пользователя: {username}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при авторизации пользователя {username}");
                return false;
            }
        }

        /// <summary>
        /// Выход из системы
        /// </summary>
        public void Logout()
        {
            _currentUser = null;
            _logger.LogInformation("Пользователь вышел из системы");
        }

        /// <summary>
        /// Проверка существования пользователя
        /// </summary>
        private async Task<bool> UserExistsAsync(string username, string email)
        {
            try
            {
                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();

                var checkQuery = "SELECT COUNT(*) FROM Users WHERE Username = ? OR Email = ?";
                using var command = new OleDbCommand(checkQuery, connection);
                command.Parameters.Add("@Username", OleDbType.VarChar).Value = username;
                command.Parameters.Add("@Email", OleDbType.VarChar).Value = email;

                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке существования пользователя");
                return true; // В случае ошибки считаем, что пользователь существует
            }
        }

        /// <summary>
        /// Обновление даты последнего входа
        /// </summary>
        private async Task UpdateLastLoginAsync(OleDbConnection connection, int userId)
        {
            try
            {
                var updateQuery = "UPDATE Users SET LastLoginDate = ? WHERE Id = ?";
                using var command = new OleDbCommand(updateQuery, connection);
                command.Parameters.Add("@LastLoginDate", OleDbType.Date).Value = DateTime.Now;
                command.Parameters.Add("@Id", OleDbType.Integer).Value = userId;

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении даты последнего входа");
            }
        }

        /// <summary>
        /// Хеширование пароля
        /// </summary>
        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "SoftwareProductsManager2024"));
            return Convert.ToBase64String(hashedBytes);
        }

        /// <summary>
        /// Проверка пароля
        /// </summary>
        private bool VerifyPassword(string password, string storedPassword)
        {
            // Простое сравнение паролей без шифрования
            return password == storedPassword;
        }

        /// <summary>
        /// Обеспечение наличия администратора
        /// </summary>
        public async Task EnsureAdminExistsAsync()
        {
            try
            {
                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();
                await CreateDefaultAdminAsync(connection);
                
                // Гарантируем, что пароль админа - это просто "admin"
                await ForceAdminPasswordAsync(connection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при проверке наличия администратора");
            }
        }

        /// <summary>
        /// Принудительно устанавливает пароль администратора на "admin"
        /// </summary>
        private async Task ForceAdminPasswordAsync(OleDbConnection connection)
        {
            try
            {
                var updateQuery = "UPDATE Users SET PasswordHash = 'admin' WHERE Username = 'admin'";
                using var command = new OleDbCommand(updateQuery, connection);
                var rowsAffected = await command.ExecuteNonQueryAsync();
                
                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Пароль администратора установлен на 'admin'");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось обновить пароль администратора");
            }
        }

        /// <summary>
        /// Получение всех пользователей (только для администратора)
        /// </summary>
        public async Task<List<User>> GetAllUsersAsync()
        {
            try
            {
                using var connection = new OleDbConnection(_connectionString);
                await connection.OpenAsync();

                var selectQuery = @"
                    SELECT Id, Username, Email, PasswordHash, FirstName, LastName, Role, IsActive, 
                           CreatedDate, LastLoginDate, UpdatedDate
                    FROM Users 
                    ORDER BY CreatedDate DESC";

                using var command = new OleDbCommand(selectQuery, connection);
                using var reader = await command.ExecuteReaderAsync();

                var users = new List<User>();
                while (await reader.ReadAsync())
                {
                    var user = new User
                    {
                        Id = Convert.ToInt32(reader["Id"]),
                        Username = reader["Username"].ToString() ?? "",
                        Email = reader["Email"].ToString() ?? "",
                        PasswordHash = reader["PasswordHash"].ToString() ?? "",
                        FirstName = reader["FirstName"].ToString() ?? "",
                        LastName = reader["LastName"].ToString() ?? "",
                        Role = reader["Role"].ToString() ?? "User",
                        IsActive = Convert.ToBoolean(reader["IsActive"]),
                        CreatedDate = Convert.ToDateTime(reader["CreatedDate"]),
                        LastLoginDate = reader["LastLoginDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["LastLoginDate"]),
                        UpdatedDate = reader["UpdatedDate"] == DBNull.Value ? null : Convert.ToDateTime(reader["UpdatedDate"])
                    };
                    users.Add(user);
                }

                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении списка пользователей");
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
