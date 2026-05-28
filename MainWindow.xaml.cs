#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Models;
using SoftwareProductsManager.Services;
using SoftwareProductsManager.Windows;

namespace SoftwareProductsManager
{
    /// <summary>
    /// Главное окно приложения
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SoftwareProductService _productService;
        private readonly TableViewerService _tableViewerService;
        private readonly UniversalCrudService _crudService;
        private readonly AuthenticationService _authService;
        private readonly MonitoringReportingService _monitoringReportingService;
        private readonly ILogger<MainWindow> _logger;
        private IReadOnlyList<Dictionary<string, object>> _part2LastReportRows = Array.Empty<Dictionary<string, object>>();
        private bool _part2InternalReportTableComboChange;

        /// <summary>Пункт в списке колонки даты: отчёт без WHERE по дате (для таблиц без поля даты, напр. «Карты»).</summary>
        private const string Part2DateFilterAllRowsLabel = "— Вся таблица (без фильтра по дате) —";
        private List<SoftwareProduct> _allProducts = new();
        private List<SoftwareProduct> _filteredProducts = new();
        private string _currentTableName = "";
        private string _currentPrimaryKeyColumn = "Id";

        public MainWindow(SoftwareProductService productService, TableViewerService tableViewerService, 
            UniversalCrudService crudService, AuthenticationService authService,
            MonitoringReportingService monitoringReportingService, ILogger<MainWindow> logger)
        {
            InitializeComponent();
            _productService = productService;
            _tableViewerService = tableViewerService;
            _crudService = crudService;
            _authService = authService;
            _monitoringReportingService = monitoringReportingService;
            _logger = logger;
            
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("MainWindow загружается...");
                
                // Обновляем информацию о пользователе
                UpdateUserInfo();
                
                // Управляем видимостью кнопок в зависимости от роли пользователя
                UpdateButtonVisibility();
                
                StatusText.Text = "Загрузка таблиц...";
                await LoadTablesAsync();
                
                // Загружаем данные только если есть выбранная таблица
                if (!string.IsNullOrEmpty(_currentTableName))
                {
                    await LoadTableDataAsync();
                    StatusText.Text = $"Загружено {GetCurrentRowCount()} записей из таблицы {_currentTableName}";
                }
                else
                {
                    StatusText.Text = "Нет доступных таблиц для загрузки";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке данных");
                MessageBox.Show($"Ошибка при загрузке данных: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка загрузки данных";
            }
        }

        /// <summary>
        /// Загрузка списка таблиц
        /// </summary>
        private async Task LoadTablesAsync()
        {
            try
            {
                var tables = await _tableViewerService.GetAllTablesAsync();
                var tableInfos = new List<TableInfo>();
                
                foreach (var tableName in tables)
                {
                    // Скрываем таблицу Users от обычных пользователей
                    if (!_authService.IsAdmin && tableName.Equals("Users", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    
                    try
                    {
                        var rowCount = await _tableViewerService.GetTableRowCountAsync(tableName);
                        tableInfos.Add(new TableInfo { TableName = tableName, RowCount = rowCount });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Не удалось получить количество строк для таблицы {tableName}");
                        // Добавляем таблицу с нулевым количеством строк
                        tableInfos.Add(new TableInfo { TableName = tableName, RowCount = 0 });
                    }
                }
                
                TableSelector.ItemsSource = tableInfos;
                
                // Выбираем первую таблицу по умолчанию
                if (tableInfos.Any())
                {
                    TableSelector.SelectedItem = tableInfos.First();
                    _currentTableName = tableInfos.First().TableName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке списка таблиц");
                throw;
            }
        }

        /// <summary>
        /// Загрузка данных из выбранной таблицы
        /// </summary>
        private async Task LoadTableDataAsync()
        {
            try
            {
                // Определяем первичный ключ таблицы
                _currentPrimaryKeyColumn = await _crudService.GetPrimaryKeyColumnAsync(_currentTableName) ?? "Id";

                // Используем универсальный просмотр для всех таблиц
                var data = await _tableViewerService.GetTableDataAsync(_currentTableName);
                
                // Преобразуем данные в формат, подходящий для DataGrid
                var dataTable = new System.Data.DataTable();
                
                if (data.Any())
                {
                    // Добавляем колонки
                    foreach (var columnName in data.First().Keys)
                    {
                        dataTable.Columns.Add(columnName, typeof(object));
                    }

                    // Добавляем строки
                    foreach (var row in data)
                    {
                        var dataRow = dataTable.NewRow();
                        foreach (var kvp in row)
                        {
                            dataRow[kvp.Key] = kvp.Value;
                        }
                        dataTable.Rows.Add(dataRow);
                    }
                }

                ProductsDataGrid.ItemsSource = dataTable.DefaultView;
                _allProducts.Clear();
                _filteredProducts.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при загрузке данных из таблицы {_currentTableName}");
                throw;
            }
        }

        /// <summary>
        /// Обработчик изменения выбранной таблицы
        /// </summary>
        private async void TableSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedTable = TableSelector.SelectedItem as TableInfo;
            if (selectedTable == null) return;

            try
            {
                StatusText.Text = $"Загрузка данных из таблицы {selectedTable.TableName}...";
                _currentTableName = selectedTable.TableName;
                await LoadTableDataAsync();
                StatusText.Text = $"Загружено {selectedTable.RowCount} записей из таблицы {selectedTable.TableName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при переключении на таблицу {selectedTable.TableName}");
                MessageBox.Show($"Ошибка при загрузке таблицы: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка загрузки таблицы";
            }
        }

        /// <summary>
        /// Загрузка списка продуктов
        /// </summary>
        private async Task LoadProductsAsync()
        {
            try
            {
                _allProducts = await _productService.GetAllProductsAsync();
                _filteredProducts = new List<SoftwareProduct>(_allProducts);
                ProductsDataGrid.ItemsSource = _filteredProducts;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке продуктов");
                throw;
            }
        }

        /// <summary>
        /// Обработчик изменения текста поиска
        /// </summary>
        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Обновляем видимость placeholder
            UpdatePlaceholderVisibility();
            
            var searchTerm = SearchTextBox.Text.Trim();
            
            if (string.IsNullOrEmpty(searchTerm))
            {
                // Если поиск пустой, загружаем все данные из текущей таблицы
                await LoadTableDataAsync();
            }
            else
            {
                try
                {
                    // Всегда используем универсальный поиск для всех таблиц
                    await SearchInCurrentTableAsync(searchTerm);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при поиске");
                    MessageBox.Show($"Ошибка при поиске: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            StatusText.Text = $"Найдено {GetCurrentRowCount()} записей";
        }

        /// <summary>
        /// Очистка поиска
        /// </summary>
        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
            UpdatePlaceholderVisibility();
        }

        /// <summary>
        /// Обработчик получения фокуса
        /// </summary>
        private void SearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            UpdatePlaceholderVisibility();
        }

        /// <summary>
        /// Обработчик потери фокуса
        /// </summary>
        private void SearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdatePlaceholderVisibility();
        }

        /// <summary>
        /// Обновление видимости placeholder
        /// </summary>
        private void UpdatePlaceholderVisibility()
        {
            var placeholder = SearchTextBox.Template.FindName("PlaceholderText", SearchTextBox) as TextBlock;
            if (placeholder != null)
            {
                placeholder.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Добавление нового продукта
        /// </summary>
        private void AddProduct_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var addWindow = App.ServiceProvider.GetRequiredService<ProductEditWindow>();
                addWindow.SetProduct(null);
                
                if (addWindow.ShowDialog() == true)
                {
                    _ = LoadProductsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при открытии окна добавления продукта");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Редактирование выбранного продукта
        /// </summary>
        private void EditProduct_Click(object sender, RoutedEventArgs e)
        {
            var selectedProduct = ProductsDataGrid.SelectedItem as SoftwareProduct;
            if (selectedProduct == null)
            {
                MessageBox.Show("Выберите продукт для редактирования", "Предупреждение", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var editWindow = App.ServiceProvider.GetRequiredService<ProductEditWindow>();
                editWindow.SetProduct(selectedProduct);
                
                if (editWindow.ShowDialog() == true)
                {
                    _ = LoadProductsAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при открытии окна редактирования продукта");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Удаление выбранного продукта
        /// </summary>
        private async void DeleteProduct_Click(object sender, RoutedEventArgs e)
        {
            var selectedProduct = ProductsDataGrid.SelectedItem as SoftwareProduct;
            if (selectedProduct == null)
            {
                MessageBox.Show("Выберите продукт для удаления", "Предупреждение", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Вы уверены, что хотите удалить продукт '{selectedProduct.Name}'?", 
                "Подтверждение удаления", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var success = await _productService.DeleteProductAsync(selectedProduct.Id);
                    if (success)
                    {
                        MessageBox.Show("Продукт успешно удален", "Успех", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        await LoadProductsAsync();
                    }
                    else
                    {
                        MessageBox.Show("Не удалось удалить продукт", "Ошибка", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при удалении продукта");
                    MessageBox.Show($"Ошибка при удалении: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Обновление списка продуктов
        /// </summary>
        private async void RefreshProducts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Обновление данных...";
                await LoadProductsAsync();
                StatusText.Text = $"Обновлено {_allProducts.Count} продуктов";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении данных");
                MessageBox.Show($"Ошибка при обновлении: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка обновления данных";
            }
        }

        /// <summary>
        /// Обработчик изменения выбранного продукта
        /// </summary>
        private void ProductsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedItem = ProductsDataGrid.SelectedItem;
            
            if (selectedItem != null)
            {
                ShowRecordDetails(selectedItem);
            }
            else
            {
                HideProductDetails();
            }
        }

        /// <summary>
        /// Отображение деталей любой записи
        /// </summary>
        private void ShowRecordDetails(object selectedItem)
        {
            RecordDetailsPanel.Visibility = Visibility.Visible;
            NoSelectionPanel.Visibility = Visibility.Collapsed;

            // Очищаем предыдущие поля
            DynamicFieldsPanel.Children.Clear();

            if (selectedItem is SoftwareProduct product)
            {
                // Для объектов SoftwareProduct используем кулинарные подписи
                DetailsTitle.Text = "Детали компонента";

                AddDetailField("Наименование компонента", product.Name);
                AddDetailField("Описание", product.Description);
                AddDetailField("Версия/релиз", product.Version);
                AddDetailField("Ответственная команда", product.Developer);
                AddDetailField("Категория модуля", product.Category);
                AddDetailField("Эксплуатационные затраты", product.Price.ToString("C"));
                AddDetailField("Дата ввода в сопровождение", product.ReleaseDate.ToString("dd.MM.yyyy"));
                AddDetailField("Лицензирование", product.License);
                AddDetailField("Технические требования", product.SystemRequirements);
                AddDetailField("Дата создания", product.CreatedDate.ToString("dd.MM.yyyy HH:mm"));
                AddDetailField("Дата обновления", product.UpdatedDate?.ToString("dd.MM.yyyy HH:mm") ?? "Не обновлялся");
            }
            else if (selectedItem is System.Data.DataRowView dataRowView)
            {
                // Для DataRowView (универсальные таблицы) создаем поля динамически
                DetailsTitle.Text = $"Детали записи из таблицы {_currentTableName}";
                
                for (int i = 0; i < dataRowView.Row.Table.Columns.Count; i++)
                {
                    var columnName = dataRowView.Row.Table.Columns[i].ColumnName;
                    var value = dataRowView[i];
                    var stringValue = value?.ToString() ?? "NULL";
                    
                    // Переводим технические названия на русский
                    var displayName = TranslateColumnName(columnName);
                    AddDetailField(displayName, stringValue);
                }
            }
        }

        /// <summary>
        /// Добавляет поле деталей в динамическую панель
        /// </summary>
        private void AddDetailField(string fieldName, string fieldValue)
        {
            if (string.IsNullOrEmpty(fieldValue) || fieldValue == "NULL")
                return;

            // Создаем контейнер для поля
            var fieldContainer = new StackPanel { Margin = new Thickness(0, 5, 0, 10) };

            // Создаем лейбл
            var label = new TextBlock
            {
                Text = $"{fieldName}:",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 5, 0, 2)
            };

            // Создаем значение
            var value = new TextBlock
            {
                Text = fieldValue,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 0)
            };

            fieldContainer.Children.Add(label);
            fieldContainer.Children.Add(value);
            DynamicFieldsPanel.Children.Add(fieldContainer);
        }

        /// <summary>
        /// Переводит технические названия столбцов на понятные русские названия
        /// </summary>
        private string TranslateColumnName(string columnName)
        {
            var translations = new Dictionary<string, string>
            {
                { "ID", "ID" },
                { "КодЗаписи", "Код записи" },
                { "КодУстановки", "Код установки" },
                { "Дата", "Дата" },
                { "Действие", "Действие" },
                { "Результат", "Результат" },
                { "Сообщение", "Сообщение" },
                { "Название", "Наименование компонента" },
                { "Name", "Наименование компонента" },
                { "Описание", "Описание" },
                { "Description", "Описание" },
                { "Версия", "Версия/релиз" },
                { "Version", "Версия/релиз" },
                { "Разработчик", "Ответственная команда" },
                { "Developer", "Ответственная команда" },
                { "Категория", "Категория модуля" },
                { "Category", "Категория модуля" },
                { "Цена", "Эксплуатационные затраты" },
                { "Price", "Эксплуатационные затраты" },
                { "ДатаВыпуска", "Дата ввода в сопровождение" },
                { "ReleaseDate", "Дата ввода в сопровождение" },
                { "Лицензия", "Лицензирование" },
                { "License", "Лицензирование" },
                { "СистемныеТребования", "Технические требования" },
                { "SystemRequirements", "Технические требования" },
                { "ДатаСоздания", "Дата создания" },
                { "CreatedDate", "Дата создания" },
                { "ДатаОбновления", "Дата обновления" },
                { "UpdatedDate", "Дата обновления" }
            };

            return translations.ContainsKey(columnName) ? translations[columnName] : columnName;
        }

        /// <summary>
        /// Отображение деталей продукта (старый метод для совместимости)
        /// </summary>
        private void ShowProductDetails(SoftwareProduct product)
        {
            ShowRecordDetails(product);
        }

        /// <summary>
        /// Просмотр всех таблиц базы данных
        /// </summary>
        private void ViewAllTables_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tableViewerWindow = App.ServiceProvider.GetRequiredService<TableViewerWindow>();
                tableViewerWindow.Show();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при открытии окна просмотра таблиц");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Добавление новой записи
        /// </summary>
        private async void AddRecord_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем права доступа
            if (!_authService.IsAdmin)
            {
                MessageBox.Show("У вас нет прав для добавления записей. Обратитесь к администратору.", 
                    "Доступ запрещен", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var editWindow = new UniversalEditWindow(_crudService, 
                    App.ServiceProvider.GetRequiredService<ILogger<UniversalEditWindow>>(), 
                    _authService,
                    _currentTableName);
                
                if (editWindow.ShowDialog() == true)
                {
                    await LoadTableDataAsync();
                    StatusText.Text = $"Запись добавлена в таблицу {_currentTableName}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при добавлении записи в таблицу {_currentTableName}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// Редактирование выбранной записи
        /// </summary>
        private async void EditRecord_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем права доступа
            if (!_authService.IsAdmin)
            {
                MessageBox.Show("У вас нет прав для редактирования записей. Обратитесь к администратору.", 
                    "Доступ запрещен", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var selectedItem = ProductsDataGrid.SelectedItem;
                if (selectedItem == null)
                {
                    MessageBox.Show("Выберите запись для редактирования", "Предупреждение", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                object? primaryKeyValue = null;
                string recordName = "запись";
                
                if (selectedItem is System.Data.DataRowView dataRowView)
                {
                    primaryKeyValue = dataRowView[_currentPrimaryKeyColumn];
                    recordName = $"запись с ID {primaryKeyValue}";
                }

                if (primaryKeyValue == null)
                {
                    MessageBox.Show("Не удалось определить ID записи", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var editWindow = new UniversalEditWindow(_crudService, 
                    App.ServiceProvider.GetRequiredService<ILogger<UniversalEditWindow>>(), 
                    _authService,
                    _currentTableName, primaryKeyValue, _currentPrimaryKeyColumn);
                
                if (editWindow.ShowDialog() == true)
                {
                    await LoadTableDataAsync();
                    StatusText.Text = $"Запись обновлена в таблице {_currentTableName}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при редактировании записи в таблице {_currentTableName}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Удаление выбранной записи
        /// </summary>
        private async void DeleteRecord_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем права доступа
            if (!_authService.IsAdmin)
            {
                MessageBox.Show("У вас нет прав для удаления записей. Обратитесь к администратору.", 
                    "Доступ запрещен", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var selectedItem = ProductsDataGrid.SelectedItem;
                if (selectedItem == null)
                {
                    MessageBox.Show("Выберите запись для удаления", "Предупреждение", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                object? primaryKeyValue = null;
                string recordName = "запись";
                
                if (_currentTableName == "SoftwareProducts")
                {
                    var product = selectedItem as SoftwareProduct;
                    primaryKeyValue = product?.Id;
                    recordName = product?.Name ?? "продукт";
                }
                else
                {
                    // Для других таблиц получаем значение первичного ключа из DataRowView
                    if (selectedItem is System.Data.DataRowView dataRowView)
                    {
                        primaryKeyValue = dataRowView[_currentPrimaryKeyColumn];
                        recordName = $"запись с ID {primaryKeyValue}";
                    }
                }

                if (primaryKeyValue == null)
                {
                    MessageBox.Show("Не удалось определить ID записи", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var result = MessageBox.Show(
                    $"Вы уверены, что хотите удалить {recordName}?", 
                    "Подтверждение удаления", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var success = await _crudService.DeleteRecordAsync(_currentTableName, _currentPrimaryKeyColumn, primaryKeyValue);
                    if (success)
                    {
                        await LoadTableDataAsync();
                        StatusText.Text = $"Запись удалена из таблицы {_currentTableName}";
                    }
                    else
                    {
                        MessageBox.Show("Не удалось удалить запись", "Ошибка", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при удалении записи из таблицы {_currentTableName}");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Обновление данных
        /// </summary>
        private async void RefreshData_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Обновление данных...";
                await LoadTableDataAsync();
                StatusText.Text = $"Данные обновлены для таблицы {_currentTableName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обновлении данных");
                MessageBox.Show($"Ошибка при обновлении: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка обновления данных";
            }
        }

        /// <summary>
        /// Скрытие деталей продукта
        /// </summary>
        private void HideProductDetails()
        {
            RecordDetailsPanel.Visibility = Visibility.Collapsed;
            NoSelectionPanel.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Поиск в текущей таблице
        /// </summary>
        private async Task SearchInCurrentTableAsync(string searchTerm)
        {
            try
            {
                // Получаем все данные из текущей таблицы
                var data = await _tableViewerService.GetTableDataAsync(_currentTableName);
                
                // Фильтруем данные по поисковому запросу (регистронезависимый поиск)
                var searchTermLower = searchTerm.ToLower();
                var filteredData = data.Where(row => 
                    row.Values.Any(value => 
                    {
                        var stringValue = value?.ToString();
                        return !string.IsNullOrEmpty(stringValue) && 
                               stringValue.ToLower().Contains(searchTermLower);
                    }))
                    .ToList();

                // Преобразуем в DataTable для отображения
                var dataTable = new System.Data.DataTable();
                
                if (filteredData.Any())
                {
                    // Добавляем колонки
                    foreach (var columnName in filteredData.First().Keys)
                    {
                        dataTable.Columns.Add(columnName, typeof(object));
                    }

                    // Добавляем строки
                    foreach (var row in filteredData)
                    {
                        var dataRow = dataTable.NewRow();
                        foreach (var kvp in row)
                        {
                            dataRow[kvp.Key] = kvp.Value;
                        }
                        dataTable.Rows.Add(dataRow);
                    }
                }

                ProductsDataGrid.ItemsSource = dataTable.DefaultView;
                _logger.LogInformation($"Найдено {filteredData.Count} записей по запросу '{searchTerm}' в таблице '{_currentTableName}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при поиске в таблице {_currentTableName}");
                throw;
            }
        }

        /// <summary>
        /// Получить количество строк в текущем источнике данных
        /// </summary>
        private int GetCurrentRowCount()
        {
            if (ProductsDataGrid.ItemsSource is System.Data.DataView dataView)
            {
                return dataView.Count;
            }
            else if (ProductsDataGrid.ItemsSource is List<SoftwareProduct> products)
            {
                return products.Count;
            }
            return 0;
        }

        /// <summary>
        /// Обновление информации о пользователе
        /// </summary>
        private void UpdateUserInfo()
        {
            if (_authService.CurrentUser != null)
            {
                var user = _authService.CurrentUser;
                var roleText = user.Role == "Admin" ? " (Администратор)" : " (Пользователь)";
                UserInfoText.Text = $"Пользователь: {user.Username}{roleText}";
            }
            else
            {
                UserInfoText.Text = "Пользователь: Не авторизован";
            }
        }

        /// <summary>
        /// Обновление видимости кнопок в зависимости от роли пользователя
        /// </summary>
        private void UpdateButtonVisibility()
        {
            if (_authService.IsAdmin)
            {
                // Администратор видит все кнопки
                AddButton.Visibility = Visibility.Visible;
                EditButton.Visibility = Visibility.Visible;
                DeleteButton.Visibility = Visibility.Visible;
            }
            else
            {
                // Обычный пользователь не может добавлять, редактировать или удалять записи
                AddButton.Visibility = Visibility.Collapsed;
                EditButton.Visibility = Visibility.Collapsed;
                DeleteButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Обработчик кнопки выхода
        /// </summary>
        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Вы уверены, что хотите выйти из системы?", 
                    "Подтверждение выхода", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _authService.Logout();
                    
                    // Создаем и показываем новое окно входа
                    var loginWindow = new LoginWindow(_authService, 
                        App.ServiceProvider.GetRequiredService<ILogger<LoginWindow>>());
                    loginWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    
                    // Подписываемся на успешный вход для повторного открытия главного окна
                    loginWindow.LoginSuccessful += (s, args) =>
                    {
                        // Создаем новое главное окно и показываем его
                        var mainWindow = App.ServiceProvider.GetRequiredService<MainWindow>();
                        Application.Current.MainWindow = mainWindow;
                        mainWindow.Show();
                        mainWindow.Activate();
                        mainWindow.Focus();
                        loginWindow.Close();
                    };
                    
                    // Подписываемся на закрытие окна входа
                    loginWindow.Closed += (s, args) =>
                    {
                        // Если окно входа закрылось без входа, закрываем приложение
                        if (_authService.CurrentUser == null)
                        {
                            Application.Current.Shutdown();
                        }
                    };
                    
                    // Скрываем главное окно и показываем окно входа
                    Hide();
                    loginWindow.Show();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при выходе из системы");
                    MessageBox.Show($"Ошибка при выходе: {ex.Message}", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Обработчик закрытия главного окна
        /// </summary>
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _authService.Logout();
                _logger.LogInformation("Приложение закрывается");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при закрытии приложения");
            }
        }

        /// <summary>
        /// Обработчик события закрытия главного окна
        /// </summary>
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            try
            {
                _logger.LogInformation("Главное окно закрыто");
                // Закрываем приложение только если это действительно закрытие главного окна
                if (_authService.IsAuthenticated)
                {
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при закрытии главного окна");
            }
        }

        private void MainWorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // SelectionChanged всплывает от ComboBox/DataGrid внутри вкладки — без этой проверки
            // при каждой смене таблицы/колонки снова грузится вся сводка и интерфейс «зависает».
            if (!ReferenceEquals(e.Source, sender))
                return;

            if (MainWorkspaceTabs.SelectedIndex != 1)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsurePart2DateDefaults();
                _ = LoadPart2SummariesAsync();
            }), DispatcherPriority.Loaded);
        }

        private void EnsurePart2DateDefaults()
        {
            if (Part2DateFrom.SelectedDate == null)
                Part2DateFrom.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            if (Part2DateTo.SelectedDate == null)
                Part2DateTo.SelectedDate = DateTime.Today;
        }

        private async Task LoadPart2SummariesAsync()
        {
            try
            {
                StatusText.Text = "Загрузка сводки для части 2...";
                var rows = await _monitoringReportingService.GetTableSummariesAsync(_authService.IsAdmin);
                BindPart2SummaryGrid(rows);

                var allTables = await _tableViewerService.GetAllTablesAsync();
                var filteredTables = allTables
                    .Where(t => _authService.IsAdmin || !t.Equals("Users", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // Сохраняем выбор до сброса ItemsSource (иначе после загрузки сводки всегда остаётся первая таблица)
                var savedTableName = Part2ReportTableCombo.SelectedItem as string;

                _part2InternalReportTableComboChange = true;
                try
                {
                    Part2ReportTableCombo.ItemsSource = null;
                    Part2ReportTableCombo.ItemsSource = filteredTables;

                    string? tableToSelect = null;
                    if (!string.IsNullOrEmpty(savedTableName))
                        tableToSelect = filteredTables.FirstOrDefault(t =>
                            t.Equals(savedTableName, StringComparison.OrdinalIgnoreCase));

                    if (tableToSelect != null)
                        Part2ReportTableCombo.SelectedItem = tableToSelect;
                    else if (filteredTables.Count > 0)
                        Part2ReportTableCombo.SelectedIndex = 0;
                    else
                        Part2ReportTableCombo.SelectedIndex = -1;
                }
                finally
                {
                    _part2InternalReportTableComboChange = false;
                }

                if (Part2ReportTableCombo.SelectedItem is string selectedTable)
                    await Part2LoadColumnsForTableAsync(selectedTable);

                var readyCount = rows.Count(r => r.SupportsResultReport);
                StatusText.Text = $"Часть 2: сводка загружена ({rows.Count} табл.). Автоотчёт возможен для {readyCount} табл.; иначе выберите колонки вручную.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка загрузки сводки части 2");
                MessageBox.Show($"Не удалось загрузить сводку: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка загрузки части 2";
            }
        }

        private void BindPart2SummaryGrid(IReadOnlyList<TableSummaryRow> rows)
        {
            var dt = new DataTable();
            dt.Columns.Add("Таблица", typeof(string));
            dt.Columns.Add("Записей", typeof(string));
            dt.Columns.Add("Колонка даты", typeof(string));
            dt.Columns.Add("Колонка результата", typeof(string));
            dt.Columns.Add("Автоотчёт", typeof(string));
            dt.Columns.Add("Примечание", typeof(string));

            foreach (var r in rows)
            {
                var rowCountText = string.IsNullOrEmpty(r.LoadError) ? r.RowCount.ToString() : "—";

                dt.Rows.Add(
                    r.TableName,
                    rowCountText,
                    string.IsNullOrEmpty(r.DateColumnName) ? "—" : r.DateColumnName,
                    string.IsNullOrEmpty(r.ResultColumnName) ? "—" : r.ResultColumnName,
                    r.SupportsResultReport ? "Да" : "Нет",
                    r.LoadError ?? "");
            }

            Part2SummaryGrid.ItemsSource = dt.DefaultView;
        }

        private async void Part2ReportTableCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_part2InternalReportTableComboChange)
                return;

            if (Part2ReportTableCombo.SelectedItem is not string tableName)
                return;

            await Part2LoadColumnsForTableAsync(tableName);
        }

        private async Task Part2LoadColumnsForTableAsync(string tableName)
        {
            try
            {
                var cols = await _tableViewerService.GetTableColumnsAsync(tableName);

                var dateItems = new List<string> { Part2DateFilterAllRowsLabel };
                dateItems.AddRange(cols);
                Part2DateColumnCombo.ItemsSource = dateItems;

                Part2ResultColumnCombo.ItemsSource = cols;

                var dateGuess = MonitoringReportingService.SuggestDateColumn(cols);
                var resultGuess = MonitoringReportingService.SuggestResultColumn(cols);

                if (dateGuess != null)
                {
                    var dateMatch = dateItems.FirstOrDefault(x =>
                        !string.Equals(x, Part2DateFilterAllRowsLabel, StringComparison.Ordinal) &&
                        x.Equals(dateGuess, StringComparison.OrdinalIgnoreCase));
                    Part2DateColumnCombo.SelectedItem = dateMatch ?? dateItems[0];
                }
                else
                {
                    Part2DateColumnCombo.SelectedItem = dateItems[0];
                }

                Part2ResultColumnCombo.SelectedItem = cols.FirstOrDefault(c =>
                    resultGuess != null && c.Equals(resultGuess, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось загрузить колонки для части 2, таблица {Table}", tableName);
                Part2DateColumnCombo.ItemsSource = null;
                Part2ResultColumnCombo.ItemsSource = null;
                MessageBox.Show($"Не удалось прочитать колонки таблицы «{tableName}»: {ex.Message}", "Часть 2",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void Part2RefreshSummary_Click(object sender, RoutedEventArgs e)
        {
            EnsurePart2DateDefaults();
            await LoadPart2SummariesAsync();
        }

        private async void Part2BuildReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EnsurePart2DateDefaults();
                if (Part2ReportTableCombo.SelectedItem is not string tableName)
                {
                    MessageBox.Show("Выберите таблицу для отчёта.", "Часть 2",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (Part2DateColumnCombo.SelectedItem is not string datePick)
                {
                    MessageBox.Show("Выберите вариант в списке «Колонка даты» (можно «вся таблица»).", "Часть 2",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (Part2ResultColumnCombo.SelectedItem is not string resultCol)
                {
                    MessageBox.Show("Выберите колонку результата (группировка).", "Часть 2",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var from = Part2DateFrom.SelectedDate ?? DateTime.Today;
                var to = Part2DateTo.SelectedDate ?? DateTime.Today;
                var useDateFilter = !string.Equals(datePick, Part2DateFilterAllRowsLabel, StringComparison.Ordinal);

                if (useDateFilter && to < from)
                {
                    MessageBox.Show("Дата «по» не может быть раньше даты «с».", "Часть 2",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusText.Text = $"Построение отчёта для таблицы {tableName}...";
                Part2ReportGrid.ItemsSource = null;

                IReadOnlyList<Dictionary<string, object>> data;
                if (useDateFilter)
                    data = await _monitoringReportingService.GetResultDistributionAsync(tableName, datePick, resultCol, from, to);
                else
                    data = await _monitoringReportingService.GetResultDistributionWithoutDateFilterAsync(tableName, resultCol);

                _part2LastReportRows = data.ToList();
                BindDictionariesToGrid(Part2ReportGrid, _part2LastReportRows);

                if (_part2LastReportRows.Count == 0)
                {
                    MessageBox.Show(
                        "Запрос выполнен, но в таблице нет строк для отображения (или все значения результата пустые).\n" +
                        (useDateFilter
                            ? "Попробуйте режим «вся таблица (без фильтра по дате)» в списке колонки даты или расширьте период."
                            : "Проверьте выбранную колонку результата."),
                        "Часть 2 — отчёт пустой",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                StatusText.Text = useDateFilter
                    ? $"Часть 2: отчёт построен за период, строк: {_part2LastReportRows.Count}"
                    : $"Часть 2: отчёт по всей таблице (без фильтра даты), строк: {_part2LastReportRows.Count}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка построения отчёта части 2");
                MessageBox.Show($"Ошибка отчёта: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Part2ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_part2LastReportRows.Count == 0)
                {
                    MessageBox.Show("Сначала постройте отчёт.", "Часть 2",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "CSV (*.csv)|*.csv",
                    FileName = $"report_part2_{DateTime.Now:yyyyMMdd_HHmm}.csv"
                };

                if (dialog.ShowDialog() != true)
                    return;

                CsvExportHelper.WriteDictionaryRows(dialog.FileName, _part2LastReportRows);
                StatusText.Text = $"Экспорт выполнен: {dialog.FileName}";
                MessageBox.Show("Файл сохранён.", "Часть 2",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка экспорта CSV части 2");
                MessageBox.Show($"Ошибка экспорта: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void BindDictionariesToGrid(DataGrid grid, IReadOnlyList<Dictionary<string, object>> data)
        {
            grid.ItemsSource = null;
            grid.Columns.Clear();
            var table = new System.Data.DataTable();
            if (data.Count == 0)
            {
                return;
            }

            foreach (var key in data[0].Keys)
                table.Columns.Add(key, typeof(object));

            foreach (var row in data)
            {
                var dr = table.NewRow();
                foreach (var col in table.Columns.Cast<System.Data.DataColumn>())
                {
                    dr[col.ColumnName] = row.TryGetValue(col.ColumnName, out var v) ? v ?? DBNull.Value : DBNull.Value;
                }

                table.Rows.Add(dr);
            }

            grid.ItemsSource = table.DefaultView;
        }

    }

    /// <summary>
    /// Информация о таблице
    /// </summary>
    public class TableInfo
    {
        public string TableName { get; set; } = string.Empty;
        public int RowCount { get; set; }
    }
}
