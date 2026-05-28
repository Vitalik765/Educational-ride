#nullable enable
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Services;

namespace SoftwareProductsManager.Windows
{
    /// <summary>
    /// Окно регистрации нового пользователя
    /// </summary>
    public partial class RegisterWindow : Window
    {
        private readonly AuthenticationService _authService;
        private readonly ILogger<RegisterWindow> _logger;

        public RegisterWindow(AuthenticationService authService, ILogger<RegisterWindow> logger)
        {
            InitializeComponent();
            _authService = authService;
            _logger = logger;
            
            // Устанавливаем фокус на поле имени пользователя
            Loaded += (s, e) => UsernameTextBox.Focus();
        }

        /// <summary>
        /// Обработчик кнопки регистрации
        /// </summary>
        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            var username = UsernameTextBox.Text.Trim();
            var email = EmailTextBox.Text.Trim();
            var firstName = FirstNameTextBox.Text.Trim();
            var lastName = LastNameTextBox.Text.Trim();
            var password = PasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;

            // Валидация
            if (!ValidateInput(username, email, password, confirmPassword))
                return;

            try
            {
                RegisterButton.IsEnabled = false;
                ShowStatus("Регистрация пользователя...", false);

                var success = await _authService.RegisterAsync(username, email, password, firstName, lastName);
                
                if (success)
                {
                    ShowStatus("Регистрация выполнена успешно", false);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ShowStatus("Ошибка регистрации. Возможно, пользователь с таким именем или email уже существует", true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при регистрации пользователя");
                ShowStatus("Ошибка при регистрации пользователя", true);
            }
            finally
            {
                RegisterButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Обработчик кнопки отмены
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Валидация введенных данных
        /// </summary>
        private bool ValidateInput(string username, string email, string password, string confirmPassword)
        {
            // Проверка обязательных полей
            if (string.IsNullOrEmpty(username))
            {
                ShowStatus("Введите имя пользователя", true);
                UsernameTextBox.Focus();
                return false;
            }

            if (string.IsNullOrEmpty(email))
            {
                ShowStatus("Введите email", true);
                EmailTextBox.Focus();
                return false;
            }

            if (string.IsNullOrEmpty(password))
            {
                ShowStatus("Введите пароль", true);
                PasswordBox.Focus();
                return false;
            }

            // Проверка длины имени пользователя
            if (username.Length < 3)
            {
                ShowStatus("Имя пользователя должно содержать минимум 3 символа", true);
                UsernameTextBox.Focus();
                return false;
            }

            // Проверка email
            if (!IsValidEmail(email))
            {
                ShowStatus("Введите корректный email адрес", true);
                EmailTextBox.Focus();
                return false;
            }

            // Проверка пароля
            if (password.Length < 6)
            {
                ShowStatus("Пароль должен содержать минимум 6 символов", true);
                PasswordBox.Focus();
                return false;
            }

            // Проверка совпадения паролей
            if (password != confirmPassword)
            {
                ShowStatus("Пароли не совпадают", true);
                ConfirmPasswordBox.Focus();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Проверка корректности email
        /// </summary>
        private bool IsValidEmail(string email)
        {
            try
            {
                var regex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                return regex.IsMatch(email);
            }
            catch
            {
                return false;
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
    }
}
