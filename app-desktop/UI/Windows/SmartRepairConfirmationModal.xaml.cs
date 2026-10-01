using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VoltrisOptimizer.UI.Helpers;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.UI.Windows
{
    /// <summary>
    /// Modelo de dados para itens do modal de confirmação
    /// </summary>
    public class ConfirmationModuleItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _moduleName = string.Empty;
        private string _description = string.Empty;
        private string _impactKey = "Medium"; // Chave para tradução
        private string _impactDisplay = "Medium"; // Texto exibido (traduzido)
        private string _status = "Pronto";

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string ModuleName
        {
            get => _moduleName;
            set { _moduleName = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public string ImpactLevel
        {
            get => _impactDisplay;
            set { _impactDisplay = value; OnPropertyChanged(); }
        }

        public string ImpactKey
        {
            get => _impactKey;
            set
            {
                _impactKey = value;
                UpdateImpactTranslation();
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public void UpdateImpactTranslation()
        {
            _impactDisplay = _impactKey switch
            {
                "High" => LocalizationService.Instance.GetString("SmartRepairImpactHigh"),
                "Medium" => LocalizationService.Instance.GetString("SmartRepairImpactMedium"),
                "Low" => LocalizationService.Instance.GetString("SmartRepairImpactLow"),
                _ => LocalizationService.Instance.GetString("SmartRepairImpactMedium")
            };
            OnPropertyChanged(nameof(ImpactLevel));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Resultado do modal de confirmação
    /// </summary>
    public class ConfirmationResult
    {
        public bool Confirmed { get; set; }
        public List<string> SelectedModuleIds { get; set; } = new();
        public int TotalModules { get; set; }
        public long EstimatedSpaceBytes { get; set; }
        public int TotalItems { get; set; }
    }

    public partial class SmartRepairConfirmationModal : Window
    {
        private readonly ObservableCollection<ConfirmationModuleItem> _modules = new();
        private ConfirmationResult? _result;
        private bool _acrylicApplied;

        public SmartRepairConfirmationModal()
        {
            InitializeComponent();
            ModulesList.ItemsSource = _modules;
            _acrylicApplied = false;
            
            // Definir textos iniciais diretamente
            SetInitialTexts();
            
            // Inscrever-se no evento de mudança de idioma
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }
        
        ~SmartRepairConfirmationModal()
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        private void SetInitialTexts()
        {
            try
            {
                // Atualizar título da janela
                Title = LocalizationService.Instance.GetString("SmartRepairConfirmTitle");
                
                // Atualizar título e subtítulo do header
                if (TitleTextBlock != null)
                    TitleTextBlock.Text = LocalizationService.Instance.GetString("SmartRepairConfirmTitle");
                
                if (SubtitleTextBlock != null)
                    SubtitleTextBlock.Text = LocalizationService.Instance.GetString("SmartRepairConfirmSubtitle");
                
                // Debug para verificar se os textos estão sendo carregados
                var cancelText = LocalizationService.Instance.GetString("SmartRepairConfirmCancel");
                var confirmText = LocalizationService.Instance.GetString("SmartRepairConfirmConfirm");
                var alertText = LocalizationService.Instance.GetString("SmartRepairConfirmRestorePointInfo");
                var modulesText = LocalizationService.Instance.GetString("SmartRepairConfirmModules");
                var spaceText = LocalizationService.Instance.GetString("SmartRepairConfirmSpace");
                var itemsText = LocalizationService.Instance.GetString("SmartRepairConfirmItems");
                var operationText = LocalizationService.Instance.GetString("SmartRepairConfirmOperation");
                var impactText = LocalizationService.Instance.GetString("SmartRepairConfirmImpact");
                
                System.Diagnostics.Debug.WriteLine($"[Modal Texts] Title: {Title}");
                System.Diagnostics.Debug.WriteLine($"[Modal Texts] Modules: {modulesText}, Space: {spaceText}, Items: {itemsText}");
                System.Diagnostics.Debug.WriteLine($"[Modal Texts] Operation: {operationText}, Impact: {impactText}");
                
                // Definir textos dos botões
                if (CancelButton != null)
                    CancelButton.Content = cancelText;
                
                if (ConfirmButtonText != null)
                    ConfirmButtonText.Text = confirmText;
                
                // Definir texto do alerta
                if (AlertTextBlock != null)
                    AlertTextBlock.Text = alertText;
                
                // ATUALIZAR LABELS DIRETAMENTE PELO X:NAME
                if (ModulesLabel != null)
                    ModulesLabel.Text = modulesText;
                
                if (SpaceLabel != null)
                    SpaceLabel.Text = spaceText;
                
                if (ItemsLabel != null)
                    ItemsLabel.Text = itemsText;
                
                if (OperationLabel != null)
                    OperationLabel.Text = operationText;
                
                if (ImpactLabel != null)
                    ImpactLabel.Text = impactText;
                
                // Nota de segurança: módulos de alto impacto começam desmarcados
                if (SafetyNoteTextBlock != null)
                    SafetyNoteTextBlock.Text = LocalizationService.Instance.GetString("SmartRepairConfirmSafetyNote");
                
                System.Diagnostics.Debug.WriteLine($"[Modal] Labels set: Modules={ModulesLabel?.Text}, Space={SpaceLabel?.Text}, Items={ItemsLabel?.Text}");
                System.Diagnostics.Debug.WriteLine($"[Modal] Labels set: Operation={OperationLabel?.Text}, Impact={ImpactLabel?.Text}");
                
                // Atualizar módulos existentes
                foreach (var module in _modules)
                {
                    module.UpdateImpactTranslation();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SetInitialTexts Error] {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[SetInitialTexts Error] Stack: {ex.StackTrace}");
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            // Redefinir TODOS os textos quando idioma mudar
            SetInitialTexts();
        }

        private void UpdateTranslations()
        {
            try
            {
                // Atualizar título da janela
                Title = LocalizationService.Instance.GetString("SmartRepairConfirmTitle");
                
                // Atualizar módulos existentes
                foreach (var module in _modules)
                {
                    module.UpdateImpactTranslation();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Translation Error] {ex.Message}");
            }
        }

        /// <summary>
        /// Aplica efeito ACRYLIC profissional (MESMO método do Voltris)
        /// Usa SetWindowCompositionAttribute - NUNCA BlurEffect
        /// </summary>
        private void ApplyProfessionalAcrylic()
        {
            if (_acrylicApplied) return;

            try
            {
                // Usar o MESMO método do Voltris - BackdropHelper.ApplyModernBackdrop
                // Isso usa SetWindowCompositionAttribute com ACCENT_ENABLE_ACRYLICBLURBEHIND
                BackdropHelper.ApplyModernBackdrop(this, BackdropHelper.SystemBackdropType.Acrylic, false);
                
                _acrylicApplied = true;
            }
            catch (Exception ex)
            {
                // Fallback: background sólido se falhar
                AcrylicBorder.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(10, 10, 15));
            }
        }

        /// <summary>
        /// Carrega os módulos para confirmação
        /// </summary>
        public void LoadModules(
            List<(string Id, string Name, string Description, string Impact, bool SelectedByDefault)> modules,
            long estimatedSpaceBytes,
            int totalItems)
        {
            _modules.Clear();

            foreach (var module in modules)
            {
                _modules.Add(new ConfirmationModuleItem
                {
                    ModuleName = module.Name,
                    Description = module.Description,
                    ImpactKey = module.Impact, // Usa a chave para tradução
                    Status = LocalizationService.Instance.GetString("SmartRepairStatusPending"),
                    IsSelected = module.SelectedByDefault
                });
            }

            // Atualizar resumo
            ModuleCountText.Text = modules.Count.ToString();
            SpaceText.Text = FormatBytes(estimatedSpaceBytes);
            ItemsText.Text = totalItems.ToString();

            // Atualizar checkbox Select All
            UpdateSelectAllCheckbox();
            
            // GARANTIR que todos os textos sejam atualizados após carregar módulos
            SetInitialTexts();
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private void UpdateSelectAllCheckbox()
        {
            if (_modules.Count == 0)
            {
                SelectAllCheckbox.IsChecked = false;
                return;
            }

            var allSelected = _modules.All(m => m.IsSelected);
            var noneSelected = _modules.All(m => !m.IsSelected);

            if (allSelected)
                SelectAllCheckbox.IsChecked = true;
            else if (noneSelected)
                SelectAllCheckbox.IsChecked = false;
            else
                SelectAllCheckbox.IsChecked = null; // Estado intermediário
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // APLICAR ACRYLIC PROFISSIONAL (mesmo método do Voltris)
            ApplyProfessionalAcrylic();
            
            // Focar no botão de confirmar por padrão
            ConfirmButton.Focus();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void SelectAllCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var module in _modules)
            {
                module.IsSelected = true;
            }
        }

        private void SelectAllCheckbox_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var module in _modules)
            {
                module.IsSelected = false;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _result = new ConfirmationResult
            {
                Confirmed = false,
                SelectedModuleIds = new List<string>()
            };
            DialogResult = false;
            Close();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedModules = _modules
                .Where(m => m.IsSelected)
                .Select(m => m.ModuleName)
                .ToList();

            if (selectedModules.Count == 0)
            {
                MessageBox.Show(
                    LocalizationService.Instance.GetString("SmartRepairConfirmNoModulesSelected"),
                    LocalizationService.Instance.GetString("SmartRepairConfirmNoModulesSelectedTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _result = new ConfirmationResult
            {
                Confirmed = true,
                SelectedModuleIds = selectedModules,
                TotalModules = _modules.Count(m => m.IsSelected),
                EstimatedSpaceBytes = long.TryParse(SpaceText.Text.Replace("MB", "").Replace("KB", "").Replace("GB", "").Replace(".", "").Replace(",", ""), out var space) ? space : 0,
                TotalItems = int.TryParse(ItemsText.Text, out var items) ? items : 0
            };

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Obtém o resultado da confirmação
        /// </summary>
        public ConfirmationResult? GetResult() => _result;

        /// <summary>
        /// Atualiza estado de um módulo específico
        /// </summary>
        public void UpdateModuleStatus(string moduleName, string status)
        {
            var module = _modules.FirstOrDefault(m => m.ModuleName == moduleName);
            if (module != null)
            {
                module.Status = status;
            }
        }
    }
}