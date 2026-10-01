namespace VoltrisOptimizer.Core.Constants
{
    /// <summary>
    /// Constantes de tamanho de arquivo
    /// </summary>
    public static class FileSizeConstants
    {
        // Tamanhos mínimos para detecção de jogos
        public const long MinGameExecutableSize = 1024 * 1024; // 1 MB
        public const long TypicalGameSize = 100 * 1024 * 1024; // 100 MB
        
        // Tamanhos para otimização de processos
        public const long MinProcessWorkingSetForOptimization = 50 * 1024 * 1024; // 50 MB
        public const long LargeProcessWorkingSet = 500 * 1024 * 1024; // 500 MB
        
        // Tamanhos de cache
        public const long SmallCacheSize = 10 * 1024 * 1024; // 10 MB
        public const long MediumCacheSize = 100 * 1024 * 1024; // 100 MB
        public const long LargeCacheSize = 1024 * 1024 * 1024; // 1 GB
    }
}
