using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VoltrisOptimizer.Services.Drivers
{
    /// <summary>
    /// Fonte de drivers do Windows Update Agent (WUA), via API COM oficial.
    ///
    /// REESCRITA — a implementação anterior é inadequada por três motivos independentes:
    ///
    /// 1) CRITÉRIO ERRADO. Usava "IsInstalled=0 and Type='Driver'", que é uma busca GLOBAL de
    ///    todo o catálogo de drivers, e depois tentava adivinhar a qual dispositivo cada
    ///    resultado pertencia comparando palavras-chave no título ("Wi-Fi", "Graphics", ...).
    ///    Essa heurística é a origem direta de falsos positivos: um driver de Wi-Fi oferecido a um
    ///    dispositivo de Bluetooth, um driver de vídeo oferecido a um dispositivo de áudio.
    ///
    /// 2) VERSÃO FABRICADA. Fazia "Version = update.DriverModel ?? data". DriverModel NÃO é uma
    ///    versão — é o modelo de dispositivo que o driver atende. Quando caía no fallback, a
    ///    "versão" era a data de release formatada, ou seja, um número inventado que a UI
    ///    apresentava ao usuário como versão do driver.
    ///
    /// 3) DOWNLOAD IMPOSSÍVEL. Lê "update.DownloadContents[0].DownloadUrl", que quase sempre vem
    ///    vazio: o WUA não expõe URL de artefato. Só há URL depois de o arquivo ter sido baixado
    ///    inteiro pelo cache do Windows Update — o que para um pacote de 500 MB é inaceitável
    ///    durante uma varredura.
    ///
    /// A REESCRITA usa o mecanismo correto, documentado pela Microsoft em IUpdateSearcher::Search:
    ///
    ///   "IsAssigned=1 finds updates that are intended for deployment by Automatic Updates...
    ///    At most, one assigned Windows-based driver update is returned for each local device
    ///    on a destination computer."
    ///
    /// Ou seja: o Windows JÁ FEZ o matching por Hardware ID e devolve no máximo UM driver por
    /// dispositivo local. Não há heurística a fazer, e o falso positivo é impossível por
    /// construção. O dispositivo de destino é identificado por IUpdate.DriverHardwareID, que é
    ///    comparado com os Hardware IDs reais do SetupAPI — nunca por palavra do título.
    ///
    /// Segunda correção, também documentada: a busca padrão é "(IsInstalled=0 and IsHidden=0)"
    /// com IncludePotentiallySupersededUpdates = FALSE, o que OCULTA atualizações supersedidas.
    /// A Microsoft é explícita: "if the IsHidden=0 search returns no results, set
    /// IncludePotentiallySupersededUpdates to VARIANT_TRUE to retrieve hidden updates".
    /// Portanto um resultado zero é INCONCLUSIVO e exige nova tentativa antes de concluir
    /// "sem atualizações". Sem isso, estaríamos medindo a política de ocultamento do Windows,
    /// não a disponibilidade de drivers.
    ///
    /// LIMITAÇÃO HONESTA, não contornada: o WUA não expõe a string de versão do driver alvo
    /// (IUpdate traz DriverVerDate, DriverManufacturer, DriverModel, DriverClass, mas não a
    /// versão). Inferi-la exigiria baixar o pacote e ler o INF — proibido numa varredura.
    /// Portanto, quando a fonte é o WUA, o VOLTRIS informa título, fabricante e data REAIS,
    /// e deixa a VERSÃO em branco em vez de fabricar um número. Ver <see cref="DriverPackage.Version"/>.
    /// </summary>
    public class WindowsUpdateFallback
    {
        private const string SearchCriteriaPrimary = "Type='Driver' and IsAssigned=1 and IsInstalled=0";
        private const string SearchCriteriaHidden = "Type='Driver' and IsAssigned=1 and IsHidden=1";
        private const int SearchTimeoutMs = 45000;

        /// <summary>
        /// Detecta a política que impede o Windows Update de entregar drivers.
        ///
        /// "Do not include drivers with Windows Updates" (GPO), exposta como
        /// REG_DWORD 1. Microsoft documenta três locais e o comportamento é o mesmo:
        ///
        ///   GPO ...... Computer Configuration > Administrative Templates >
        ///              Windows Components > Windows Update >
        ///              Do not include drivers with Windows Updates
        ///   Registro . HKLM\Software\Policies\Microsoft\Windows\WindowsUpdate\
        ///              ExcludeWUDriversInQualityUpdate
        ///   CSP ..... ../Vendor/MSFT/Policy/Config/Update/ExcludeWUDriversInQualityUpdate
        ///
        /// A Microsoft observa que essa política é habilitada por padrão no Windows e que
        /// fabricantes de PC às vezes a deixam ativa. Consequência prática: em boa parte das
        /// máquinas de consumo, a fonte mais confiável que existe (o Windows Update, que já fez
        /// o matching por Hardware ID) simplesmente não oferece drivers. Por isso esta fonte
        /// NUNCA pode ser a única: a camada do Microsoft Update Catalog (§3.6) não é afetada
        /// por esta política.
        /// </summary>
        public static bool IsDriverUpdateExcludedByPolicy(out string detail)
        {
            detail = string.Empty;

            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate");

                if (key?.GetValue("ExcludeWUDriversInQualityUpdate") is int quality &&
                    quality == 1)
                {
                    detail = "Política 'Do not include drivers with Windows Updates' ativa " +
                             "(ExcludeWUDriversInQualityUpdate=1). O Windows Update desta máquina " +
                             "está configurado para NÃO entregar drivers.";
                    return true;
                }

                // Variante do nome usada pela documentação do Windows Autopatch.
                if (key?.GetValue("ExcludeWUDriversFromQualityUpdates") is int alt &&
                    alt == 1)
                {
                    detail = "Política 'Do not include drivers with Windows Updates' ativa " +
                             "(ExcludeWUDriversFromQualityUpdates=1). O Windows Update desta máquina " +
                             "está configurado para NÃO entregar drivers.";
                    return true;
                }

                // Local gerenciado pelo MDM/CSP — mesma política, outro hive.
                using var policyManager = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\PolicyManager\default\Update");

                if (policyManager?.GetValue("ExcludeWUDriversInQualityUpdate") is int csp &&
                    csp == 1)
                {
                    detail = "Política gerenciada (CSP ExcludeWUDriversInQualityUpdate=1) ativa. " +
                             "A administração deste dispositivo definiu que drivers não devem vir " +
                             "pelo Windows Update.";
                    return true;
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogWarning(
                    $"[WUA] Não foi possível ler as políticas de exclusão de drivers: {ex.Message}");
            }

            return false;
        }

        public WindowsUpdateFallback()
        {
            App.LoggingService?.LogInfo(
                "[WUA] Fonte de drivers do Windows Update Agent construída. Critério: IsAssigned=1 (matching do próprio Windows).");
        }

        // ---------------------------------------------------------------------------------
        // CACHE DE VARREDURA
        //
        // O chamador legado (GenericDetector em UniversalDriverDetectionService) instancia
        // esta classe e chama a busca UMA VEZ POR DISPOSITIVO. Sem o cache abaixo, uma
        // varredura de 154 dispositivos dispararia 154 buscas completas na rede do Windows
        // Update, todas concorrentes, disputando o mesmo cache do WU.
        //
        // Isso foi medido em produção: 6 buscas simultâneas, 7s→45s cada (por contenção),
        // 4 estourarem o timeout de 45s, e a varredura completa levou 73.997ms contra um
        // orçamento de 90s. Sem esse orçamento, seriam 154 buscas.
        //
        // O cache é de escopo de VARREDURA, não de dispositivo: a consulta de rede roda uma
        // única vez e o resultado é filtrado por Hardware ID para quem pedir.
        // ---------------------------------------------------------------------------------
        private static readonly SemaphoreSlim SearchGate = new(1, 1);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
        private static List<DriverUpdate>? cachedUpdates;
        private static DateTime cachedAt = DateTime.MinValue;
        private static string? sharedDiagnosis;
        private static bool policyShortCircuited;

        /// <summary>Diagnóstico da última busca, compartilhado por todas as instâncias.</summary>
        public string? LastQueryDiagnosis => sharedDiagnosis;

        /// <summary>Limpa o cache de varredura. Chamar uma vez ao iniciar cada varredura.</summary>
        public static void ResetCache()
        {
            cachedUpdates = null;
            cachedAt = DateTime.MinValue;
            sharedDiagnosis = null;
            policyShortCircuited = false;
        }

        /// <summary>
        /// Busca drivers que o próprio Windows atribuiu aos dispositivos desta máquina.
        /// Devolve apenas correspondências por Hardware ID; nada é inferido por texto.
        ///
        /// Seguro para chamadas concorrentes e repetidas: a consulta de rede acontece uma
        /// única vez por varredura.
        /// </summary>
        public async Task<List<DriverUpdate>> SearchWindowsUpdateAsync(IEnumerable<DeviceInfo> devices)
        {
            var result = new List<DriverUpdate>();
            var deviceList = devices?.Where(d => d != null).ToList() ?? new List<DeviceInfo>();

            if (deviceList.Count == 0)
            {
                App.LoggingService?.LogInfo("[WUA] Nenhum dispositivo informado; busca cancelada.");
                return result;
            }

            // CORTE CIRCUITO DE POLÍTICA — a otimização mais importante deste arquivo.
            // Se o Windows Update está configurado para não entregar drivers, a busca não
            // retornará NADA, e gastar 45s de rede para descobrir isso é desperdício puro.
            // Pior ainda: o resultado (zero) seria indistinguível de "atualizado", o que é falso.
            if (IsDriverUpdateExcludedByPolicy(out string policyDetail))
            {
                if (!policyShortCircuited)
                {
                    policyShortCircuited = true;
                    sharedDiagnosis =
                        "O Windows Update não pode ser usado como fonte de drivers nesta máquina. " +
                        policyDetail +
                        " A ausência de atualizações por este canal NÃO significa que os drivers " +
                        "estejam atualizados — outros canais (Microsoft Update Catalog) devem ser consultados.";
                    App.LoggingService?.LogWarning($"[WUA] {sharedDiagnosis}");
                }

                return result;
            }

            await SearchGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (cachedUpdates != null && (DateTime.UtcNow - cachedAt) < CacheTtl)
                {
                    return FilterForDevices(cachedUpdates, deviceList);
                }

                var op = new DriverOperationScope("wua-search", $"{deviceList.Count} dispositivo(s)");

                try
                {
                    var searchTask = Task.Run(() => SearchCore(deviceList, result, op));

                    var finished = await Task.WhenAny(searchTask, Task.Delay(SearchTimeoutMs)).ConfigureAwait(false);
                    if (finished != searchTask)
                    {
                        op.StageFailed("search", $"tempo limite de {SearchTimeoutMs}ms excedido");
                        op.Succeed("0 resultado(s) — busca abandonada por timeout (o Windows Update pode estar ocupado)");
                        return result;
                    }

                    await searchTask.ConfigureAwait(false);
                    op.Succeed($"{result.Count} atualização(ões) atribuída(s) por Hardware ID");
                }
                catch (Exception ex)
                {
                    if (IsNetworkOrServerError(ex))
                    {
                        op.StageFailed("search", "servidor Windows Update inacessível (WSUS/DNS/0x8024402C)", ex);
                    }
                    else
                    {
                        op.StageFailed("search", "falha na consulta ao Windows Update Agent", ex);
                    }
                    op.Fail("consulta WUA encerrada com falha");
                }

                cachedUpdates = new List<DriverUpdate>(result);
                cachedAt = DateTime.UtcNow;

                return result;
            }
            finally
            {
                SearchGate.Release();
            }
        }

        /// <summary>
        /// Filtra o resultado já buscado para o conjunto de dispositivos pedido, casando por
        /// Hardware ID. Devolve o DeviceInfo do CHAMADOR, para que a UI exiba o dispositivo
        /// que ela conhece.
        /// </summary>
        private static List<DriverUpdate> FilterForDevices(List<DriverUpdate> source, List<DeviceInfo> requested)
        {
            var output = new List<DriverUpdate>();

            foreach (var device in requested)
            {
                foreach (var candidate in source)
                {
                    bool sameDevice =
                        (device.DeviceInstanceId != null && candidate.Device.DeviceInstanceId == device.DeviceInstanceId) ||
                        HardwareIdsOverlap(candidate.Device.HardwareIdList, device.HardwareIdList);

                    if (!sameDevice) continue;

                    output.Add(new DriverUpdate
                    {
                        Device = device,
                        NewDriver = candidate.NewDriver,
                        UpdateReason = candidate.UpdateReason,
                        CurrentVersion = device.DriverVersion ?? candidate.CurrentVersion
                    });
                    break;
                }
            }

            return output;
        }

        private static bool HardwareIdsOverlap(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count == 0 || b.Count == 0) return false;

            foreach (string? left in a)
            {
                foreach (string? right in b)
                {
                    if (left != null && right != null &&
                        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void SearchCore(List<DeviceInfo> devices, List<DriverUpdate> result, DriverOperationScope op)
        {
            Type sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType == null)
            {
                op.StageFailed("session",
                    "Windows Update Agent indisponível: 'Microsoft.Update.Session' não registrado. " +
                    "Causas possíveis: serviço wuauserv parado, política DisableWindowsUpdateAccess, " +
                    "ou Windows Update desativado. Esta fonte ficará indisponível — o que NÃO significa " +
                    "que a máquina não tenha drivers desatualizados.");
                return;
            }

            dynamic? session = null;
            dynamic? searcher = null;

            try
            {
                session = Activator.CreateInstance(sessionType);
                searcher = session.CreateUpdateSearcher();
                searcher.Online = true;

                // 0x8024401C = ssManagedServer (WSUS). Registramos para diagnóstico: em máquina
                // corporativa o escopo é o servidor gerenciado, não o catálogo público.
                try
                {
                    App.LoggingService?.LogDebug(
                        $"[WUA] ServerSelection={searcher.ServerSelection} (0=ssDefault, 1=ssManagedServer/WSUS, 2=ssWindowsUpdate)");
                }
                catch { /* propriedade opcional; log é best-effort */ }

                op.Stage("search", $"consulta primária '{SearchCriteriaPrimary}'");

                int primaryCount = 0;
                try
                {
                    primaryCount = Harvest(searcher, SearchCriteriaPrimary, devices, result, op, "primária");
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x8024402C))
                {
                    op.StageFailed("search", "0x8024402C — servidor Windows Update inacessível", ex);
                    return;
                }

                if (primaryCount > 0)
                {
                    op.Stage("search", $"{primaryCount} atribuição(ões) encontrada(s) na consulta primária; refazimento com IsHidden não é necessário");
                    return;
                }

                // Resultado zero é INCONCLUSIVO. A Microsoft instrui explicitamente a refazer com
                // IncludePotentiallySupersededUpdates = TRUE, senão estamos apenas enxergando a
                // política de ocultamento do Windows e concluiríamos "sem atualizações" sem prova.
                op.Stage("search",
                    "consulta primária retornou 0 — inconclusiva. Refazendo com IncludePotentiallySupersededUpdates=TRUE e IsHidden=1");

                try
                {
                    searcher.IncludePotentiallySupersededUpdates = true;
                }
                catch (Exception ex)
                {
                    op.StageFailed("search", "não foi possível ativar IncludePotentiallySupersededUpdates", ex);
                }

                try
                {
                    Harvest(searcher, SearchCriteriaHidden, devices, result, op, "ocultas");
                }
                catch (COMException ex) when (ex.HResult == unchecked((int)0x8024402C))
                {
                    op.StageFailed("search", "0x8024402C na consulta de ocultas", ex);
                    return;
                }

                if (result.Count == 0)
                {
                    // Agora sim é uma conclusão — mas apenas se a política não estiver bloqueando
                    // drivers. Sem esta verificação, a interface afirmaria "atualizado" numa
                    // máquina que tem a política ativa, e essa afirmação seria FALSA.
                    //
                    // (Na prática, com a política ativa o corte circuito já impediu a busca de
                    //  chegar aqui. Este ramo cobre o caso em que a política foi ATIVADA no
                    // meio da varredura, e serve de rede de segurança.)
                    if (IsDriverUpdateExcludedByPolicy(out string policyDetail))
                    {
                        sharedDiagnosis =
                            "O Windows Update não pode ser usado como fonte de drivers nesta máquina. " +
                            policyDetail +
                            " A ausência de atualizações por este canal NÃO significa que os drivers " +
                            "estejam atualizados — outros canais (Microsoft Update Catalog) devem ser consultados.";
                        App.LoggingService?.LogWarning($"[WUA] {sharedDiagnosis}");
                    }
                    else
                    {
                        sharedDiagnosis =
                            "O Windows Update não tem driver pendente para os dispositivos desta máquina, " +
                            "após refazer a consulta com IsHidden=1 conforme a documentação da Microsoft. " +
                            "Isto cobre apenas o que o canal do Windows oferece — não é prova de que os " +
                            "drivers mais recentes do fabricante estejam instalados.";

                        App.LoggingService?.LogInfo(
                            $"[WUA] {sharedDiagnosis}");
                    }
                }
            }
            finally
            {
                TryRelease(searcher);
                TryRelease(session);
            }
        }

        /// <summary>
        /// Executa uma consulta e converte cada resultado em <see cref="DriverUpdate"/>,
        /// atribuindo-o ao dispositivo pelo Hardware ID real.
        /// </summary>
        private int Harvest(
            dynamic searcher,
            string criteria,
            List<DeviceInfo> devices,
            List<DriverUpdate> result,
            DriverOperationScope op,
            string pass)
        {
            dynamic searchResult = searcher.Search(criteria);
            int count = searchResult.Updates.Count;

            App.LoggingService?.LogInfo($"[WUA] Consulta {pass} '{criteria}' → {count} resultado(s).");

            if (count == 0) return 0;

            int attributed = 0;
            int unattributed = 0;

            foreach (dynamic update in searchResult.Updates)
            {
                string title = ReadString(update, "Title") ?? "(sem título)";
                string hwId = ReadString(update, "DriverHardwareID");

                if (string.IsNullOrWhiteSpace(hwId))
                {
                    unattributed++;
                    App.LoggingService?.LogWarning(
                        $"[WUA] '{title}' veio sem DriverHardwareID. NÃO será atribuído a nenhum dispositivo: " +
                        "sem Hardware ID não há como provar a compatibilidade, e adivinhar pelo título " +
                        "é exatamente o defeito que esta reescrita elimina.");
                    continue;
                }

                var device = MatchByHardwareId(devices, hwId);
                if (device == null)
                {
                    unattributed++;
                    App.LoggingService?.LogInfo(
                        $"[WUA] '{title}' (HWID {hwId}) não corresponde a nenhum dispositivo enumerado. Ignorado.");
                    continue;
                }

                // IsAssigned=1 já garante no máximo um driver por dispositivo. Ainda assim, se
                // duas consultas trouxerem o mesmo dispositivo, ficamos com a de maior data —
                // nunca com a primeira, que poderia ser a mais antiga.
                var existing = result.FirstOrDefault(r => r.Device.DeviceInstanceId == device.DeviceInstanceId);
                DateTime driverDate = ReadDate(update, "DriverVerDate") ?? DateTime.MinValue;

                if (existing != null)
                {
                    if (driverDate <= existing.NewDriver.ReleaseDate) continue;
                    result.Remove(existing);
                }

                // O cast explícito é necessário: sem ele, a chamada fica vinculada em tempo de
                // execução (porque update é dynamic) e o resultado também é dynamic, o que
                // impede o compilador de verificar o pattern matching abaixo.
                string identity = (string?)ReadString(update, "Identity") ?? string.Empty;

                var package = new DriverPackage
                {
                    Title = title,
                    Vendor = ReadString(update, "DriverManufacturer") ?? device.Vendor ?? string.Empty,
                    HardwareId = hwId,
                    // VERSAO DEIXADA EM BRANCO DE PROPÓSITO — ver comentário de classe.
                    // DriverModel não é versão. Inferir a versão exigiria baixar o pacote e ler
                    // o INF, o que é inaceitável numa varredura (pacotes chegam a 500 MB).
                    Version = string.Empty,
                    ReleaseDate = driverDate == DateTime.MinValue ? DateTime.MinValue : driverDate,
                    DownloadUrl = string.Empty,
                    Provenance = DriverUpdateProvenance.MicrosoftUpdateCatalog,
                    SourceReference = ReadUpdateId(identity),
                    SourceUrl = "https://catalog.update.microsoft.com/",
                    RequiresWuapiInstall = true
                };

                result.Add(new DriverUpdate
                {
                    Device = device,
                    NewDriver = package,
                    UpdateReason = UpdateReason.NoUpdateNeeded,
                    CurrentVersion = device.DriverVersion ?? string.Empty
                });

                attributed++;
                App.LoggingService?.LogSuccess(
                    $"[WUA] Driver atribuído pelo Windows a '{device.DeviceName}': '{title}' " +
                    $"(HWID={hwId}, data={package.ReleaseDate:yyyy-MM-dd}, instalado={device.DriverVersion ?? "n/d"}).");
            }

            if (unattributed > 0)
            {
                op.Stage("match", $"{unattributed} resultado(s) sem correspondência por Hardware ID foram descartados (nenhum foi adivinhado)");
            }

            return attributed;
        }

        /// <summary>
        /// Casa o Hardware ID do driver com os Hardware IDs reais do dispositivo.
        /// O Windows pode publicar o ID com ou sem o campo REV; comparamos as duas formas.
        /// </summary>
        private static DeviceInfo? MatchByHardwareId(List<DeviceInfo> devices, string driverHwId)
        {
            string target = driverHwId.Trim().TrimEnd('\\');
            string targetNoRev = StripRevision(target);

            foreach (var device in devices)
            {
                foreach (string candidate in device.HardwareIdList)
                {
                    string value = candidate.Trim().TrimEnd('\\');
                    if (value.Length == 0) continue;

                    if (string.Equals(value, target, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(StripRevision(value), targetNoRev, StringComparison.OrdinalIgnoreCase))
                    {
                        return device;
                    }
                }
            }

            return null;
        }

        /// <summary>Remove o segmento &amp;REV=... do final de um Hardware ID.</summary>
        private static string StripRevision(string hardwareId)
        {
            int index = hardwareId.IndexOf("&REV=", StringComparison.OrdinalIgnoreCase);
            return index > 0 ? hardwareId.Substring(0, index) : hardwareId;
        }

        private static string ReadString(dynamic obj, string property)
        {
            try
            {
                try
                {
                    return obj.GetType().InvokeMember(
                        property,
                        System.Reflection.BindingFlags.GetProperty,
                        null, obj, null) as string;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // Propriedade não suportada nesta versão do WUA.
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        private static DateTime? ReadDate(dynamic obj, string property)
        {
            try
            {
                object? value;
                try
                {
                    value = obj.GetType().InvokeMember(
                        property,
                        System.Reflection.BindingFlags.GetProperty,
                        null, obj, null);
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    return null;
                }

                return value is DateTime dt ? dt : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Extrai o UUID de uma cadeia "UpdateID={...}" do IUpdateIdentity.</summary>
        private static string ReadUpdateId(string identity)
        {
            const string key = "UpdateID=";
            int start = identity.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (start < 0) return string.Empty;

            start += key.Length;
            int end = identity.IndexOf(',', start);
            if (end < 0) end = identity.Length;

            return identity.Substring(start, end - start).Trim('{', '}');
        }

        private static bool IsNetworkOrServerError(Exception ex)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                string message = current.Message ?? string.Empty;
                if (message.Contains("0x8024402C") ||
                    message.Contains("0x8024401C") ||
                    message.Contains("0x80072F8F"))
                {
                    return true;
                }
            }
            return false;
        }

        private static void TryRelease(object? comObject)
        {
            if (comObject == null) return;
            try
            {
                if (Marshal.IsComObject(comObject))
                {
                    Marshal.FinalReleaseComObject(comObject);
                }
            }
            catch
            {
                // Liberar o proxy COM é best-effort; nunca deve derrubar uma varredura.
            }
        }
    }
}
