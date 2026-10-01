namespace VoltrisOptimizer.UI.ViewModels
{
    /// <summary>
    /// Estado visual do ícone de Saúde do Dashboard. Deriva do score real
    /// (0-100) calculado pela análise de saúde e reflete o ícone e a animação.
    /// </summary>
    public enum HealthIconState
    {
        /// <summary>Score abaixo de 55: coração partido.</summary>
        Critical,

        /// <summary>Score entre 55 e 69: coração instável, batimento irregular.</summary>
        Warning,

        /// <summary>Score 70 ou acima: coração saudável, batimento calmo e contínuo.</summary>
        Healthy
    }
}
