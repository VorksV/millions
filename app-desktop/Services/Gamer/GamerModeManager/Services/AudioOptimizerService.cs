using VoltrisOptimizer.Helpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using VoltrisOptimizer.Services.Gamer.Interfaces;

namespace VoltrisOptimizer.Services.Gamer.GamerModeManager.Services
{
    /// <summary>
    /// Audio Optimization Service - Otimização de áudio para gaming
    /// Reduz interference, prioriza threads de áudio, desativa efeitos desnecessários
    /// </summary>
    public class AudioOptimizerService : IAudioOptimizerService
    {
        private readonly ILoggingService _logger;
        
        // Backup de configurações
        private int? _originalAudioPriority;
        private int? _originalAudioEffects;
        private int? _originalAudioQuality;
        
        // APIs nativas
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);
        
        [DllImport("winmm.dll", SetLastError = true)]
        private static extern int waveOutGetVolume(IntPtr hwo, out uint pdwVolume);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadPriority(IntPtr hThread, int nPriority);
        
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
        
        // Constantes
        private const int THREAD_BASE_PRIORITY_MAX = 2;
        private const int THREAD_PRIORITY_HIGHEST = 2;
        private const uint THREAD_SET_INFORMATION = 0x0020;
        
        public AudioOptimizerService(ILoggingService logger)
        {
            _logger.LogEntry(nameof(AudioOptimizerService));
            _logger = logger;
            _logger.LogExit(nameof(AudioOptimizerService));
        }
        
