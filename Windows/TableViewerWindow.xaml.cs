#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Services;

namespace SoftwareProductsManager.Windows
{
    /// <summary>
    /// Окно для просмотра всех таблиц в базе данных
    /// </summary>
    public partial class TableViewerWindow : Window
    {
        private readonly TableViewerService _tableViewerService;
        private readonly ILogger<TableViewerWindow> _logger;
        private readonly ObservableCollection<TableInfo> _tables = new();

        public TableViewerWindow(TableViewerService tableViewerService, ILogger<TableViewerWindow> logger)
        {
            InitializeComponent();
            _tableViewerService = tableViewerService;
            _logger = logger;
            
            TablesListBox.ItemsSource = _tables;
            Loaded += TableViewerWindow_Loaded;
        }

        private async void TableViewerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                StatusText.Text = "Загрузка таблиц...";
                await LoadTablesAsync();
                StatusText.Text = $"Загружено {_tables.Count} таблиц";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке таблиц");
                MessageBox.Show($"Ошибка при загрузке таблиц: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка загрузки таблиц";
            }
        }

        /// <summary>
        /// Загрузка списка таблиц
        /// </summary>
        private async Task LoadTablesAsync()
        {
            try
            {
                var tableNames = await _tableViewerService.GetAllTablesAsync();
                _tables.Clear();

                foreach (var tableName in tableNames)
                {
                    var rowCount = await _tableViewerService.GetTableRowCountAsync(tableName);
                    _tables.Add(new TableInfo { TableName = tableName, RowCount = rowCount });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при загрузке списка таблиц");
                throw;
            }
        }

        /// <summary>
        /// Обработчик выбора таблицы
        /// </summary>
        private async void TablesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedTable = TablesListBox.SelectedItem as TableInfo;
            if (selectedTable == null)
            {
                SelectedTableName.Text = "Выберите таблицу";
                SelectedTableCount.Text = "";
                TableDataGrid.ItemsSource = null;
                return;
            }

            try
            {
                StatusText.Text = $"Загрузка данных из таблицы {selectedTable.TableName}...";
                
                SelectedTableName.Text = selectedTable.TableName;
                SelectedTableCount.Text = $"({selectedTable.RowCount} записей)";

                var data = await _tableViewerService.GetTableDataAsync(selectedTable.TableName);
                
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

                TableDataGrid.ItemsSource = dataTable.DefaultView;
                StatusText.Text = $"Загружено {data.Count} записей из таблицы {selectedTable.TableName}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при загрузке данных из таблицы {selectedTable.TableName}");
                MessageBox.Show($"Ошибка при загрузке данных: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка загрузки данных";
            }
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
