#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Services;

namespace SoftwareProductsManager.Windows
{
    /// <summary>
    /// Окно входа в систему
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly AuthenticationService _authService;
        private readonly ILogger<LoginWindow> _logger;

        /// <summary>
        /// Событие успешного входа
        /// </summary>
        public event EventHandler? LoginSuccessful;

        public LoginWindow(AuthenticationService authService, ILogger<LoginWindow> logger)
        {
            InitializeComponent();
            _authService = authService;
            _logger = logger;
            
            // Устанавливаем фокус на поле имени пользователя
            Loaded += (s, e) => UsernameTextBox.Focus();
            
            // Обработка нажатия Enter
            UsernameTextBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) PasswordBox.Focus(); };
            PasswordBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) LoginButton_Click(s, e); };
        }

        /// <summary>
        /// Обработчик кнопки входа
        /// </summary>
        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text.Trim();
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowStatus("Введите имя пользователя и пароль", true);
                return;
            }

            try
            {
                LoginButton.IsEnabled = false;
                ShowStatus("Проверка данных...", false);

                var success = await _authService.LoginAsync(username, password);
                
                if (success)
                {
                    ShowStatus("Вход выполнен успешно", false);
                    await Task.Delay(1000); // Задержка для отображения сообщения
                    
                    // Вызываем событие успешного входа
                    LoginSuccessful?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ShowStatus("Неверное имя пользователя или пароль", true);
                    PasswordBox.Clear();
                    PasswordBox.Focus();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при входе в систему");
                ShowStatus("Ошибка при входе в систему", true);
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Обработчик кнопки регистрации
        /// </summary>
        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var registerWindow = App.ServiceProvider.GetRequiredService<RegisterWindow>();
                registerWindow.Owner = this;
                
                if (registerWindow.ShowDialog() == true)
                {
                    ShowStatus("Регистрация выполнена успешно. Теперь вы можете войти в систему", false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при открытии окна регистрации");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Отображение статуса
        /// </summary>
        private void ShowStatus(string message, bool isError)
        {
            StatusText.Text = message;
            StatusText.Foreground = isError ? 
                System.Windows.Media.Brushes.Red : 
                System.Windows.Media.Brushes.Green;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Просто закрываем окно без установки DialogResult
            base.OnClosing(e);
        }
    }
}
