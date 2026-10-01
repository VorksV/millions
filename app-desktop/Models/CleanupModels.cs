using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Models
{
    // MODELO UNIFICADO DE LIMPEZA - Single Source of Truth (SSOT)
    // Separação clara de conceitos:
    // - FOUND: O que foi detectado (real)
    // - SELECTED: O que está marcado para limpeza (intenção do usuário)
    // - CLEANED: O que foi realmente limpo (resultado da execução)
    // - SKIPPED: O que foi pulado e por quê (transparência)

    /// <summary>
    /// Configuração de uma categoria de limpeza (definição estática)
    /// </summary>
    public class CleanupCategory
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public List<CleanupCategoryItem> Items { get; set; } = new();

        /// <summary>
        /// Chave de tradução do NOME da categoria no LocalizationService.
        ///
        /// CONTRATO: <see cref="Name"/> é IDENTIDADE, não apresentação. Dela saem
        /// o ModuleId (GenerateModuleId), a chave de autorização no plano de
        /// execução e as políticas declarativas. Traduzir Name quebraria a
        /// autorização em silêncio — o item simplesmente sumiria da análise.
        /// Por isso a tradução vive em um campo separado, opcional, e só é
        /// aplicada na fronteira de exibição.
        ///
        /// Null/vazio = manter <see cref="Name"/> (comportamento legado).
        /// </summary>
        public string? LocalizationKey { get; set; }
    }

    /// <summary>
    /// Configuração de um item de limpeza (definição estática)
    /// </summary>
    public class CleanupCategoryItem
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Delegate? CleanAction { get; set; }
        public Delegate? AnalyzeAction { get; set; }
        public bool RequiresAdmin { get; set; }

        /// <summary>
        /// Indica se o item é considerado seguro para limpeza automática
        /// </summary>
        public bool IsSafe { get; set; }

        /// <summary>
        /// Indica se requer confirmação explícita do usuário
        /// </summary>
        public bool RequiresConsent { get; set; }

        /// <summary>
        /// Razão pela qual pode ser pulado (para exibição ao usuário)
        /// </summary>
        public string? SkipReason { get; set; }

        /// <summary>
        /// Chave de tradução do NOME do item no LocalizationService.
        /// Ver <see cref="CleanupCategory.LocalizationKey"/> para o contrato
        /// identidade-vs-apresentação. Null/vazio = usar <see cref="Name"/>.
        /// </summary>
        public string? LocalizationKey { get; set; }

        /// <summary>
        /// Chave de tradução da DESCRIÇÃO do item. Null/vazio = usar <see cref="Description"/>.
        /// </summary>
        public string? DescriptionLocalizationKey { get; set; }
    }

    /// <summary>
    /// Resultado da análise de um item específico
    /// </summary>
    public class ItemAnalysis : System.ComponentModel.INotifyPropertyChanged
    {
        /// <summary>
        /// Nome CANÔNICO do item (identidade do módulo). Nunca traduzido:
        /// é a chave de autorização do plano de execução.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Rótulo EXIBIDO ao usuário, já no idioma ativo.
        /// Preenchido pelo UltraCleanerService a partir de
        /// <see cref="CleanupCategoryItem.LocalizationKey"/>. Se vazio, a UI
        /// deve cair para <see cref="Name"/>.
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        // METADADOS DO ITEM
        public bool IsSafe { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool RequiresConsent { get; set; }
        public string? Path { get; set; }
        public DateTime? LastModified { get; set; }

        // TAMANHOS (separados por estado)
        /// <summary>
        /// Tamanho total encontrado (FOUND)
        /// </summary>
        public long FoundSize { get; set; }

        public string FormattedFoundSize
        {
            get
            {
                if (FoundSize == 0) return "0 B";
                string[] sizes = { "B", "KB", "MB", "GB", "TB" };
                double len = FoundSize;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }
                return string.Format("{0:0.##} {1}", len, sizes[order]);
            }
        }

        private bool _isSelected;
        /// <summary>
        /// Se o item está selecionado para limpeza
        /// </summary>
        public bool IsSelected 
        { 
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(SelectedSize)));
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(UnselectedSize)));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Tamanho que será limpo (SELECTED) = FoundSize se IsSelected = true
        /// </summary>
        public long SelectedSize => IsSelected ? FoundSize : 0;

        /// <summary>
        /// Tamanho que NÃO será limpo (UNSELECTED/UNSAFE)
        /// </summary>
        public long UnselectedSize => IsSelected ? 0 : FoundSize;

        /// <summary>
        /// Razão pela qual foi pulado (se aplicável)
        /// </summary>
        public string? SkipReason { get; set; }

        /// <summary>
        /// Ação de limpeza associada
        /// </summary>
        public Delegate? CleanAction { get; set; }

        /// <summary>
        /// Se o item foi realmente limpo na execução
        /// </summary>
        public bool WasCleaned { get; set; }

        /// <summary>
        /// Tamanho realmente liberado na execução (CLEANED)
        /// </summary>
        public long CleanedSize { get; set; }
    }

    /// <summary>
    /// Análise de uma categoria completa
    /// </summary>
    public class CategoryAnalysis
    {
        /// <summary>Nome canônico da categoria (identidade do módulo).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Rótulo exibido ao usuário, já no idioma ativo.</summary>
        public string DisplayName { get; set; } = string.Empty;

        public string Icon { get; set; } = string.Empty;
        public List<ItemAnalysis> Items { get; set; } = new();

        // TOTAIS CALCULADOS
        /// <summary>
        /// Total encontrado na categoria
        /// </summary>
        public long TotalFoundSize => Items.Sum(i => i.FoundSize);

        /// <summary>
        /// Total selecionado para limpeza
        /// </summary>
        public long TotalSelectedSize => Items.Sum(i => i.SelectedSize);

        /// <summary>
        /// Total NÃO selecionado (sensível/seguro mas não marcado)
        /// </summary>
        public long TotalUnselectedSize => Items.Sum(i => i.UnselectedSize);

        /// <summary>
        /// Total de itens encontrados
        /// </summary>
        public int TotalItemCount => Items.Count;

        /// <summary>
        /// Total de itens selecionados
        /// </summary>
        public int SelectedItemCount => Items.Count(i => i.IsSelected);

        /// <summary>
        /// Itens que requerem consentimento (não seguros)
        /// </summary>
        public List<ItemAnalysis> UnsafeItems => Items.Where(i => !i.IsSafe && i.FoundSize > 0).ToList();

        /// <summary>
        /// Tamanho total de itens não seguros
        /// </summary>
        public long UnsafeSize => UnsafeItems.Sum(i => i.FoundSize);
    }

    /// <summary>
    /// Análise completa do sistema de limpeza - SINGLE SOURCE OF TRUTH
    /// </summary>
    public class UltraCleanAnalysis
    {
        public DateTime AnalysisDate { get; set; } = DateTime.Now;
        public TimeSpan AnalysisDuration { get; set; }
        public bool IsCompleted { get; set; }
        public string? ErrorMessage { get; set; }
        public List<CategoryAnalysis> Categories { get; set; } = new();

        // TOTAIS GLOBAIS (calculados, não armazenados separadamente)
        /// <summary>
        /// ESPAÇO TOTAL ENCONTRADO (FOUND) - tudo que existe
        /// </summary>
        public long TotalFoundSpace => Categories.Sum(c => c.TotalFoundSize);

        /// <summary>
        /// ESPAÇO SELECIONADO PARA LIMPEZA (SELECTED) - o que será limpo
        /// </summary>
        public long TotalSelectedSpace => Categories.Sum(c => c.TotalSelectedSize);

        /// <summary>
        /// ESPAÇO NÃO SELECIONADO (UNSELECTED) - encontrado mas não marcado
        /// </summary>
        public long TotalUnselectedSpace => Categories.Sum(c => c.TotalUnselectedSize);

        /// <summary>
        /// ESPAÇO EM ITENS NÃO SEGUROS (UNSAFE) - requer consentimento
        /// </summary>
        public long TotalUnsafeSpace => Categories.Sum(c => c.UnsafeSize);

        /// <summary>
        /// Total de itens encontrados
        /// </summary>
        public int TotalItemCount => Categories.Sum(c => c.TotalItemCount);

        /// <summary>
        /// Total de itens selecionados
        /// </summary>
        public int SelectedItemCount => Categories.Sum(c => c.SelectedItemCount);

        /// <summary>
        /// ESPAÇO REALMENTE LIMPO NA EXECUÇÃO (CLEANED)
        /// </summary>
        public long TotalCleanedSpace { get; set; }

        /// <summary>
        /// Lista de itens que foram pulados com razões
        /// </summary>
        public List<SkippedItemInfo> SkippedItems { get; set; } = new();

        // PROPRIEDADES DE COMPATIBILIDADE (deprecadas, mas mantidas)
        [Obsolete("Use TotalFoundSpace para ser explícito, ou TotalSelectedSpace para o que será limpo")]
        public long TotalReclaimable => TotalFoundSpace;

        [Obsolete("Use TotalSelectedSpace para clareza semântica")]
        public long TotalSpaceToClean => TotalSelectedSpace;

        /// <summary>
        /// Gera relatório de transparência detalhado
        /// </summary>
        public CleanupTransparencyReport GenerateTransparencyReport()
        {
            return new CleanupTransparencyReport
            {
                TotalFound = TotalFoundSpace,
                TotalSelected = TotalSelectedSpace,
                TotalUnselected = TotalUnselectedSpace,
                TotalUnsafe = TotalUnsafeSpace,
                TotalCleaned = TotalCleanedSpace,
                SelectedItemCount = SelectedItemCount,
                TotalItemCount = TotalItemCount,
                SkippedItems = SkippedItems
            };
        }
    }

    /// <summary>
    /// Informação sobre item pulado
    /// </summary>
    public class SkippedItemInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Reason { get; set; } = string.Empty;
        public bool RequiresManualConsent { get; set; }
    }

    /// <summary>
    /// Relatório de transparência da limpeza
    /// </summary>
    public class CleanupTransparencyReport
    {
        public long TotalFound { get; set; }
        public long TotalSelected { get; set; }
        public long TotalUnselected { get; set; }
        public long TotalUnsafe { get; set; }
        public long TotalCleaned { get; set; }
        public int SelectedItemCount { get; set; }
        public int TotalItemCount { get; set; }
        public List<SkippedItemInfo> SkippedItems { get; set; } = new();

        public string Summary => $"{FormatBytes(TotalFound)} encontrados, {FormatBytes(TotalSelected)} selecionados, {FormatBytes(TotalUnsafe)} requerem confirmação";

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.0} {sizes[order]}";
        }
    }

    /// <summary>
    /// Progressão da análise
    /// </summary>
    public class AnalysisProgress
    {
        public string CurrentCategory { get; set; } = string.Empty;
        public string CurrentItem { get; set; } = string.Empty;
        public int CategoriesProcessed { get; set; }
        public int TotalCategories { get; set; }
        public int ItemsProcessed { get; set; }
        public int TotalItems { get; set; }
        public double ProgressPercentage => TotalCategories > 0 ? (CategoriesProcessed * 100.0 / TotalCategories) : 0;
        public int PercentComplete { get; set; }
        public bool IsCompleted => CategoriesProcessed >= TotalCategories;
        public DateTime StartTime { get; set; } = DateTime.Now;
        public TimeSpan ElapsedTime => DateTime.Now - StartTime;
    }

    /// <summary>
    /// Resultado da execução da limpeza
    /// </summary>
    public class CleanupResult
    {
        public bool Success { get; set; }

        /// <summary>
        /// Espaço realmente liberado (confirmado)
        /// </summary>
        public long SpaceCleaned { get; set; }

        /// <summary>
        /// Itens que foram limpos
        /// </summary>
        public int ItemsCleaned { get; set; }

        /// <summary>
        /// Itens que falharam
        /// </summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Itens que foram pulados com razões
        /// </summary>
        public List<SkippedItemInfo> SkippedItems { get; set; } = new();

        /// <summary>
        /// Relatório completo de transparência
        /// </summary>
        public CleanupTransparencyReport? TransparencyReport { get; set; }
    }

    /// <summary>
    /// Progressão da limpeza
    /// </summary>
    public class CleanupProgress
    {
        public string CurrentItem { get; set; } = string.Empty;
        public int PercentComplete { get; set; }
        public long SpaceCleanedSoFar { get; set; }
    }

    /// <summary>
    /// Status atual da análise
    /// </summary>
    public class AnalysisStatus
    {
        public bool IsAnalyzing { get; set; }
        public string CurrentCategory { get; set; } = string.Empty;
        public string CurrentItem { get; set; } = string.Empty;
        public int PercentComplete { get; set; }
        public UltraCleanAnalysis? LastAnalysis { get; set; }
    }
}
