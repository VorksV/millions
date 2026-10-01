using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VoltrisOptimizer.Utils.Win32
{
    /// <summary>
    /// Fornece acesso direto as APIs nativas do Windows para gerenciamento de energia (powrprof.dll).
    /// Evita overhead de chamar powercfg.exe como processo e permite leitura/escrita ultrarrápida.
    /// </summary>
    public static class PowerNativeMethods
    {
        private const string POWRPROF = "powrprof.dll";

        [DllImport(POWRPROF, EntryPoint = "PowerWriteACValueIndex")]
        private static extern uint PowerWriteACValueIndexNative(
            IntPtr RootPowerKey, ref Guid SchemeGuid,
            ref Guid SubGroupGuid, ref Guid SettingGuid, uint AcValueIndex);

        [DllImport(POWRPROF, EntryPoint = "PowerWriteDCValueIndex")]
        private static extern uint PowerWriteDCValueIndexNative(
            IntPtr RootPowerKey, ref Guid SchemeGuid,
            ref Guid SubGroupGuid, ref Guid SettingGuid, uint DcValueIndex);

        public static bool IsVoltrisPlan(Guid schemeGuid)
        {
            string planName = GetPlanName(schemeGuid);
            return !string.IsNullOrEmpty(planName) && planName.IndexOf("Voltris", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static uint PowerWriteACValueIndex(
   IntPtr RootPowerKey, ref Guid SchemeGuid,
   ref Guid SubGroupGuid, ref Guid SettingGuid, uint AcValueIndex)
   {
   // [FIX:POWER-GATE] O "GUARDA" ANTERIOR SÓ OBSERVAVA.
   //
   // O código abaixo era, na íntegra:
   //
   //     if (!IsVoltrisPlan(SchemeGuid))
   //     {
   //         LoggingService?.LogDebug($"[PowerGuard] Permitindo gravacao...");
   //     }
   //     return PowerWriteACValueIndexNative(...);   // ← escrevia assim mesmo
   //
   // Um guarda que registra e depois obedece não é um guarda: é um log. Por
   // isso o `o servico legado` conseguia gravar EPP 20 no plano
   // nativo e desfazer o EPP 45 que o sistema novo havia gravado no plano
   // Voltris. A medição de campo provou isso.
   //
   // Agora a decisão é do `PowerWriteGate`, que sabe qual plano é o gerenciado.
   if (!VoltrisOptimizer.Services.Power.PowerWriteGate.AllowSettingWrite(
           SchemeGuid, SettingGuid, "API nativa (AC)", out string gateReasonAc))
   {
       VoltrisOptimizer.App.LoggingService?.LogDebug($"[PowerGuard] escrita AC bloqueada: {gateReasonAc}");
       return 5; // ERROR_ACCESS_DENIED — os chamadores ja tratam isso como falha
   }

   return PowerWriteACValueIndexNative(RootPowerKey, ref SchemeGuid, ref SubGroupGuid, ref SettingGuid, AcValueIndex);
   }

        public static uint PowerWriteDCValueIndex(
            IntPtr RootPowerKey, ref Guid SchemeGuid,
            ref Guid SubGroupGuid, ref Guid SettingGuid, uint DcValueIndex)
        {
            // [FIX:POWER-GATE] Mesma decisao do caminho AC: a linha DC passa a
            // ser filtrada pelo portao, e nao apenas observada.
            if (!VoltrisOptimizer.Services.Power.PowerWriteGate.AllowSettingWrite(
                    SchemeGuid, SettingGuid, "API nativa (DC)", out string gateReasonDc))
            {
                VoltrisOptimizer.App.LoggingService?.LogDebug($"[PowerGuard] escrita DC bloqueada: {gateReasonDc}");
                return 5; // ERROR_ACCESS_DENIED
            }

            return PowerWriteDCValueIndexNative(RootPowerKey, ref SchemeGuid, ref SubGroupGuid, ref SettingGuid, DcValueIndex);
        }

        [DllImport(POWRPROF)]
        public static extern uint PowerReadACValueIndex(
            IntPtr RootPowerKey, ref Guid SchemeGuid,
            ref Guid SubGroupGuid, ref Guid SettingGuid, out uint AcValueIndex);

        [DllImport(POWRPROF)]
        public static extern uint PowerReadDCValueIndex(
            IntPtr RootPowerKey, ref Guid SchemeGuid,
            ref Guid SubGroupGuid, ref Guid SettingGuid, out uint DcValueIndex);

        [DllImport(POWRPROF)]
        public static extern uint PowerGetActiveScheme(
            IntPtr UserRootPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport(POWRPROF)]
        public static extern uint PowerSetActiveScheme(
            IntPtr UserRootPowerKey, ref Guid SchemeGuid);

        [DllImport(POWRPROF, CharSet = CharSet.Unicode)]
        public static extern uint PowerReadFriendlyName(
            IntPtr RootPowerKey, ref Guid SchemeGuid, 
            IntPtr SubGroupOfPowerSettingsGuid, IntPtr SettingGuid, 
            IntPtr Buffer, ref uint BufferSize);

        [DllImport(POWRPROF)]
        public static extern uint PowerEnumerate(
            IntPtr RootPowerKey, IntPtr SchemeGuid, IntPtr SubGroupOfPowerSettingsGuid, 
            uint AccessFlags, uint Index, IntPtr Buffer, ref uint BufferSize);

        [DllImport(POWRPROF)]
        public static extern uint PowerDuplicateScheme(
            IntPtr RootPowerKey, ref Guid SourceSchemeGuid, out IntPtr DestinationSchemeGuid);

        [DllImport(POWRPROF)]
        public static extern uint PowerDeleteScheme(
            IntPtr RootPowerKey, ref Guid SchemeGuid);

        [DllImport(POWRPROF, CharSet = CharSet.Unicode)]
        public static extern uint PowerWriteFriendlyName(
            IntPtr RootPowerKey, ref Guid SchemeGuid, 
            IntPtr SubGroupOfPowerSettingsGuid, IntPtr SettingGuid, 
            IntPtr Buffer, uint BufferSize);

        [DllImport(POWRPROF)]
        public static extern uint PowerFreeMemory(IntPtr Memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);

        // Power Setting GUIDs - Categorias
        public static readonly Guid GUID_SUB_PROCESSOR = new("54533251-82be-4824-96c1-47b60b740d00");
        public static readonly Guid GUID_SUB_DISK      = new("0012ee47-9041-4b5d-9b77-535fba8b1442");
        public static readonly Guid GUID_SUB_USB       = new("2a737441-1930-4402-8d77-b2bebba308a3");
        public static readonly Guid GUID_SUB_PCIE      = new("501a4d13-42af-4429-9fd1-a8218c268e20");
        public static readonly Guid GUID_SUB_DISPLAY   = new("7516b95f-f776-4464-8c53-06167f40cc99");
        public static readonly Guid GUID_SUB_WIRELESS  = new("19caa586-e017-45cd-bf73-05f3498e2f82");

        // Power Setting GUIDs - Configurações
        public static readonly Guid GUID_CPU_MIN        = new("893dee8e-2bef-41e0-89c6-b55d0929964c");
        public static readonly Guid GUID_CPU_MAX        = new("bc5038f7-23e0-4960-96da-33abaf5935ec");
        public static readonly Guid GUID_CPU_BOOST      = new("be337238-0d82-4146-a960-4f3749d470c7");
        public static readonly Guid GUID_CPU_EPP        = new("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");
        // [FIX:WRONG-GUID] Este era o GUID do Core Parking MINIMO, e estava ERRADO:
        // `0cc5b5c8-38ce-4793-80e1-94e85f6dca3a` nao existe como setting de
        // processador. O correto e `0cc5b647-c1df-4637-891a-dec35c318583`, o mesmo
        // que os servicos legados gravavam por `powercfg`.
        //
        // Consequencia do GUID errado: toda leitura deste campo devolvia falha,
        // o que fazia o app acreditar que nao havia core parking no plano — e
        // portanto nao via o valor 0 sendo gravado pelos servicos legacy. O
        // defeito ficava invisivel justamente no campo que ele mais importava.
        public static readonly Guid GUID_CPU_CPUMINCORES= new("0cc5b647-c1df-4637-891a-dec35c318583");
        public static readonly Guid GUID_CPU_CPUMAXCORES= new("ea062031-0e34-4ff1-9b6d-eb1059334028");
        public static readonly Guid GUID_DISK_TIMEOUT   = new("6738e2c4-e8a5-4a42-b16a-e040e769756e");
        public static readonly Guid GUID_USB_SUSPEND    = new("48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
        public static readonly Guid GUID_PCIE_LINK      = new("ee12f906-d277-404b-b6da-e5fa1a576df5");
        public static readonly Guid GUID_DISPLAY_TIMEOUT = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
        public static readonly Guid GUID_WIRELESS_MODE   = new("12bbebe6-58d6-4636-95bb-3217ef867c1a");

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessorPowerInformation
        {
            public uint Number;
            public uint MaxMhz;
            public uint CurrentMhz;
            public uint MhzLimit;
            public uint MaxIdleState;
            public uint CurrentIdleState;
        }

        [DllImport(POWRPROF)]
        public static extern uint CallNtPowerInformation(
            int InformationLevel, IntPtr InputBuffer, uint InputBufferLength, 
            IntPtr OutputBuffer, uint OutputBufferLength);

        public const int ProcessorInformation = 11;

        // Helpers
        public static bool ApplyPowerSetting(Guid schemeGuid, Guid subGroup, Guid setting, uint acValue, uint dcValue)
        {
            try
            {
                uint resAc = PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, acValue);
                uint resDc = PowerWriteDCValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, dcValue);
                
                if (resAc != 0)
                    VoltrisOptimizer.App.LoggingService?.LogWarning($"[PowerNative] PowerWriteACValueIndex falhou com código {resAc}");
                if (resDc != 0)
                    VoltrisOptimizer.App.LoggingService?.LogWarning($"[PowerNative] PowerWriteDCValueIndex falhou com código {resDc}");
                    
                return resAc == 0 && resDc == 0;
            }
            catch (Exception ex)
            { 
                VoltrisOptimizer.App.LoggingService?.LogError($"[PowerNative] Erro ao aplicar configuração: {ex.Message}");
                return false; 
            }
        }

        public static uint? GetPowerSettingAC(Guid schemeGuid, Guid subGroup, Guid setting)
        {
            try
            {
                if (PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subGroup, ref setting, out uint val) == 0)
                    return val;
                return null;
            }
            catch (Exception ex)
            {
                VoltrisOptimizer.App.LoggingService?.LogDebug($"[PowerNative] Erro ao ler configuração AC: {ex.Message}");
                return null;
            }
        }

        public static bool TryGetActiveSchemeGuid(out Guid schemeGuid)
        {
            schemeGuid = Guid.Empty;
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out IntPtr guidPtr) == 0 && guidPtr != IntPtr.Zero)
                {
                    schemeGuid = Marshal.PtrToStructure<Guid>(guidPtr);
                    LocalFree(guidPtr);
                    
                    if (!SchemeExists(schemeGuid))
                    {
                        VoltrisOptimizer.App.LoggingService?.LogWarning($"[PowerNative] GUID ativo {schemeGuid} parece inválido - não encontrado na enumeração");
                    }
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                VoltrisOptimizer.App.LoggingService?.LogError($"[PowerNative] Erro ao obter esquema ativo: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// [FIX:UNICO-DONO-DE-ENERGIA] Lê o EPP (Processor Energy Performance
        /// Preference) que está valendo AGORA no esquema ativo.
        ///
        /// Existe para o Brain poder continuar sabendo o estado real depois de
        /// pedir um EPP. O Brain deixou de gravar EPP — quem grava é o Perfil
        /// Inteligente — mas devolver ao Brain o valor que ele pediu seria uma
        /// mentira. O que ele precisa é do valor EFETIVO, e este método é o que
        /// dá essa verdade. Sem ele, o Brain ficaria reportando o próprio
        /// pedido como se fosse medição.
        ///
        /// É leitura pura: não escreve, não muda o plano, não passa pelo portão
        /// de escrita.
        /// </summary>
        /// <param name="epp">EPP efetivo em porcentagem (0 a 100).</param>
        /// <returns><c>true</c> se a leitura foi obtida.</returns>
        public static bool TryReadCurrentEpp(out int epp)
        {
            epp = -1;
            try
            {
                var subProcessor = new Guid("54533251-82be-4824-96c1-47b60b740d00");
                var eppSetting = new Guid("36687f9e-e3a5-4dbf-b1dc-15eb381c6863");

                if (!TryGetActiveSchemeGuid(out Guid activeScheme))
                {
                    return false;
                }

                var value = GetPowerSettingAC(activeScheme, subProcessor, eppSetting);
                if (value == null)
                {
                    return false;
                }

                epp = (int)value.Value;
                return true;
            }
            catch (Exception ex)
            {
                VoltrisOptimizer.App.LoggingService?.LogDebug($"[PowerNative] Erro ao ler EPP atual: {ex.Message}");
                return false;
            }
        }

        public static bool ApplyCurrentPowerScheme()
        {
            try
            {
                if (TryGetActiveSchemeGuid(out Guid guid))
                    return PowerSetActiveScheme(IntPtr.Zero, ref guid) == 0;
                return false;
            }
            catch { return false; }
        }

        public static string GetPlanName(Guid schemeGuid)
        {
            try
            {
                uint bufferSize = 0;
                PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref bufferSize);
                if (bufferSize > 0)
                {
                    IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
                    try
                    {
                        if (PowerReadFriendlyName(IntPtr.Zero, ref schemeGuid, IntPtr.Zero, IntPtr.Zero, buffer, ref bufferSize) == 0)
                        {
                            return Marshal.PtrToStringUni(buffer) ?? string.Empty;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
                return string.Empty;
            }
            catch { return string.Empty; }
        }

        public static bool SchemeExists(Guid schemeGuid)
        {
            try
            {
                if (schemeGuid == Guid.Empty)
                    return false;

                uint bufferSize = 0;
                uint index = 0;
                const uint ACCESS_SCHEME = 16;

                while (true)
                {
                    bufferSize = 1024;
                    IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
                    try
                    {
                        uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, index, buffer, ref bufferSize);
                        
                        if (result != 0)
                            break;

                        if (bufferSize >= 16)
                        {
                            Guid enumeratedGuid = Marshal.PtrToStructure<Guid>(buffer);
                            if (enumeratedGuid == schemeGuid)
                                return true;
                        }

                        index++;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }

                return false;
            }
            catch { return false; }
        }

        public static bool TryGetValidScheme(Guid requestedGuid, out Guid validGuid)
        {
            validGuid = Guid.Empty;

            if (requestedGuid != Guid.Empty && SchemeExists(requestedGuid))
            {
                validGuid = requestedGuid;
                return true;
            }

            // 1. Tentar encontrar o plano Balanced nativo se ele existir no sistema
            Guid balancedGuid = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e");
            if (SchemeExists(balancedGuid))
            {
                validGuid = balancedGuid;
                return true;
            }

            // 2. Tentar encontrar qualquer plano Voltris ou o primeiro plano da enumeração
            try
            {
                uint bufferSize = 0;
                uint index = 0;
                const uint ACCESS_SCHEME = 16;
                Guid firstEnumeratedGuid = Guid.Empty;

                while (true)
                {
                    bufferSize = 1024;
                    IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
                    try
                    {
                        uint result = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, index, buffer, ref bufferSize);

                        if (result != 0)
                            break;

                        if (bufferSize >= 16)
                        {
                            Guid enumeratedGuid = Marshal.PtrToStructure<Guid>(buffer);
                            if (firstEnumeratedGuid == Guid.Empty)
                            {
                                firstEnumeratedGuid = enumeratedGuid;
                            }

                            string planName = GetPlanName(enumeratedGuid);

                            if (planName.IndexOf("Voltris", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                validGuid = enumeratedGuid;
                                return true;
                            }
                        }

                        index++;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }

                if (firstEnumeratedGuid != Guid.Empty)
                {
                    validGuid = firstEnumeratedGuid;
                    return true;
                }

                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// [FIX:POWER-TIER] Lê um valor do esquema de energia ATIVO, na linha AC.
        ///
        /// ESTE MÉTODO NÃO EXISTIA, e a ausência dele é parte do problema que
        /// estamos resolvendo: sem poder LER, o app não pode (a) descobrir a
        /// política de resfriamento da máquina, (b) mostrar o "antes" no log e
        /// (c) confirmar depois que a escrita foi aceita. Gravar sem ler é
        /// escrever no escuro — foi assim que os perfis passaram anos sem valor
        /// verificável.
        ///
        /// Devolve `null` quando não há valor para o par
        /// (esquema/grupo/setting) — o que é normal: many settings de
        /// refinamento não existem em todos os planos, e `null` é tratado como
        /// "usar o padrão do Windows" pelo chamador, nunca como erro.
        /// </summary>
        public static uint? TryReadActiveAcValueIndex(Guid settingGuid, Guid subgroupGuid)
        {
            return TryReadValueIndex(settingGuid, subgroupGuid, active: true);
        }

        /// <summary>
        /// [FIX:POWER-TIER] O mesmo, na linha DC (na bateria).
        ///
        /// Existe separado do AC de propósito: o defeito mais grave que
        /// encontramos é o app gravar o MESMO valor nos dois lados. Num
        /// notebook, "EPP 0" na tomada e "EPP 0" na bateria é a mesma coisa —
        /// e é exatamente o que destrói a autonomia quando o usuário escolhe
        /// "Performance Extrema" e vai para orus.
        /// </summary>
        public static uint? TryReadActiveDcValueIndex(Guid settingGuid, Guid subgroupGuid)
        {
            return TryReadValueIndex(settingGuid, subgroupGuid, active: false);
        }

        /// <summary>
        /// [FIX:POWER-MATRIX] Lê um valor de um esquema ESPECÍFICO, na linha AC.
        ///
        /// Diferente de <see cref="TryReadActiveAcValueIndex"/>, que lê do plano
        /// ativo: aqui o esquema é informado. Isso é indispensável para a
        /// verificação pós-escrita — o plano que acabamos de gravar pode ainda
        /// não ser o ativo, e ler o plano ativo daria o valor errado, fazendo o
        /// app concluir que "confirmou" uma gravação que não gravou.
        /// </summary>
        public static uint? TryReadSchemeAcValueIndex(Guid schemeGuid, Guid settingGuid, Guid subgroupGuid)
        {
            try
            {
                Guid s = schemeGuid, g = subgroupGuid, set = settingGuid;
                uint res = PowerReadACValueIndex(IntPtr.Zero, ref s, ref g, ref set, out uint value);
                if (res != 0) return null;
                return value;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// [FIX:POWER-MATRIX] O mesmo, na linha DC. Ver a observação sobre
        /// <see cref="TryReadSchemeAcValueIndex"/> sobre ler o esquema certo.
        /// </summary>
        public static uint? TryReadSchemeDcValueIndex(Guid schemeGuid, Guid settingGuid, Guid subgroupGuid)
        {
            try
            {
                Guid s = schemeGuid, g = subgroupGuid, set = settingGuid;
                uint res = PowerReadDCValueIndex(IntPtr.Zero, ref s, ref g, ref set, out uint value);
                if (res != 0) return null;
                return value;
            }
            catch
            {
                return null;
            }
        }

        private static uint? TryReadValueIndex(Guid settingGuid, Guid subgroupGuid, bool active)
        {
            try
            {
                IntPtr schemePtr = IntPtr.Zero;
                try
                {
                    uint res = PowerGetActiveScheme(IntPtr.Zero, out schemePtr);
                    if (res != 0 || schemePtr == IntPtr.Zero)
                    {
                        VoltrisOptimizer.App.LoggingService?.LogDebug(
                            $"[PowerRead] PowerGetActiveScheme falhou (res={res}); sem leitura para {settingGuid}.");
                        return null;
                    }

                    var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
                    return active
                        ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subgroupGuid, ref settingGuid, out uint ac)
                        : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subgroupGuid, ref settingGuid, out uint dc);
                }
                finally
                {
                    if (schemePtr != IntPtr.Zero) LocalFree(schemePtr);
                }
            }
            catch (Exception ex)
            {
                VoltrisOptimizer.App.LoggingService?.LogDebug(
                    $"[PowerRead] Excecao ao ler {settingGuid} (AC={active}): {ex.Message}");
                return null;
            }
        }
    }
}
