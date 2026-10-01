using System.Collections.Generic;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Core.SystemIntelligenceProfiler
{
    public interface IRollbackManager
    {
        /// <summary>
        /// Cria um ponto de rollback.
        /// </summary>
        /// <param name="pointId">ID do ponto de rollback.</param>
        /// <param name="description">Descrição do ponto de rollback.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task CreateRollbackPointAsync(string pointId, string description);

        /// <summary>
        /// Executa um rollback para um ponto específico.
        /// </summary>
        /// <param name="pointId">ID do ponto de rollback.</param>
        /// <returns>Resultado do rollback.</returns>
        Task<RollbackResult> ExecuteRollbackAsync(string pointId);

        /// <summary>
        /// Remove um ponto de rollback.
        /// </summary>
        /// <param name="pointId">ID do ponto de rollback.</param>
        /// <returns>Tarefa assíncrona.</returns>
        Task RemoveRollbackPointAsync(string pointId);

        /// <summary>
        /// Obtém todos os pontos de rollback disponíveis.
        /// </summary>
        /// <returns>Lista de pontos de rollback.</returns>
        List<RollbackPoint> GetAvailableRollbackPoints();

        /// <summary>
        /// Verifica se um ponto de rollback existe.
        /// </summary>
        /// <param name="pointId">ID do ponto de rollback.</param>
        /// <returns>Verdadeiro se o ponto de rollback existe.</returns>
        bool RollbackPointExists(string pointId);
        
        // Métodos adicionais necessários para compatibilidade
        string BeginSession();
        void SaveText(string sessionDir, string name, string content);
        List<string> ListBackups();
        void PushAction(string description, string type, string backupPath);
        void ExportBundle(string sessionDir, string bundlePath);
    }

    /// <summary>
    /// Resultado de uma operação de rollback.
    /// </summary>
    public class RollbackResult
    {
        /// <summary>
        /// ID da transação.
        /// </summary>
        public string TransactionId { get; set; }

        /// <summary>
        /// Indica se o rollback foi bem-sucedido.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Número de ações de rollback executadas.
        /// </summary>
        public int RollbackActionsExecuted { get; set; }

        /// <summary>
        /// Lista de erros ocorridos durante o rollback.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Tempo total de execução do rollback.
        /// </summary>
        public System.TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Ponto de rollback.
    /// </summary>
    public class RollbackPoint
    {
        /// <summary>
        /// ID do ponto de rollback.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Descrição do ponto de rollback.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Data/hora de criação.
        /// </summary>
        public System.DateTime CreatedAt { get; set; }

        /// <summary>
        /// Data/hora de restauração (se aplicável).
        /// </summary>
        public System.DateTime? RestoredAt { get; set; }

        /// <summary>
        /// Estado do sistema no momento da criação.
        /// </summary>
        public SystemState SystemState { get; set; }
    }

    /// <summary>
    /// Estado do sistema.
    /// </summary>
    public class SystemState
    {
        /// <summary>
        /// Data/hora da captura.
        /// </summary>
        public System.DateTime CaptureTime { get; set; }

        /// <summary>
        /// Chaves do registro capturadas.
        /// </summary>
        public Dictionary<string, string> RegistryKeys { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Arquivos capturados.
        /// </summary>
        public List<FileInfo> Files { get; set; } = new List<FileInfo>();

        /// <summary>
        /// Serviços do sistema.
        /// </summary>
        public List<ServiceInfo> Services { get; set; } = new List<ServiceInfo>();
    }

    /// <summary>
    /// Informações de um arquivo.
    /// </summary>
    public class FileInfo
    {
        /// <summary>
        /// Caminho completo do arquivo.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Tamanho do arquivo em bytes.
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// Data de modificação.
        /// </summary>
        public System.DateTime ModifiedDate { get; set; }

        /// <summary>
        /// Hash do arquivo para verificação de integridade.
        /// </summary>
        public string Hash { get; set; }
    }

    /// <summary>
    /// Informações de um serviço.
    /// </summary>
    public class ServiceInfo
    {
        /// <summary>
        /// Nome do serviço.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Status do serviço.
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Modo de inicialização do serviço.
        /// </summary>
        public string StartMode { get; set; }
    }
}
