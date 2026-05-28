#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Services;
using SoftwareProductsManager.Windows;

namespace SoftwareProductsManager
{
    /// <summary>
    /// Главный класс приложения
    /// </summary>
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                // Настройка конфигурации
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                // Настройка DI контейнера
                var services = new ServiceCollection();
                ConfigureServices(services, configuration);

                ServiceProvider = services.BuildServiceProvider();

                // Инициализация базы данных
                InitializeDatabaseAsync().Wait();

                base.OnStartup(e);

                // Показываем окно входа
                var loginWindow = ServiceProvider.GetRequiredService<LoginWindow>();
                loginWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                
                // Подписываемся на событие успешного входа
                loginWindow.LoginSuccessful += (sender, e) =>
                {
                    // Если вход успешен, показываем главное окно
                    var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                    MainWindow = mainWindow;
                    mainWindow.Show();
                    mainWindow.Activate();
                    mainWindow.Focus();
                    
                    // Закрываем окно входа
                    loginWindow.Close();
                };
                
                // Подписываемся на событие закрытия окна входа
                loginWindow.Closed += (sender, e) =>
                {
                    // Если окно входа закрылось без успешного входа, закрываем приложение
                    if (MainWindow == null)
                    {
                        Shutdown();
                    }
                };
                
                // Показываем окно входа обычным способом
                loginWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Критическая ошибка при запуске приложения: {ex.Message}", 
                    "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// Настройка сервисов
        /// </summary>
        private void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Конфигурация
            services.AddSingleton(configuration);

            // Логирование
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // Сервисы базы данных
            services.AddSingleton<DatabaseService>();
            services.AddSingleton<SoftwareProductService>();
            services.AddSingleton<TableViewerService>();
            services.AddSingleton<UniversalCrudService>();
            services.AddSingleton<MonitoringReportingService>();

            // Сервис аутентификации
            services.AddSingleton<AuthenticationService>(provider =>
            {
                var databaseService = provider.GetRequiredService<DatabaseService>();
                var logger = provider.GetRequiredService<ILogger<AuthenticationService>>();
                return new AuthenticationService(databaseService.GetConnectionString(), logger);
            });

            // Окна
            services.AddTransient<MainWindow>();
            services.AddTransient<ProductEditWindow>(provider =>
            {
                var productService = provider.GetRequiredService<SoftwareProductService>();
                var logger = provider.GetRequiredService<ILogger<ProductEditWindow>>();
                var authService = provider.GetRequiredService<AuthenticationService>();
                return new ProductEditWindow(productService, logger, authService);
            });
            services.AddTransient<TableViewerWindow>();
            services.AddTransient<UniversalEditWindow>();
            services.AddTransient<UserManagementWindow>(provider =>
            {
                var authService = provider.GetRequiredService<AuthenticationService>();
                var logger = provider.GetRequiredService<ILogger<UserManagementWindow>>();
                return new UserManagementWindow(authService, logger);
            });
            services.AddTransient<LoginWindow>();
            services.AddTransient<RegisterWindow>();
        }

        /// <summary>
        /// Инициализация базы данных
        /// </summary>
        private async Task InitializeDatabaseAsync()
        {
            try
            {
                var databaseService = ServiceProvider.GetRequiredService<DatabaseService>();
                
                // Проверка подключения
                var isConnected = await databaseService.TestConnectionAsync();
                if (!isConnected)
                {
                    throw new Exception("Не удалось подключиться к базе данных. Проверьте путь к файлу базы данных.");
                }

                // Показываем все таблицы в вашей базе данных
                var tables = await databaseService.GetAllTablesAsync();
                var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
                logger.LogInformation($"Таблицы в вашей базе данных: {string.Join(", ", tables)}");
                
                // Проверяем данные в таблицах с русскими названиями
                await databaseService.CheckDataInRussianTablesAsync();
                
                // Создаем таблицы если их нет
                await databaseService.CreateTablesIfNotExistAsync();
                
                // Создаем таблицу пользователей
                var authService = ServiceProvider.GetRequiredService<AuthenticationService>();
                await authService.CreateUsersTableIfNotExistsAsync();
                
                // Создаем администратора, если его нет
                await authService.EnsureAdminExistsAsync();
                
                // Добавляем тестовые данные если таблица пуста
                await databaseService.AddTestDataIfEmptyAsync();
            }
            catch (Exception ex)
            {
                var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
                logger.LogError(ex, "Ошибка при инициализации базы данных");
                throw;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            base.OnExit(e);
        }
    }
}