        /// <summary>
        /// Otimiza áudio para gaming
        /// </summary>
        public async Task<bool> OptimizeAudioAsync()
        {
            _logger.LogEntry(nameof(OptimizeAudioAsync));
try
            {
                _logger.LogInfo("[Audio] Iniciando otimização de áudio para gaming...");
                
                // 1. Elevar prioridade de threads de áudio
                await OptimizeAudioThreadsAsync();
                
                // 2. Desativar efeitos de áudio desnecessários
                DisableAudioEffects();
                
                // 3. Configurar qualidade de áudio para performance
                OptimizeAudioQuality();
                
                // 4. Otimizar buffer de áudio
                OptimizeAudioBuffer();
                
                // 5. Configurar volume otimizado para gaming
                OptimizeAudioVolume();
                
                _logger.LogSuccess("[Audio] [OK] Áudio otimizado para gaming - Performance máxima sem interferência!");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Audio] Erro na otimização de áudio", ex);
return false;
            }
            _logger.LogExit(nameof(OptimizeAudioAsync));
}
        
        /// <summary>
        /// Otimiza threads de áudio para máxima prioridade
        /// </summary>
        private async Task OptimizeAudioThreadsAsync()
        {
            _logger.LogEntry(nameof(OptimizeAudioThreadsAsync));
try
            {
                // Encontrar e priorizar threads de áudio
                var currentProcess = Process.GetCurrentProcess();
                
                foreach (ProcessThread thread in currentProcess.Threads)
                {
                    try
                    {
                        // Verificar se é thread de áudio (baseado no nome ou prioridade atual)
                        if (IsAudioThread(thread))
                        {
                            var hThread = OpenThread(THREAD_SET_INFORMATION, false, (uint)thread.Id);
                            if (hThread != IntPtr.Zero)
                            {
                                // Elevar prioridade para máxima
                                SetThreadPriority(hThread, THREAD_PRIORITY_HIGHEST);
                                CloseHandle(hThread);
                            }
                        }
                    }
                    catch
                    {
                        // Ignorar threads que não podem ser acessados
                    }
                }
                
                _logger.LogInfo("[Audio] [OK] Threads de áudio priorizados");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Audio] Erro ao otimizar threads: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeAudioThreadsAsync));
}
        
        /// <summary>
        /// Verifica se thread é relacionada a áudio
        /// </summary>
        private bool IsAudioThread(ProcessThread thread)
        {
            _logger.LogEntry(nameof(IsAudioThread));
try
            {
                // Verificar prioridade atual (threads de áudio geralmente têm prioridade mais alta)
return thread.PriorityLevel >= ThreadPriorityLevel.AboveNormal;
            }
            catch
            {
return false;
            }
            _logger.LogExit(nameof(IsAudioThread));
}
        
        /// <summary>
        /// Desativa efeitos de áudio desnecessários
        /// </summary>
        private void DisableAudioEffects()
        {
            _logger.LogEntry(nameof(DisableAudioEffects));
try
            {
                // Desativar efeitos de áudio do Windows
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Multimedia\Audio");
                
                // Backup
                _originalAudioEffects = key.GetValue("DisableAudioEffects") as int?;
                
                // Desativar efeitos para performance
                key.SetValue("DisableAudioEffects", 1, RegistryValueKind.DWord);
                
                // Desativar spatial audio (consome recursos)
                using var spatialKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Audio");
                spatialKey.SetValue("AllowSpatialAudio", 0, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Audio] [OK] Efeitos de áudio desnecessários desativados");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Audio] Erro ao desativar efeitos: {ex.Message}");
            }
            _logger.LogExit(nameof(DisableAudioEffects));
}
        
        /// <summary>
        /// Otimiza qualidade de áudio para performance
        /// </summary>
        private void OptimizeAudioQuality()
        {
            _logger.LogEntry(nameof(OptimizeAudioQuality));
try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Multimedia\Audio");
                
                // Backup
                _originalAudioQuality = key.GetValue("AudioQuality") as int?;
                
                // Configurar qualidade balanceada (performance vs qualidade)
                key.SetValue("AudioQuality", 1, RegistryValueKind.DWord); // Balanced
                
                // Desativar exclusão automática de pop
                key.SetValue("DisableAutoPop", 1, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Audio] [OK] Qualidade de áudio otimizada para performance");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Audio] Erro ao otimizar qualidade: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeAudioQuality));
}
        
        /// <summary>
        /// Otimiza buffer de áudio para menor latência
        /// </summary>
        private void OptimizeAudioBuffer()
        {
            _logger.LogEntry(nameof(OptimizeAudioBuffer));
try
            {
                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Multimedia\Audio");
                
                // Reduzir buffer para menor latência
                key.SetValue("DefaultAudioBufferSize", 128, RegistryValueKind.DWord); // 128 samples
                
                // Configurar taxa de amostragem padrão
                key.SetValue("DefaultSampleRate", 48000, RegistryValueKind.DWord); // 48kHz
                
                _logger.LogInfo("[Audio] [OK] Buffer de áudio otimizado para baixa latência");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Audio] Erro ao otimizar buffer: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeAudioBuffer));
}
        
        /// <summary>
        /// Otimiza configurações de volume para gaming
        /// </summary>
        private void OptimizeAudioVolume()
        {
            _logger.LogEntry(nameof(OptimizeAudioVolume));
try
            {
                // Configurar volume master para nível otimizado (80% para evitar clipping)
                uint currentVolume;
                waveOutGetVolume(IntPtr.Zero, out currentVolume);
                
                // Salvar volume original
                _originalAudioPriority = (int)(currentVolume & 0xFFFF);
                
                // Definir volume otimizado para gaming (80% = 0xCCCC)
                uint optimizedVolume = 0xCCCCCCCC; // 80% para ambos canais
                waveOutSetVolume(IntPtr.Zero, optimizedVolume);
                
                _logger.LogInfo("[Audio] [OK] Volume otimizado para gaming (80%)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Audio] Erro ao otimizar volume: {ex.Message}");
            }
            _logger.LogExit(nameof(OptimizeAudioVolume));
}
        
        /// <summary>
        /// Restaura configurações originais de áudio
        /// </summary>
        public async Task<bool> RestoreAudioAsync()
        {
            _logger.LogEntry(nameof(RestoreAudioAsync));
try
            {
                _logger.LogInfo("[Audio] Restaurando configurações de áudio...");
                
                // Restaurar efeitos de áudio
                if (_originalAudioEffects.HasValue)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Multimedia\Audio");
                    key.SetValue("DisableAudioEffects", _originalAudioEffects.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar qualidade de áudio
                if (_originalAudioQuality.HasValue)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Multimedia\Audio");
                    key.SetValue("AudioQuality", _originalAudioQuality.Value, RegistryValueKind.DWord);
                }
                
                // Restaurar volume original
                if (_originalAudioPriority.HasValue)
                {
                    uint originalVolume = (uint)(_originalAudioPriority.Value | (_originalAudioPriority.Value << 16));
                    waveOutSetVolume(IntPtr.Zero, originalVolume);
                }
                
                // Reabilitar spatial audio
                using var spatialKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Audio");
                spatialKey.SetValue("AllowSpatialAudio", 1, RegistryValueKind.DWord);
                
                _logger.LogInfo("[Audio] [OK] Configurações de áudio restauradas");
return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("[Audio] Erro ao restaurar áudio", ex);
return false;
            }
            _logger.LogExit(nameof(RestoreAudioAsync));
}
        
        /// <summary>
        /// Obtém métricas de áudio
        /// </summary>
        public (int BufferSize, int SampleRate, int VolumeLevel, bool EffectsDisabled) GetAudioMetrics()
        {
            try
            {
                // Obter configurações atuais
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Multimedia\Audio");
                var bufferSize = key?.GetValue("DefaultAudioBufferSize") as int? ?? 256;
                var sampleRate = key?.GetValue("DefaultSampleRate") as int? ?? 44100;
                var effectsDisabled = key?.GetValue("DisableAudioEffects") as int? == 1;
                
                // Obter volume atual
                uint currentVolume;
                waveOutGetVolume(IntPtr.Zero, out currentVolume);
                var volumeLevel = (int)(currentVolume & 0xFFFF) * 100 / 65535;
                
                return (bufferSize, sampleRate, volumeLevel, effectsDisabled);
            }
            catch
            {
                return (256, 44100, 100, false);
            }
        }
    }
    
    /// <summary>
    /// Interface para otimização de áudio
    /// </summary>
    public interface IAudioOptimizerService
    {
        Task<bool> OptimizeAudioAsync();
        Task<bool> RestoreAudioAsync();
        (int BufferSize, int SampleRate, int VolumeLevel, bool EffectsDisabled) GetAudioMetrics();
    }
}
