#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Models;
using SoftwareProductsManager.Services;

namespace SoftwareProductsManager.Windows
{
    /// <summary>
    /// Окно управления пользователями (только для администратора)
    /// </summary>
    public partial class UserManagementWindow : Window
    {
        private readonly AuthenticationService _authService;
        private readonly ILogger<UserManagementWindow> _logger;
        private List<User> _users = new();

        public UserManagementWindow(AuthenticationService authService, ILogger<UserManagementWindow> logger)
        {
            InitializeComponent();
            _authService = authService;
            _logger = logger;
            
            Loaded += UserManagementWindow_Loaded;
        }

        private async void UserManagementWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проверяем, что пользователь является администратором
                if (!_authService.IsAdmin)
                {
                    MessageBox.Show("У вас нет прав для управления пользователями.", 
                        "Доступ запрещен", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Close();
                    return;
                }

                await LoadUsersAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке пользователей");
                MessageBox.Show($"Ошибка при загрузке пользователей: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка загрузки пользователей";
            }
        }

        /// <summary>
        /// Загрузка списка пользователей
        /// </summary>
        private async Task LoadUsersAsync()
        {
            try
            {
                StatusText.Text = "Загрузка пользователей...";
                _users = await _authService.GetAllUsersAsync();
                
                // Отображаем пользователей в DataGrid
                UsersDataGrid.ItemsSource = _users;
                StatusText.Text = $"Загружено {_users.Count} пользователей";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке пользователей");
                throw;
            }
        }

        /// <summary>
        /// Обновление списка пользователей
        /// </summary>
        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadUsersAsync();
        }

        /// <summary>
        /// Обработчик изменения выбранного пользователя
        /// </summary>
        private void UsersDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UsersDataGrid.SelectedItem is User selectedUser)
            {
                ShowUserDetails(selectedUser);
            }
        }

        /// <summary>
        /// Отображение деталей пользователя
        /// </summary>
        private void ShowUserDetails(User user)
        {
            try
            {
                var details = $"Детали пользователя:\n\n";
                details += $"ID: {user.Id}\n";
                details += $"Имя пользователя: {user.Username}\n";
                details += $"Email: {user.Email}\n";
                details += $"Имя: {user.FirstName}\n";
                details += $"Фамилия: {user.LastName}\n";
                details += $"Роль: {user.Role}\n";
                details += $"Активен: {(user.IsActive ? "Да" : "Нет")}\n";
                details += $"Дата создания: {user.CreatedDate:dd.MM.yyyy HH:mm}\n";
                details += $"Последний вход: {(user.LastLoginDate.HasValue ? user.LastLoginDate.Value.ToString("dd.MM.yyyy HH:mm") : "Никогда")}\n";
                details += $"Дата обновления: {(user.UpdatedDate.HasValue ? user.UpdatedDate.Value.ToString("dd.MM.yyyy HH:mm") : "Не обновлялся")}\n";
                details += $"\n🔑 Пароль: {user.PasswordHash}";

                MessageBox.Show(details, "Информация о пользователе", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отображении деталей пользователя");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

