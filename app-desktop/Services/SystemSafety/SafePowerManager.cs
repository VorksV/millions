using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VoltrisOptimizer.Services.SystemChanges;
using VoltrisOptimizer.Core.Configuration;
using VoltrisOptimizer.Services;
using VoltrisOptimizer.Services.Power; // Assuming ILoggingService exists

namespace VoltrisOptimizer.Services.SystemSafety
{
    public interface ISafePowerManager
    {
        bool TrySetPowerScheme(Guid schemeGuid);
        bool TrySetPowerSetting(Guid schemeGuid, Guid subGroupGuid, Guid settingGuid, uint acValue);
    }

    /// <summary>
    /// Gerenciador de Energia Seguro.
    /// Substitui o uso lento de `powercfg.exe` por chamadas diretas via `PowrProf.dll`.
    /// Utiliza cache de suporte e validação antes da aplicação.
    /// </summary>
    public sealed class SafePowerManager : ISafePowerManager
    {
        private readonly ICapabilityGuard _capabilityGuard;
        private readonly IVoltrisFeatureFlagManager _featureFlags;
        private readonly ILoggingService _logger;

        // Cache de Settings Suportados. Evita chamadas cegas que corrompem a UI de Energia.
        private readonly ConcurrentDictionary<string, bool> _supportedSettingsCache = new(StringComparer.OrdinalIgnoreCase);

    // [FIX:UNICA-FONTE] P/Invoke de powrprof REMOVIDOS do SafePowerManager.
    //
    // Este serviço declarava os próprios `PowerWriteACValueIndex` e
    // `PowerReadACValueIndex` e escrevia energia por eles, com prioridade
    // `Emergency` (0) na fila de planos — ou seja, acima do Perfil Inteligente
    // (3). Era a quinta via de escrita, e o nome "Safe" não mudava o fato: um
    // rollback granular pode reverter um setting protegido do plano gerenciado
    // usando o valor que capturou ANTES da aplicação do perfil.
    //
    // Isso importa porque "restaurar o valor anterior" e "aplicar o perfil" são
    // coisas diferentes. Quem reverte é o `ProfilePowerCoordinator`, que sabe o
    // que o perfil quer. Este serviço continua existindo para o resto do que ele
    // faz (guardas de capacidade, feature flags), mas não escreve mais energia:
    // leitura e escrita de setting passaram a usar `PowerNativeMethods`, a
    // superfície única do projeto para `powrprof.dll`, onde o portão decide.

        public SafePowerManager(ICapabilityGuard capabilityGuard, IVoltrisFeatureFlagManager featureFlags, ILoggingService logger)
        {
            _capabilityGuard = capabilityGuard;
            _featureFlags = featureFlags;
            _logger = logger;
        }

        public bool TrySetPowerScheme(Guid schemeGuid)
        {
            if (!_capabilityGuard.AllowServiceTweaks()) return false;

            if (!_featureFlags.UseSafePowerManager)
            {
                return LegacySetPowerScheme(schemeGuid);
            }

            try
            {
                return ProfilePowerAuthority.RequestProfileApply("SafePowerManager", "pedido legado de troca de plano", _logger);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Falha ao definir Active Power Scheme: {schemeGuid}", ex);
                return false;
            }
        }

        /// <summary>
        /// [FIX:UNICA-FONTE] Escrita de setting de energia DELEGADA AO PORTÃƒO.
        ///
        /// O corpo antigo escrevia por P/Invoke prÃ³prio, com prioridade
        /// `Emergency` na fila de planos â€” ou seja, ACIMA do Perfil Inteligente
        /// (3). Isso permitia que um rollback granular reizesse um setting
        /// protegido do plano gerenciado com o valor capturado antes da
        /// aplicaÃ§Ã£o do perfil, e o perfil perderia sem que nada aparecesse no
        /// log como conflito.
        ///
        /// "Restaurar o valor anterior" e "aplicar o perfil" sÃ£o coisas
        /// diferentes. Quem reverte Ã© o `ProfilePowerCoordinator`, que sabe o que
        /// o perfil quer. Este serviÃ§o continua existindo para o resto do que ele
        /// faz (guarda de capacidade, feature flags), mas nÃ£o escreve mais
        /// energia.
        ///
        /// A chamada agora vai para `PowerNativeMethods`, a superfÃ­cie Ãºnica de
        /// `powrprof.dll` no projeto. LÃ¡ dentro o `PowerWriteGate` decide: se o
        /// setting Ã© protegido, o plano Ã© o gerenciado e quem pede nÃ£o Ã© o
        /// perfil, a escrita Ã© recusada e registrada.
        ///
        /// A checagem de "jÃ¡ estÃ¡ no valor" continua aqui, antes da escrita,
        /// porque evitar uma chamada inÃºtil ao Windows era a finalidade
        /// original deste serviÃ§o.
        /// </summary>
        public bool TrySetPowerSetting(Guid schemeGuid, Guid subGroupGuid, Guid settingGuid, uint acValue)
        {
            if (!_capabilityGuard.AllowServiceTweaks()) return false;

            if (!_featureFlags.UseSafePowerManager)
            {
                return LegacySetPowerSetting(schemeGuid, subGroupGuid, settingGuid, acValue);
            }

            // [FIX:UNICO-DONO-DE-ENERGIA] Este servico nao grava mais power setting.
            //
            // Ele escrevia qualquer valor que lhe pedissem, para qualquer
            // setting, e o passava pelo portao porque a feature flag
            // `UseSafePowerManager` estava ligada. O portao e' necessario, mas
            // nao e' suficiente: ele existe para que a ESCRITA DO PERFIL seja a
            // unica escrita, e uma service flag ligada por outro servico nao e'
            // o perfil.
            //
            // O detalhe que mais importa esta na ultima linha do codigo antigo:
            // ele gravava `acValue, acValue` — o MESMO valor para tomada e para
            // bateria. Gravar o valor de tomada na bateria e' um dos erros de
            // energia ja documentados nesta refatoracao, e esta era mais uma
            // porta para produzi-lo, por baixo de um nome que promete seguranca.
            //
            // A checagem de idempotencia e o cache de suporte continuam no
            // lugar: sao de LEITURA, e leitura e' sempre legitima.
            string cacheKey = $"{schemeGuid}_{subGroupGuid}_{settingGuid}";

            uint? currentValue = Utils.Win32.PowerNativeMethods.TryReadSchemeAcValueIndex(
                schemeGuid, settingGuid, subGroupGuid);

            if (currentValue.HasValue)
            {
                _supportedSettingsCache[cacheKey] = true;

                bool alreadyThere = currentValue.Value == acValue;
                _logger.LogInfo(
                    $"[SafePower] Gravacao de setting NEUTRALIZADA (valor pedido {acValue}, " +
                    $"atual {currentValue.Value}, ja estava correto={alreadyThere}). " +
                    "Quem grava energy setting e' o Perfil Inteligente.");
                return true;
            }

            _supportedSettingsCache[cacheKey] = false;
            _logger.LogWarning(
                $"[SafePower] Setting {settingGuid} nao suportado/ilegivel neste esquema; " +
                "nada foi gravado.");
            return false;
        }

        private bool LegacySetPowerScheme(Guid schemeGuid)
        {
            // By-pass (Strangler Pattern)
            // Em uma integração real, isso chamaria o PowerProfileController legado.
            return false;
        }

        private bool LegacySetPowerSetting(Guid schemeGuid, Guid subGroupGuid, Guid settingGuid, uint value)
        {
            return false;
        }
    }
}
