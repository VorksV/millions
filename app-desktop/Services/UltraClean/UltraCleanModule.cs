using System;

namespace VoltrisOptimizer.Services.UltraClean
{
    /// <summary>
    /// REPRESENTAÇÃO DE UM MÓDULO DE LIMPEZA
    /// 
    /// AUTO-DESCRITIVO E AUTO-CONTIDO
    /// NÃO DECIDE SOBRE SUA EXECUÇÃO
    /// APENAS SE AUTO-DESCREVE
    /// </summary>
    public class UltraCleanModule
    {
        /// <summary>
        /// IDENTIFICADOR ÚNICO DO MÓDULO
        /// </summary>
        public string Id { get; set; }

        public UltraCleanModule()
        {
            // O logger não é injetado aqui pois esta é uma classe de dados POCO usada pelo UltraCleanerService
            // Logamos via o serviço que orquestra, mas inicializamos o ID para garantir rastreabilidade.
        }

        /// <summary>
        /// NOME AMIGÁVEL PARA EXIBIÇÃO
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// DESCRIÇÃO DO QUE O MÓDULO FAZ
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// REQUER PRIVILÉGIOS DE ADMINISTRADOR
        /// </summary>
        public bool RequiresAdmin { get; set; }

        /// <summary>
        /// REQUER ACESSO AO REGISTRO DO WINDOWS
        /// </summary>
        public bool RequiresRegistryAccess { get; set; }

        /// <summary>
        /// REQUER ACESSO A DRIVERS/SISTEMA
        /// </summary>
        public bool RequiresDriverAccess { get; set; }

        /// <summary>
        /// NÍVEL DE RISCO DA OPERAÇÃO
        /// </summary>
        public RiskLevel RiskLevel { get; set; }

        /// <summary>
        /// É CONSIDERADO SEGURO PARA EXECUÇÃO PADRÃO
        /// </summary>
        public bool IsSafe { get; set; }

        /// <summary>
        /// CATEGORIA DO MÓDULO (Sistema, Cache, Temp, etc.)
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// REQUER CONFIRMAÇÃO EXPLÍCITA DO USUÁRIO
        /// </summary>
        public bool RequiresUserConfirmation { get; set; }

        /// <summary>
        /// TEMPO ESTIMADO DE EXECUÇÃO (em segundos)
        /// </summary>
        public int EstimatedExecutionTimeSeconds { get; set; }

        /// <summary>
        /// FUNÇÃO DE ANÁLISE (RETORNA TAMANHO ESTIMADO)
        /// </summary>
        public Delegate? AnalyzeAction { get; set; }

        /// <summary>
        /// FUNÇÃO DE LIMPEZA (RETORNA TAMANHO LIBERADO)
        /// </summary>
        public Delegate? CleanAction { get; set; }

        /// <summary>
        /// VALOR PADRÃO: MÓDULO HABILITADO
        /// </summary>
        public bool IsEnabledByDefault { get; set; } = true;

        /// <summary>
        /// ORDEM DE EXECUÇÃO SEGURO (módulos dependentes devem executar após dependências)
        /// </summary>
        public int ExecutionOrder { get; set; } = 0;

        /// <summary>
        /// SUPORTA ANÁLISE SEPARADA
        /// </summary>
        public bool SupportsAnalysis { get; set; } = true;

        /// <summary>
        /// SUPORTA LIMPEZA SEPARADA
        /// </summary>
        public bool SupportsCleaning { get; set; } = true;

        /// <summary>
        /// VALOR PADRÃO: NÃO REQUER REINICIAR O SISTEMA
        /// </summary>
        public bool RequiresRestart { get; set; } = false;

        /// <summary>
        /// VALOR PADRÃO: NÃO REQUER FECHAR APLICAÇÕES
        /// </summary>
        public bool RequiresClosingApps { get; set; } = false;

        /// <summary>
        /// REQUER CONEXÃO À INTERNET (para downloads, atualizações, etc.)
        /// </summary>
        public bool RequiresInternetConnection { get; set; } = false;

        /// <summary>
        /// É CRÍTICO PARA O SISTEMA (nunca deve ser desabilitado automaticamente)
        /// </summary>
        public bool IsSystemCritical { get; set; } = false;

        /// <summary>
        /// REQUER ESPAÇO TEMPORÁRIO PARA EXECUÇÃO
        /// </summary>
        public long EstimatedTemporarySpaceRequired { get; set; } = 0;

        /// <summary>
        /// REQUER ACESSO A SERVIÇOS DO SISTEMA
        /// </summary>
        public bool RequiresSystemServiceAccess { get; set; } = false;

        /// <summary>
        /// REQUER ACESSO A ARQUIVOS DO SISTEMA PROTEGIDOS
        /// </summary>
        public bool RequiresProtectedFileAccess { get; set; } = false;

        /// <summary>
        /// REQUER ACESSO A PROCESSOS DO SISTEMA
        /// </summary>
        public bool RequiresSystemProcessAccess { get; set; } = false;

        /// <summary>
        /// REQUER ACESSO A SERVIÇOS ESPECÍFICOS (por nome)
        /// </summary>
        public string[] RequiredServices { get; set; } = new string[0];

        /// <summary>
        /// REQUER ACESSO A APLICAÇÕES ESPECÍFICAS (por nome)
        /// </summary>
        public string[] RequiredApplications { get; set; } = new string[0];

        /// <summary>
        /// REQUER ACESSO A PASTAS ESPECÍFICAS
        /// </summary>
        public string[] RequiredFolders { get; set; } = new string[0];

        /// <summary>
        /// REQUER ACESSO A ARQUIVOS ESPECÍFICOS
        /// </summary>
        public string[] RequiredFiles { get; set; } = new string[0];

        /// <summary>
        /// REQUER ACESSO A CHAVES DE REGISTRO ESPECÍFICAS
        /// </summary>
        public string[] RequiredRegistryKeys { get; set; } = new string[0];

        /// <summary>
        /// REQUER ACESSO A PORTAS DE REDE ESPECÍFICAS
        /// </summary>
        public int[] RequiredNetworkPorts { get; set; } = new int[0];

        /// <summary>
        /// REQUER ACESSO A DISPOSITIVOS ESPECÍFICOS
        /// </summary>
        public string[] RequiredDevices { get; set; } = new string[0];

        /// <summary>
        /// REQUER ACESSO A USUÁRIOS ESPECÍFICOS
        /// </summary>
        public string[] RequiredUserAccounts { get; set; } = new string[0];

        /// <summary>
        /// REQUER ACESSO A GRUPOS DE USUÁRIOS ESPECÍFICOS
        /// </summary>
        public string[] RequiredUserGroups { get; set; } = new string[0];

        /// <summary>
        /// REQUER ACESSO A POLÍTICAS DE GRUPO ESPECÍFICAS
        /// </summary>
        public string[] RequiredGroupPolicies { get; set; } = new string[0];

        /// <summary>
        /// REQUER ACESSO A POLÍTICAS DE DOMÍNIO ESPECÍFICAS
        /// </summary>
        public string[] RequiredDomainPolicies { get; set; } = new string[0];
    }

    /// <summary>
    /// NÍVEL DE RISCO (COPIA DO ENUM EXISTENTE PARA CONSISTÊNCIA)
    /// </summary>
    public enum RiskLevel
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Extreme = 4
    }
}
