using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Interfaces
{
    /// <summary>
    /// INTERFACE PARA O RESOLVEDOR DE PERFIL DE LIMPEZA ULTRA
    /// 
    /// BASEADO EM POLÍTICAS DECLARATIVAS, NÃO EM NOMES DE PERFIL
    /// PRONTO PARA INTEGRAÇÃO SaaS E POLÍTICAS REMOTAS
    /// </summary>
    public interface IUltraCleanProfileResolver
    {
        /// <summary>
        /// RESOLVE O PLANO DE EXECUÇÃO BASEADO NA POLÍTICA ATIVA
        /// </summary>
        UltraCleanExecutionPlan ResolveExecutionPlan();
    }
}
