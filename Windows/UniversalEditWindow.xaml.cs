#nullable enable
using System;
using System.Collections.Generic;
using System.Data.OleDb;
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
    /// Универсальное окно для редактирования записей любой таблицы
    /// </summary>
    public partial class UniversalEditWindow : Window
    {
        private readonly UniversalCrudService _crudService;
        private readonly ILogger<UniversalEditWindow> _logger;
        private readonly AuthenticationService _authService;
        private readonly string _tableName;
        private readonly bool _isEditMode = false; // Отключено редактирование
        
        private readonly Dictionary<string, Control> _fieldControls = new();
        private readonly Dictionary<string, ColumnInfo> _columns = new();
        private readonly bool _autoAssignSequentialIds = false; // Пользователь сам проставляет ID

        private readonly object? _primaryKeyValue;
        private readonly string _primaryKeyColumn;

        public UniversalEditWindow(UniversalCrudService crudService, ILogger<UniversalEditWindow> logger, 
            AuthenticationService authService, string tableName)
        {
            InitializeComponent();
            _crudService = crudService;
            _logger = logger;
            _authService = authService;
            _tableName = tableName;
            _isEditMode = false;
            _primaryKeyValue = null;
            _primaryKeyColumn = string.Empty;
            
            Loaded += UniversalEditWindow_Loaded;
        }

        public UniversalEditWindow(UniversalCrudService crudService, ILogger<UniversalEditWindow> logger, 
            AuthenticationService authService, string tableName, object primaryKeyValue, string primaryKeyColumn)
        {
            InitializeComponent();
            _crudService = crudService;
            _logger = logger;
            _authService = authService;
            _tableName = tableName;
            _isEditMode = true;
            _primaryKeyValue = primaryKeyValue;
            _primaryKeyColumn = primaryKeyColumn;
            
            Loaded += UniversalEditWindow_Loaded;
        }

        private async void UniversalEditWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Проверяем права доступа
                if (!_authService.IsAdmin)
                {
                    MessageBox.Show("У вас нет прав для редактирования записей. Обратитесь к администратору.", 
                        "Доступ запрещен", MessageBoxButton.OK, MessageBoxImage.Warning);
                    DialogResult = false;
                    Close();
                    return;
                }

                if (_isEditMode)
                {
                    WindowTitle.Text = $"Редактирование записи в таблице {_tableName}";
                    SaveButton.Content = "💾 Сохранить изменения";
                    
                    await LoadTableStructureAsync();
                    await LoadRecordDataAsync();
                }
                else
                {
                    WindowTitle.Text = $"Добавление записи в таблицу {_tableName}";
                    SaveButton.Content = "➕ Добавить запись";
                    
                    await LoadTableStructureAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при загрузке окна редактирования для таблицы {_tableName}");
                MessageBox.Show($"Ошибка при загрузке: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        /// <summary>
        /// Загрузка данных записи для редактирования
        /// </summary>
        private async Task LoadRecordDataAsync()
        {
            try
            {
                if (_primaryKeyValue == null || string.IsNullOrEmpty(_primaryKeyColumn))
                {
                    _logger.LogError("Не удалось определить первичный ключ для загрузки записи");
                    return;
                }

                var record = await _crudService.GetRecordByIdAsync(_tableName, _primaryKeyColumn, _primaryKeyValue);
                if (record == null)
                {
                    _logger.LogError($"Не удалось загрузить запись с ID {_primaryKeyValue}");
                    MessageBox.Show("Запись не найдена", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }

                // Устанавливаем значения в контролы
                foreach (var kvp in record)
                {
                    if (_fieldControls.ContainsKey(kvp.Key))
                    {
                        var control = _fieldControls[kvp.Key];
                        SetControlValue(control, kvp.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при загрузке данных записи для редактирования");
                MessageBox.Show($"Ошибка при загрузке данных: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        /// <summary>
        /// Загрузка структуры таблицы
        /// </summary>
        private async Task LoadTableStructureAsync()
        {
            try
            {
                _columns.Clear();
                var columns = await _crudService.GetTableStructureAsync(_tableName);
                
                    foreach (var column in columns)
                    {
                        if (!string.IsNullOrEmpty(column.Name))
                        {
                            _columns[column.Name] = column;
                            await CreateFieldControl(column);
                        }
                    }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при загрузке структуры таблицы {_tableName}");
                throw;
            }
        }

        /// <summary>
        /// Создание контрола для поля
        /// </summary>
        private async Task CreateFieldControl(ColumnInfo column)
        {
            // Пропускаем автоинкрементные поля при добавлении
            if (!_isEditMode && column.IsAutoIncrement) return;
            
            // Пропускаем автоинкрементные первичные ключи
            if (column.IsAutoIncrement) return;

            // Список полей, которые нельзя редактировать в таблице Users
            var readOnlyFields = new HashSet<string> { "Id", "PasswordHash", "Username", "CreatedDate", "LastLoginDate", "UpdatedDate" };
            
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            
            // Заголовок поля
            var label = new TextBlock
            {
                Text = column.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 5)
            };
            panel.Children.Add(label);

            // Контрол ввода - улучшенная логика
            Control inputControl = await CreateAppropriateControl(column);

            // Делаем поле только для чтения, если оно в списке запрещенных и редактируем таблицу Users
            if (_tableName == "Users" && readOnlyFields.Contains(column.Name))
            {
                if (inputControl is TextBox textBox)
                {
                    textBox.IsReadOnly = true;
                    textBox.Background = System.Windows.Media.Brushes.LightGray;
                    textBox.Foreground = System.Windows.Media.Brushes.DarkGray;
                }
                else if (inputControl is DatePicker datePicker)
                {
                    datePicker.IsEnabled = false;
                    datePicker.Background = System.Windows.Media.Brushes.LightGray;
                    datePicker.Foreground = System.Windows.Media.Brushes.DarkGray;
                }
            }

            _fieldControls[column.Name] = inputControl;
            panel.Children.Add(inputControl);
            
            FieldsPanel.Children.Add(panel);
        }

        /// <summary>
        /// Создание подходящего контрола для поля
        /// </summary>
        private async Task<Control> CreateAppropriateControl(ColumnInfo column)
        {
            var dataType = column.DataType.ToUpper();
            var columnName = column.Name.ToLower();
            // Спец. правило: ProductID/DishID/CompositionID назначаются автоматически и недоступны пользователю
            if (column.Name.Equals("ProductID", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("DishID", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("CompositionID", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation($"Создаем авто-ID для {column.Name}");
                var tbAuto = CreateTextBox(column, false);
                tbAuto.IsReadOnly = true;
                tbAuto.IsEnabled = false;
                tbAuto.Background = System.Windows.Media.Brushes.LightGray;
                tbAuto.Foreground = System.Windows.Media.Brushes.DarkGray;
                tbAuto.Cursor = System.Windows.Input.Cursors.No;

                if (!_isEditMode)
                {
                    var nextValPid = await _crudService.GetNextSequentialIntAsync(_tableName, column.Name);
                    if (nextValPid.HasValue)
                    {
                        tbAuto.Text = nextValPid.Value.ToString();
                    }
                }
                return tbAuto;
            }
            // Поле заметок по обработке — свободный многострочный ввод
            if (columnName == "processingnotes")
            {
                _logger.LogInformation($"Создаем многострочное поле для: {column.Name}");
                return CreateTextBox(column, true);
            }

            // Специальная обработка: числовые поля для порций/веса — пользователь вводит сам
            if (columnName == "portionsperrecipe" || columnName == "portionweightgrams")
            {
                _logger.LogInformation($"Создаем редактируемое числовое поле для: {column.Name}");
                return CreateNumericTextBox(column);
            }

            // Специальная обработка: числовые поля для цены и калорийности
            if (columnName == "priceperunit" || columnName == "caloriesper100units")
            {
                _logger.LogInformation($"Создаем числовое поле для: {column.Name}");
                return CreateNumericTextBox(column);
            }

            
            _logger.LogInformation($"Обрабатываем поле: {column.Name} (columnName: {columnName})");
            
            // РАННЯЯ ПРОВЕРКА для полей с кодами - если поле содержит "код", сразу обрабатываем
            if (_autoAssignSequentialIds && (columnName.Contains("код") || column.Name.Contains("Код") || column.Name.Contains("код")))
            {
                _logger.LogInformation($"🎯 РАННЯЯ ОБРАБОТКА: Поле {column.Name} содержит 'код' - создаем последовательное ID поле");
                
                var tb = CreateTextBox(column, false);
                tb.IsReadOnly = true;
                tb.IsEnabled = false;
                tb.Background = System.Windows.Media.Brushes.LightGray;
                tb.Foreground = System.Windows.Media.Brushes.DarkGray;
                tb.Cursor = System.Windows.Input.Cursors.No;
                
                // Добавляем обработчики событий
                tb.PreviewKeyDown += (sender, e) => e.Handled = true;
                tb.PreviewTextInput += (sender, e) => e.Handled = true;
                tb.PreviewMouseDown += (sender, e) => e.Handled = true;
                
                // Получаем следующее значение
                var nextVal = await _crudService.GetNextSequentialIntAsync(_tableName, column.Name);
                if (nextVal.HasValue)
                {
                    tb.Text = nextVal.Value.ToString();
                    _logger.LogInformation($"✅ Ранняя обработка: установлено значение {nextVal.Value} для поля {column.Name}");
                }
                else
                {
                    _logger.LogWarning($"❌ Ранняя обработка: не удалось получить значение для поля {column.Name}");
                }
                
                return tb;
            }
            
            // Дополнительное логирование для полей с кодами
            if (columnName.Contains("код") || column.Name.Contains("Код"))
            {
                _logger.LogInformation($"🔍 Поле с кодом обнаружено: {column.Name}, columnName: {columnName}");
                _logger.LogInformation($"🔍 Проверяем условия для поля {column.Name}:");
                _logger.LogInformation($"🔍 - columnName.StartsWith('код'): {columnName.StartsWith("код")}");
                _logger.LogInformation($"🔍 - columnName.Contains('код'): {columnName.Contains("код")}");
                _logger.LogInformation($"🔍 - columnName.EndsWith('код'): {columnName.EndsWith("код")}");
                _logger.LogInformation($"🔍 - column.Name.Equals('КодУстановки'): {column.Name.Equals("КодУстановки", StringComparison.OrdinalIgnoreCase)}");
                _logger.LogInformation($"🔍 - column.Name.Equals('КодУдаления'): {column.Name.Equals("КодУдаления", StringComparison.OrdinalIgnoreCase)}");
                _logger.LogInformation($"🔍 - column.Name.Equals('КодПрограммы'): {column.Name.Equals("КодПрограммы", StringComparison.OrdinalIgnoreCase)}");
                _logger.LogInformation($"🔍 - column.Name.Equals('КодПользователя'): {column.Name.Equals("КодПользователя", StringComparison.OrdinalIgnoreCase)}");
                _logger.LogInformation($"🔍 - column.Name.Contains('Код'): {column.Name.Contains("Код")}");
                _logger.LogInformation($"🔍 - column.Name.Contains('код'): {column.Name.Contains("код")}");
            }

            // Первичный ключ никогда не должен считаться внешним ключом или выпадающим списком
            // Для PK показываем обычный TextBox (если не автоинкремент — пользователь сможет ввести вручную)
            if (column.IsPrimaryKey)
            {
                return CreateTextBox(column, false);
            }
            
            // Специальная обработка для поля Role в таблице Users
            if (_tableName == "Users" && column.Name.Equals("Role", StringComparison.OrdinalIgnoreCase))
            {
                return CreateRoleComboBox(column);
            }
            
            // Специальная обработка для поля IsActive в таблице Users
            if (_tableName == "Users" && column.Name.Equals("IsActive", StringComparison.OrdinalIgnoreCase))
            {
                return CreateIsActiveComboBox(column);
            }
            
            // Отладочная информация
            _logger.LogInformation($"Создание контрола для поля: {column.Name}, тип: {column.DataType}, название (нижний регистр): {columnName}");
            
            // Сначала проверяем по названию поля (приоритет)
            // Поля даты по названию - любое поле с "дата" или "время" в названии
            if (columnName.Contains("дата") || columnName.Contains("время") || columnName.Contains("date") || columnName.Contains("time"))
            {
                _logger.LogInformation($"Создаем DatePicker для поля: {column.Name} (по названию)");
                return CreateDatePicker(column);
            }
            
            // Булевые поля по названию - расширенный список
            if (columnName.Contains("принят") || columnName.Contains("ярлык") || columnName.Contains("актив") ||
                columnName.Contains("включен") || columnName.Contains("отключен") ||
                columnName.Contains("да") || columnName.Contains("нет") ||
                columnName.Contains("файлыудалены") || columnName.Contains("лицензияосвобождена") ||
                columnName.Contains("реестрочищен") || columnName.Contains("удален") ||
                columnName.Contains("создан") || columnName.Contains("изменен") ||
                columnName.Contains("требуетперезагрузки") || columnName.Contains("требует_перезагрузки") ||
                columnName.Contains("перезагрузка") || columnName.Contains("restart") ||
                columnName.Contains("reboot") || columnName.Contains("перезапуск") ||
                columnName.Contains("требует") || columnName.Contains("нужен") ||
                columnName.Contains("необходим") || columnName.Contains("обязательно") ||
                columnName.Contains("true") || columnName.Contains("false") ||
                columnName.Contains("yes") || columnName.Contains("no") ||
                columnName.Contains("on") || columnName.Contains("off") ||
                columnName.Contains("enabled") || columnName.Contains("disabled") ||
                columnName.Contains("флаг") || columnName.Contains("флажок") ||
                columnName.Contains("галочка") || columnName.Contains("чек") ||
                columnName.Contains("выбран") || columnName.Contains("отмечен"))
            {
                _logger.LogInformation($"Создаем Boolean ComboBox для поля: {column.Name} (по названию)");
                return CreateBooleanComboBox(column);
            }
            
            // Затем проверяем тип данных из базы данных
            // Булевые поля по типу данных — как ComboBox
            if (dataType.Contains("BOOLEAN") || dataType.Contains("YESNO") || dataType.Contains("BIT") || 
                dataType.Contains("LOGICAL") || (dataType.Contains("COUNTER") && columnName.Contains("флаг")))
            {
                return CreateBooleanComboBox(column);
            }
            
            // Поля даты по типу данных
            if (dataType.Contains("DATETIME") || dataType.Contains("DATE") || dataType.Contains("TIME") || 
                dataType.Contains("TIMESTAMP"))
            {
                return CreateDatePicker(column);
            }
            
            // Поля статуса с ограниченными значениями
            if (columnName.Contains("статус") || columnName.Contains("результат") ||
                columnName.Contains("состояние") || columnName.Contains("этап"))
            {
                return CreateStatusComboBox(column);
            }
            
            // Поля с булевыми значениями в виде строк (true/false, да/нет)
            if (dataType.Contains("VARCHAR") || dataType.Contains("TEXT")) 
            {
                // Проверяем, может ли это поле содержать булевые значения
                if (columnName.Contains("статус") || columnName.Contains("результат") ||
                    columnName.Contains("состояние") || columnName.Contains("этап") ||
                    columnName.Contains("принят") || columnName.Contains("актив") ||
                    columnName.Contains("включен") || columnName.Contains("отключен"))
                {
                    return CreateBooleanComboBox(column);
                }
            }
            
            // Поля типа/категории
            if (columnName.Contains("тип") || columnName.Contains("категория") || 
                columnName.Contains("вид") || columnName.Contains("класс"))
            {
                return CreateTypeComboBox(column);
            }
            
            // Поля приоритета
            if (columnName.Contains("приоритет") || columnName.Contains("важность"))
            {
                return CreatePriorityComboBox(column);
            }
            
            // Поля валюты
            if (columnName.Contains("валюта") || columnName.Contains("currency"))
            {
                return CreateCurrencyComboBox(column);
            }
            
            // Поля языка
            if (columnName.Contains("язык") || columnName.Contains("language"))
            {
                return CreateLanguageComboBox(column);
            }
            
            // Поля страны
            if (columnName.Contains("страна") || columnName.Contains("country"))
            {
                return CreateCountryComboBox(column);
            }
            
            // Поля единиц измерения (строго: только столбцы типа UnitOfMeasure/Measure)
            if (IsUnitMeasureField(columnName))
            {
                return CreateUnitComboBox(column);
            }
            
            // Поля с идентификаторами, которые идут строго по порядку (без выбора): все поля с "код"
            bool isSequentialIdField = false;
            
            // УНИВЕРСАЛЬНАЯ ПРОВЕРКА для ВСЕХ полей с "код" - приоритетная обработка
            if (column.Name.Contains("Код") || column.Name.Contains("код") || 
                columnName.Contains("код") || columnName.StartsWith("код") || 
                columnName.EndsWith("код"))
            {
                isSequentialIdField = true;
                _logger.LogInformation($"🎯 УНИВЕРСАЛЬНАЯ ОБРАБОТКА: Поле {column.Name} определено как последовательное ID (содержит 'код')");
            }
            
            // Проверяем по названию поля (точное совпадение)
            if (!isSequentialIdField && (column.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("КодУстановки", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("Код_Установки", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("КодУдаления", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("Код_Удаления", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("КодПрограммы", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("Код_Программы", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("КодПользователя", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("Код_Пользователя", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("КодОбновления", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("Код_Обновления", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("КодЛицензии", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("Код_Лицензии", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("КодЗаписи", StringComparison.OrdinalIgnoreCase) ||
                column.Name.Equals("Код_Записи", StringComparison.OrdinalIgnoreCase)))
            {
                isSequentialIdField = true;
                _logger.LogInformation($"✅ Поле {column.Name} определено как последовательное ID по точному совпадению");
            }
            
            // Дополнительная проверка по содержимому названия
            if (!isSequentialIdField && (columnName.StartsWith("код") || columnName.Contains("код") || columnName.EndsWith("код")))
            {
                isSequentialIdField = true;
                _logger.LogInformation($"✅ Поле {column.Name} определено как последовательное ID по содержимому названия");
            }
            
            // Принудительная проверка для всех полей с "код" в любом регистре
            if (!isSequentialIdField && (column.Name.Contains("Код") || column.Name.Contains("код")))
            {
                isSequentialIdField = true;
                _logger.LogInformation($"🔍 Принудительно определено как последовательное ID поле: {column.Name}");
            }
                
            if (_autoAssignSequentialIds && isSequentialIdField)
            {
                _logger.LogInformation($"✅ Создаем последовательное ID поле для: {column.Name} (columnName: {columnName})");
                var tb = CreateTextBox(column, false);
                
                // Устанавливаем все необходимые свойства для только чтения
                tb.IsReadOnly = true;
                tb.IsEnabled = false; // Полностью отключаем поле
                tb.Background = System.Windows.Media.Brushes.LightGray; // Визуально показываем, что поле недоступно
                tb.Foreground = System.Windows.Media.Brushes.DarkGray;
                tb.Cursor = System.Windows.Input.Cursors.No; // Курсор "запрещено"
                
                // Добавляем обработчики событий для предотвращения редактирования
                tb.PreviewKeyDown += (sender, e) => {
                    e.Handled = true; // Блокируем все нажатия клавиш
                };
                tb.PreviewTextInput += (sender, e) => {
                    e.Handled = true; // Блокируем ввод текста
                };
                tb.PreviewMouseDown += (sender, e) => {
                    e.Handled = true; // Блокируем клики мыши
                };
                
                _logger.LogInformation($"✅ Поле {column.Name} установлено как только для чтения (IsReadOnly = true, IsEnabled = false)");
                
                // Всегда подставляем следующее число
                var nextVal = await _crudService.GetNextSequentialIntAsync(_tableName, column.Name);
                if (nextVal.HasValue)
                {
                    tb.Text = nextVal.Value.ToString();
                    _logger.LogInformation($"✅ Установлено значение {nextVal.Value} для поля {column.Name}");
                }
                else
                {
                    _logger.LogWarning($"❌ Не удалось получить следующее значение для поля {column.Name}");
                }
                return tb;
            }
            else
            {
                _logger.LogInformation($"❌ Поле {column.Name} НЕ определено как последовательное ID поле");
                _logger.LogInformation($"❌ Финальная проверка: isSequentialIdField = {isSequentialIdField}");
            }

            // Поля с внешними ключами (включая разные варианты написания id) - создаем ComboBox с опциями
            // Исключаем поля с кодами, которые должны быть последовательными ID
            if ((columnName.EndsWith("id") || columnName.Contains("programid") || columnName.Contains("productid") || columnName.Contains("softwareid")) &&
                !columnName.Contains("код") && !column.Name.Contains("Код"))
            {
                _logger.LogInformation($"🔗 Создаем ComboBox для внешнего ключа: {column.Name}");
                return await CreateForeignKeyComboBox(column);
            }
            
            // Поля с кодами (должны быть TextBox, а не CheckBox)
            // Исключаем поля, которые уже обработаны как последовательные ID
            if ((columnName.Contains("код") || columnName.Contains("code") ||
                columnName.Contains("id") || columnName.Contains("номер") ||
                columnName.Contains("number") || columnName.Contains("кодудаления")) &&
                !isSequentialIdField)
            {
                _logger.LogInformation($"📝 Создаем обычный TextBox для поля: {column.Name} (НЕ последовательное ID)");
                return CreateTextBox(column, false);
            }
            
            // Дополнительная проверка - если поле содержит "код" и не было обработано как последовательное ID
            if ((column.Name.Contains("Код") || column.Name.Contains("код")) && !isSequentialIdField)
            {
                _logger.LogWarning($"⚠️ Поле с кодом {column.Name} не было обработано как последовательное ID! Создаем обычный TextBox.");
                return CreateTextBox(column, false);
            }
            
            // ФИНАЛЬНАЯ ПРОВЕРКА для ВСЕХ полей с "код" - если все еще не обработано
            if (_autoAssignSequentialIds && ((column.Name.Contains("Код") || column.Name.Contains("код") || 
                 columnName.Contains("код")) && !isSequentialIdField))
            {
                _logger.LogWarning($"🚨 КРИТИЧЕСКАЯ ОШИБКА: Поле {column.Name} содержит 'код' но не было обработано как последовательное ID!");
                _logger.LogWarning($"🚨 Принудительно создаем последовательное ID поле для {column.Name}");
                
                var tb = CreateTextBox(column, false);
                tb.IsReadOnly = true;
                tb.IsEnabled = false;
                tb.Background = System.Windows.Media.Brushes.LightGray;
                tb.Foreground = System.Windows.Media.Brushes.DarkGray;
                tb.Cursor = System.Windows.Input.Cursors.No;
                
                // Добавляем обработчики событий
                tb.PreviewKeyDown += (sender, e) => e.Handled = true;
                tb.PreviewTextInput += (sender, e) => e.Handled = true;
                tb.PreviewMouseDown += (sender, e) => e.Handled = true;
                
                // Получаем следующее значение
                var nextVal = await _crudService.GetNextSequentialIntAsync(_tableName, column.Name);
                if (nextVal.HasValue)
                {
                    tb.Text = nextVal.Value.ToString();
                    _logger.LogInformation($"✅ Принудительно установлено значение {nextVal.Value} для поля {column.Name}");
                }
                
                return tb;
            }
            
            // Многострочные поля
            if (dataType.Contains("MEMO") || dataType.Contains("LONGTEXT") || 
                columnName.Contains("описание") || columnName.Contains("сообщение") ||
                columnName.Contains("комментарий") || columnName.Contains("примечание"))
            {
                return CreateTextBox(column, true);
            }
            
            // Обычные текстовые поля
            return CreateTextBox(column, false);
        }

        /// <summary>
        /// Создание текстового поля
        /// </summary>
        private TextBox CreateTextBox(ColumnInfo column, bool isMultiline)
        {
            var textBox = new TextBox
            {
                Style = (Style)FindResource("ModernTextBox"),
                MaxLength = isMultiline ? int.MaxValue : 255
            };

            if (isMultiline)
            {
                textBox.AcceptsReturn = true;
                textBox.TextWrapping = TextWrapping.Wrap;
                textBox.Height = 100;
                textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }

            return textBox;
        }

        /// <summary>
        /// Создание числового текстового поля (только цифры, точка/запятая)
        /// </summary>
        private TextBox CreateNumericTextBox(ColumnInfo column)
        {
            var tb = CreateTextBox(column, false);
            tb.PreviewTextInput += (s, e) =>
            {
                // Разрешаем только цифры, точку и запятую
                e.Handled = !char.IsDigit(e.Text.First()) && e.Text != "." && e.Text != ",";
            };
            DataObject.AddPastingHandler(tb, (sender, e) =>
            {
                if (e.DataObject.GetDataPresent(DataFormats.Text))
                {
                    var text = e.DataObject.GetData(DataFormats.Text)?.ToString() ?? string.Empty;
                    if (!text.All(ch => char.IsDigit(ch) || ch == '.' || ch == ','))
                    {
                        e.CancelCommand();
                    }
                }
                else
                {
                    e.CancelCommand();
                }
            });
            return tb;
        }

        /// <summary>
        /// Определяет, относится ли имя столбца к единицам измерения
        /// </summary>
        private bool IsUnitMeasureField(string columnNameLower)
        {
            return columnNameLower == "unitofmeasure"
                   || columnNameLower == "unit_of_measure"
                   || columnNameLower == "unit"
                   || columnNameLower == "units"
                   || columnNameLower == "единицаизмерения"
                   || columnNameLower.Contains("measure");
        }

        /// <summary>
        /// Создание чекбокса
        /// </summary>
        private CheckBox CreateCheckBox(ColumnInfo column)
        {
            return new CheckBox
            {
                Style = (Style)FindResource("ModernCheckBox"),
                IsChecked = false
            };
        }

        /// <summary>
        /// Создание выпадающего списка для статуса
        /// </summary>
        private ComboBox CreateStatusComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();

            // Добавляем стандартные варианты статуса
            var statusOptions = new List<string>
            {
                "Завершена",
                "В процессе", 
                "Ошибка",
                "Отменена",
                "Ожидает",
                "Успешно",
                "Неудачно"
            };

            foreach (var option in statusOptions)
            {
                comboBox.Items.Add(option);
            }

            return comboBox;
        }

        /// <summary>
        /// Создание выпадающего списка для роли пользователя
        /// </summary>
        private ComboBox CreateRoleComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();

            // Добавляем варианты ролей
            comboBox.Items.Add("Admin");
            comboBox.Items.Add("User");

            return comboBox;
        }

        /// <summary>
        /// Создание выпадающего списка для активности пользователя
        /// </summary>
        private ComboBox CreateIsActiveComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();

            // Добавляем варианты активности
            comboBox.Items.Add("True");
            comboBox.Items.Add("False");

            return comboBox;
        }

        /// <summary>
        /// Создание ComboBox для типа/категории (в т.ч. тип клиента в платёжной системе)
        /// </summary>
        private ComboBox CreateTypeComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();
            
            var typeOptions = new List<string>
            {
                "Физическое лицо",
                "Индивидуальный предприниматель",
                "Юридическое лицо",
                "Банк",
                "Небанковская кредитная организация",
                "Платёжная система",
                "Платёжный агент / субагент",
                "Торговая организация (мерчант)",
                "Финтех-компания",
                "Государственный заказчик",
                "Некоммерческая организация",
                "Другое"
            };

            foreach (var option in typeOptions)
            {
                comboBox.Items.Add(option);
            }

            return comboBox;
        }

        /// <summary>
        /// Создание ComboBox для приоритета
        /// </summary>
        private ComboBox CreatePriorityComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();
            
            var priorityOptions = new List<string>
            {
                "Низкий",
                "Средний", 
                "Высокий",
                "Критический",
                "Обычный"
            };

            foreach (var option in priorityOptions)
            {
                comboBox.Items.Add(option);
            }

            return comboBox;
        }

        /// <summary>
        /// Создание ComboBox для валюты
        /// </summary>
        private ComboBox CreateCurrencyComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();
            
            var currencyOptions = new List<string>
            {
                "RUB (Российский рубль)",
                "USD (Доллар США)",
                "EUR (Евро)",
                "GBP (Британский фунт)",
                "JPY (Японская иена)",
                "CNY (Китайский юань)",
                "KZT (Казахстанский тенге)",
                "BYN (Белорусский рубль)"
            };

            foreach (var option in currencyOptions)
            {
                comboBox.Items.Add(option);
            }

            return comboBox;
        }

        /// <summary>
        /// Создание ComboBox для языка
        /// </summary>
        private ComboBox CreateLanguageComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();
            
            var languageOptions = new List<string>
            {
                "Русский",
                "English",
                "中文 (Китайский)",
                "日本語 (Японский)",
                "한국어 (Корейский)",
                "Español (Испанский)",
                "Français (Французский)",
                "Deutsch (Немецкий)",
                "Italiano (Итальянский)",
                "Português (Португальский)",
                "العربية (Арабский)",
                "हिन्दी (Хинди)"
            };

            foreach (var option in languageOptions)
            {
                comboBox.Items.Add(option);
            }

            return comboBox;
        }

        /// <summary>
        /// Создание ComboBox для страны
        /// </summary>
        private ComboBox CreateCountryComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();
            
            var countryOptions = new List<string>
            {
                "Россия",
                "США",
                "Китай",
                "Япония",
                "Германия",
                "Великобритания",
                "Франция",
                "Италия",
                "Испания",
                "Канада",
                "Австралия",
                "Бразилия",
                "Индия",
                "Южная Корея",
                "Нидерланды",
                "Швеция",
                "Норвегия",
                "Финляндия",
                "Польша",
                "Чехия"
            };

            foreach (var option in countryOptions)
            {
                comboBox.Items.Add(option);
            }

            return comboBox;
        }

        /// <summary>
        /// Создание ComboBox для единиц измерения
        /// </summary>
        private ComboBox CreateUnitComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();
            
            var unitOptions = new List<string>
            {
                "шт (штука)",
                "кг (килограмм)",
                "г (грамм)",
                "л (литр)",
                "мл (миллилитр)"
            };

            foreach (var option in unitOptions)
            {
                comboBox.Items.Add(option);
            }

            return comboBox;
        }

        /// <summary>
        /// Создание ComboBox для булевых значений
        /// </summary>
        private ComboBox CreateBooleanComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();
            
            var booleanOptions = new List<string>
            {
                "да",
                "нет"
            };

            foreach (var option in booleanOptions)
            {
                comboBox.Items.Add(option);
            }

            return comboBox;
        }

        /// <summary>
        /// Создание базового ComboBox с общими настройками
        /// </summary>
        private ComboBox CreateBaseComboBox()
        {
            return new ComboBox
            {
                IsEditable = false,
                Padding = new Thickness(10, 8, 10, 8),
                BorderThickness = new Thickness(1),
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                Background = System.Windows.Media.Brushes.White,
                FontSize = 14
            };
        }

        private async Task<ComboBox> CreateForeignKeyComboBox(ColumnInfo column)
        {
            var comboBox = CreateBaseComboBox();
            
            try
            {
                var options = await _crudService.GetForeignKeyOptionsAsync(_tableName, column.Name);
                
                // Сначала добавляем пустое значение, чтобы можно было явно выбрать "ничего"
                comboBox.Items.Clear();
                comboBox.Items.Add(new ComboBoxItem { Content = "— не выбрано —", Tag = null });

                foreach (var option in options)
                {
                    // Показываем как "ID — Название" для наглядности числового кода
                    var displayText = $"{option.Key} — {option.Value}";
                    comboBox.Items.Add(new ComboBoxItem 
                    { 
                        Content = displayText, 
                        Tag = option.Key 
                    });
                }
                
                if (options.Any())
                {
                    comboBox.SelectedIndex = 0; // по умолчанию — пустое значение
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при загрузке опций для внешнего ключа {column.Name}");
                // Добавляем пустой элемент в случае ошибки
                comboBox.Items.Add(new ComboBoxItem { Content = "Ошибка загрузки", Tag = null });
            }
            
            return comboBox;
        }

        /// <summary>
        /// Создание выбора даты
        /// </summary>
        private DatePicker CreateDatePicker(ColumnInfo column)
        {
            return new DatePicker
            {
                Style = (Style)FindResource("ModernDatePicker"),
                SelectedDate = DateTime.Today
            };
        }



        /// <summary>
        /// Установить значение в контрол
        /// </summary>
        private void SetControlValue(Control control, object? value)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.Text = value?.ToString() ?? string.Empty;
                    break;
                case CheckBox checkBox:
                    // Обрабатываем различные типы булевых значений
                    if (value is bool boolValue)
                    {
                        checkBox.IsChecked = boolValue;
                    }
                    else if (value is int intValue)
                    {
                        checkBox.IsChecked = intValue != 0;
                    }
                    else if (value is string stringBoolValue)
                    {
                        checkBox.IsChecked = stringBoolValue.ToLower() switch
                        {
                            "true" or "1" or "да" or "yes" or "on" => true,
                            "false" or "0" or "нет" or "no" or "off" => false,
                            _ => false
                        };
                    }
                    else
                    {
                        checkBox.IsChecked = false;
                    }
                    break;
                case DatePicker datePicker:
                    if (value is DateTime dateValue)
                    {
                        datePicker.SelectedDate = dateValue;
                    }
                    else if (value is string dateString && DateTime.TryParse(dateString, out var parsedDate))
                    {
                        datePicker.SelectedDate = parsedDate;
                    }
                    break;
                    case ComboBox comboBox:
                        var stringValue = value?.ToString() ?? string.Empty;
                        // Ищем элемент с соответствующим Tag или Content, учитывая разные типы элементов
                        foreach (var obj in comboBox.Items)
                        {
                            if (obj is ComboBoxItem item)
                            {
                                var tagString = item.Tag?.ToString();
                                var contentString = item.Content?.ToString();
                                if (tagString == stringValue || contentString == stringValue)
                                {
                                    comboBox.SelectedItem = item;
                                    return;
                                }

                                // Также сравним по числовому значению, если возможно
                                if (value is int intVal && int.TryParse(tagString, out var tagInt) && tagInt == intVal)
                                {
                                    comboBox.SelectedItem = item;
                                    return;
                                }
                            }
                            else if (obj is string s)
                            {
                                if (s == stringValue)
                                {
                                    comboBox.SelectedItem = s;
                                    return;
                                }
                            }
                        }
                        // Если не найден, устанавливаем по строке
                        comboBox.SelectedItem = stringValue;
                        break;
            }
        }

        /// <summary>
        /// Преобразовать тип данных Access в OleDbType
        /// </summary>
        private OleDbType GetOleDbType(string dataType)
        {
            return dataType?.ToUpper() switch
            {
                "INTEGER" or "COUNTER" => OleDbType.Integer,
                "VARCHAR" or "TEXT" => OleDbType.VarChar,
                "MEMO" or "LONGTEXT" => OleDbType.LongVarChar,
                "CURRENCY" or "MONEY" => OleDbType.Currency,
                "DATETIME" or "DATE" or "TIME" => OleDbType.Date,
                "BOOLEAN" or "YESNO" or "BIT" => OleDbType.Boolean,
                "DOUBLE" or "FLOAT" or "SINGLE" => OleDbType.Double,
                "DECIMAL" or "NUMERIC" => OleDbType.Decimal,
                _ => OleDbType.VarChar
            };
        }

        /// <summary>
        /// Сохранение записи
        /// </summary>
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Валидация полей
                var validationResult = ValidateFields();
                if (!validationResult.IsValid)
                {
                    MessageBox.Show($"Ошибки валидации:\n{string.Join("\n", validationResult.Errors)}", 
                        "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var values = GetFieldValues();
                
                if (_isEditMode)
                {
                    // Редактирование существующей записи
                    if (_primaryKeyValue == null || string.IsNullOrEmpty(_primaryKeyColumn))
                    {
                        MessageBox.Show("Не удалось определить ID записи для редактирования", "Ошибка", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var success = await _crudService.UpdateRecordAsync(_tableName, values, _primaryKeyColumn, _primaryKeyValue);
                    if (success)
                    {
                        MessageBox.Show("Запись успешно обновлена!", "Успех", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        DialogResult = true;
                        Close();
                    }
                    else
                    {
                        MessageBox.Show("Не удалось обновить запись", "Ошибка", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    // Добавление новой записи
                    var newId = await _crudService.InsertRecordAsync(_tableName, values);
                    MessageBox.Show($"Запись успешно добавлена! ID: {newId}", "Успех", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при сохранении записи в таблицу {_tableName}");
                MessageBox.Show($"Ошибка при сохранении: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Получение значений полей
        /// </summary>
        private Dictionary<string, object> GetFieldValues()
        {
            var values = new Dictionary<string, object>();

            foreach (var kvp in _fieldControls)
            {
                var fieldName = kvp.Key;
                var control = kvp.Value;
                var column = _columns[fieldName];

                object? value = control switch
                {
                    TextBox textBox => string.IsNullOrWhiteSpace(textBox.Text) ? null : textBox.Text,
                    CheckBox checkBox => checkBox.IsChecked ?? false, // Всегда возвращаем bool для CheckBox
                    DatePicker datePicker => datePicker.SelectedDate,
                    ComboBox comboBox => comboBox.SelectedItem switch
                    {
                        ComboBoxItem item => item.Tag,
                        _ => comboBox.SelectedItem?.ToString()
                    },
                    _ => null
                };

                if (value != null && !IsEmptyValue(value))
                {
                    values[fieldName] = value;
                }
            }

            return values;
        }

        /// <summary>
        /// Проверить, является ли значение пустым
        /// </summary>
        private bool IsEmptyValue(object value)
        {
            return value switch
            {
                null => true,
                string str => string.IsNullOrWhiteSpace(str),
                bool => false, // Булевые значения всегда валидны
                DateTime date => date == DateTime.MinValue,
                _ => false
            };
        }

        /// <summary>
        /// Отмена редактирования
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Валидация полей
        /// </summary>
        private ValidationResult ValidateFields()
        {
            var result = new ValidationResult();
            
            foreach (var kvp in _fieldControls)
            {
                var fieldName = kvp.Key;
                var control = kvp.Value;
                var column = _columns[fieldName];

                // Проверяем обязательные поля
                if (!column.IsNullable && !column.IsAutoIncrement)
                {
                    var isEmpty = control switch
                    {
                        TextBox textBox => string.IsNullOrWhiteSpace(textBox.Text),
                        CheckBox checkBox => !checkBox.IsChecked.HasValue,
                        DatePicker datePicker => !datePicker.SelectedDate.HasValue,
                        ComboBox comboBox => comboBox.SelectedItem == null,
                        _ => true
                    };

                    if (isEmpty)
                    {
                        result.Errors.Add($"Поле '{fieldName}' обязательно для заполнения");
                    }
                }

                // Дополнительная валидация для текстовых полей
                if (control is TextBox textBoxControl && !string.IsNullOrWhiteSpace(textBoxControl.Text))
                {
                    var text = textBoxControl.Text.Trim();
                    
                    // Проверка длины для VARCHAR полей
                    if (column.DataType.ToUpper().Contains("VARCHAR") && text.Length > 255)
                    {
                        result.Errors.Add($"Поле '{fieldName}' не может содержать более 255 символов");
                    }
                    
                    // Проверка на специальные символы для некоторых полей
                    if (fieldName.ToLower().Contains("email") && !IsValidEmail(text))
                    {
                        result.Errors.Add($"Поле '{fieldName}' должно содержать корректный email адрес");
                    }
                    
                    if (fieldName.ToLower().Contains("phone") && !IsValidPhone(text))
                    {
                        result.Errors.Add($"Поле '{fieldName}' должно содержать корректный номер телефона");
                    }
                }

                // Валидация дат
                if (control is DatePicker datePickerControl && datePickerControl.SelectedDate.HasValue)
                {
                    var date = datePickerControl.SelectedDate.Value;
                    if (date < DateTime.MinValue || date > DateTime.MaxValue)
                    {
                        result.Errors.Add($"Поле '{fieldName}' содержит некорректную дату");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Проверка корректности email
        /// </summary>
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Проверка корректности номера телефона
        /// </summary>
        private bool IsValidPhone(string phone)
        {
            // Простая проверка на наличие цифр и допустимых символов
            var cleanPhone = phone.Replace("+", "").Replace("-", "").Replace("(", "").Replace(")", "").Replace(" ", "");
            return cleanPhone.All(char.IsDigit) && cleanPhone.Length >= 10;
        }
    }

    /// <summary>
    /// Результат валидации
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();
    }
}
