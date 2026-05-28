#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoftwareProductsManager.Models;
using SoftwareProductsManager.Services;

namespace SoftwareProductsManager.Windows
{
    /// <summary>
    /// Окно для добавления/редактирования программного продукта
    /// </summary>
    public partial class ProductEditWindow : Window
    {
        private readonly SoftwareProductService _productService;
        private readonly ILogger<ProductEditWindow> _logger;
        private readonly AuthenticationService _authService;
        private SoftwareProduct? _product;

        public ProductEditWindow(SoftwareProductService productService, ILogger<ProductEditWindow> logger, 
            AuthenticationService authService)
        {
            InitializeComponent();
            _productService = productService;
            _logger = logger;
            _authService = authService;
        }

        /// <summary>
        /// Установка продукта для редактирования (null для создания нового)
        /// </summary>
        public void SetProduct(SoftwareProduct? product)
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

            _product = product;
            
            if (product == null)
            {
                WindowTitle.Text = "Добавление карточки компонента";
                Title = "Добавление карточки компонента";
                ClearForm();
            }
            else
            {
                WindowTitle.Text = "Редактирование карточки компонента";
                Title = "Редактирование карточки компонента";
                FillForm(product);
            }
        }

        /// <summary>
        /// Заполнение формы данными продукта
        /// </summary>
        private void FillForm(SoftwareProduct product)
        {
            NameTextBox.Text = product.Name;
            DescriptionTextBox.Text = product.Description;
            VersionTextBox.Text = product.Version;
            DeveloperTextBox.Text = product.Developer;
            CategoryComboBox.Text = product.Category;
            PriceTextBox.Text = product.Price.ToString();
            ReleaseDatePicker.SelectedDate = product.ReleaseDate;
            LicenseComboBox.Text = product.License;
            SystemRequirementsTextBox.Text = product.SystemRequirements;
            IsActiveCheckBox.IsChecked = product.IsActive;
        }

        /// <summary>
        /// Очистка формы
        /// </summary>
        private void ClearForm()
        {
            NameTextBox.Text = string.Empty;
            DescriptionTextBox.Text = string.Empty;
            VersionTextBox.Text = string.Empty;
            DeveloperTextBox.Text = string.Empty;
            CategoryComboBox.Text = string.Empty;
            PriceTextBox.Text = string.Empty;
            ReleaseDatePicker.SelectedDate = DateTime.Today;
            LicenseComboBox.Text = string.Empty;
            SystemRequirementsTextBox.Text = string.Empty;
            IsActiveCheckBox.IsChecked = true;
        }

        /// <summary>
        /// Валидация формы
        /// </summary>
        private bool ValidateForm()
        {
            if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            {
                MessageBox.Show("Наименование компонента обязательно для заполнения", "Ошибка валидации", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                NameTextBox.Focus();
                return false;
            }

            if (!decimal.TryParse(PriceTextBox.Text, out _) && !string.IsNullOrWhiteSpace(PriceTextBox.Text))
            {
                MessageBox.Show("Цена должна быть числом", "Ошибка валидации", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                PriceTextBox.Focus();
                return false;
            }

            if (ReleaseDatePicker.SelectedDate == null)
            {
                MessageBox.Show("Выберите дату добавления", "Ошибка валидации", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ReleaseDatePicker.Focus();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Получение данных продукта из формы
        /// </summary>
        private SoftwareProduct GetProductFromForm()
        {
            var product = new SoftwareProduct
            {
                Name = NameTextBox.Text.Trim(),
                Description = DescriptionTextBox.Text.Trim(),
                Version = VersionTextBox.Text.Trim(),
                Developer = DeveloperTextBox.Text.Trim(),
                Category = CategoryComboBox.Text.Trim(),
                Price = decimal.TryParse(PriceTextBox.Text, out var price) ? price : 0,
                ReleaseDate = ReleaseDatePicker.SelectedDate ?? DateTime.Today,
                License = LicenseComboBox.Text.Trim(),
                SystemRequirements = SystemRequirementsTextBox.Text.Trim(),
                IsActive = IsActiveCheckBox.IsChecked ?? true
            };

            if (_product != null)
            {
                product.Id = _product.Id;
                product.CreatedDate = _product.CreatedDate;
            }

            return product;
        }

        /// <summary>
        /// Сохранение продукта
        /// </summary>
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateForm())
                return;

            try
            {
                var product = GetProductFromForm();
                bool success;

                if (_product == null)
                {
                    // Добавление нового компонента
                    var id = await _productService.AddProductAsync(product);
                    success = id > 0;
                    
                    if (success)
                    {
                        MessageBox.Show("Компонент успешно добавлен", "Успех", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    // Обновление существующего компонента
                    success = await _productService.UpdateProductAsync(product);
                    
                    if (success)
                    {
                        MessageBox.Show("Компонент успешно обновлен", "Успех", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                if (success)
                {
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Не удалось сохранить компонент", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сохранении компонента");
                MessageBox.Show($"Ошибка при сохранении: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Отмена редактирования
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
