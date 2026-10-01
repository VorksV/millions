namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Estado visual do ícone de Licença do Dashboard. Reflete o plano
    /// realmente ativo no LicenseTokenStore.
    /// </summary>
    public enum LicenseIconState
    {
        /// <summary>Sem licença paga (inclui trial e None): ícone moderno de licença inativa.</summary>
        Inactive,

        /// <summary>Plano Standard ativo.</summary>
        Standard,

        /// <summary>Plano Pro ativo.</summary>
        Pro,

        /// <summary>Plano Enterprise ativo.</summary>
        Enterprise
    }
}
