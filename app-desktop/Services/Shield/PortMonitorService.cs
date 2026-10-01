using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;
using VoltrisOptimizer.Utils;

namespace VoltrisOptimizer.Services.Shield
{
    /// <summary>
    /// PortMonitorService — Monitoramento inteligente de portas TCP/UDP e conexões ativas.
    /// Detecta conexões suspeitas, portas de Metasploit/reverse shell, e IPs maliciosos.
    /// </summary>
    public class PortMonitorService : IDisposable
    {
        private readonly ILoggingService _logger;
        private readonly SecurityLogService _securityLog;
        private Timer? _scanTimer;
        private bool _isMonitoring;
        private bool _lowActivityMode;
        private bool _disposed;

        // Portas comumente usadas por Metasploit/reverse shells
        private static readonly HashSet<int> MetasploitPorts = new()
        {
            4444, 4445, 4446, 5555, 5556, 8080, 8443, 8888, 9999,
            1234, 1337, 31337, 6666, 6667, 7777, 12345, 54321,
            2222, 3333, 4321, 5432, 6789, 9876, 1080, 3128
        };

        // Portas legítimas que não devem gerar alerta
        private static readonly HashSet<int> SafePorts = new()
        {
            80, 443, 53, 22, 21, 25, 110, 143, 993, 995, 587, 465,
            3389, 5900, 8080, 27015, 27016, 27017, // Steam/jogos
            3478, 3479, 3480, // STUN/TURN (VoIP, jogos)
            9100, 515, 631, // Impressoras
            5353, // mDNS
            1900, // UPnP
            137, 138, 139, 445 // SMB/NetBIOS
        };

        // IPs de redes privadas (não suspeitos)
        private static readonly string[] PrivateRanges = { "10.", "172.16.", "172.17.", "172.18.",
            "172.19.", "172.20.", "172.21.", "172.22.", "172.23.", "172.24.", "172.25.",
            "172.26.", "172.27.", "172.28.", "172.29.", "172.30.", "172.31.",
            "192.168.", "127.", "::1", "0.0.0.0" };

        private readonly ConcurrentDictionary<string, ConnectionRecord> _activeConnections = new();
        private readonly ConcurrentDictionary<string, int> _suspiciousIpCounts = new();

        public event EventHandler<SuspiciousConnectionEventArgs>? SuspiciousConnectionDetected;
        public event EventHandler<ConnectionSnapshot>? SnapshotUpdated;

        public bool IsMonitoring => _isMonitoring;

        public PortMonitorService(ILoggingService logger, SecurityLogService securityLog)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _securityLog = securityLog ?? throw new ArgumentNullException(nameof(securityLog));
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            _logger.LogInfo("[PortMonitor] ══════════════════════════════════════════");
            _logger.LogInfo("[PortMonitor] Iniciando monitoramento de portas e conexões...");

            var interval = _lowActivityMode ? 30000 : 10000; // 30s gamer, 10s normal
            _scanTimer = new Timer(ScanConnections, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
            _isMonitoring = true;

            _securityLog.LogSecurityEvent("PortMonitor", "MONITORING_STARTED", "TCP/UDP connection monitoring active");
            _logger.LogSuccess("[PortMonitor] Monitoramento de portas ativo");
            _logger.LogInfo("[PortMonitor] ══════════════════════════════════════════");
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            _scanTimer?.Dispose();
            _scanTimer = null;
            _isMonitoring = false;
            _activeConnections.Clear();

            _securityLog.LogSecurityEvent("PortMonitor", "MONITORING_STOPPED", "Connection monitoring disabled");
            _logger.LogInfo("[PortMonitor] Monitoramento de portas parado");
        }

        public void SetLowActivityMode(bool enabled)
        {
            _lowActivityMode = enabled;
            if (_isMonitoring && _scanTimer != null)
            {
                var interval = enabled ? 30000 : 10000;
                _scanTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
            }
            _logger.LogInfo($"[PortMonitor] Modo baixa atividade: {enabled}");
        }

        private void ScanConnections(object? state)
        {
            try
            {
                var tcpConnections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();

                // Cache process mapping for this scan to avoid multiple netstat calls
                var portMap = GetPortProcessMap();

                var snapshot = new ConnectionSnapshot
                {
                    Timestamp = DateTime.Now,
                    TotalConnections = tcpConnections.Length,
                    TotalListeners = listeners.Length
                };

                var newConnections = new HashSet<string>();

                foreach (var conn in tcpConnections)
                {
                    var key = $"{conn.LocalEndPoint}->{conn.RemoteEndPoint}";
                    newConnections.Add(key);

                    var remoteIp = conn.RemoteEndPoint.Address.ToString();
                    var remotePort = conn.RemoteEndPoint.Port;
                    var localPort = conn.LocalEndPoint.Port;

                    if (!_activeConnections.TryGetValue(key, out var record))
                    {
                        record = new ConnectionRecord
                        {
                            LocalEndpoint = conn.LocalEndPoint.ToString(),
                            RemoteEndpoint = conn.RemoteEndPoint.ToString(),
                            RemoteIp = remoteIp,
                            RemotePort = remotePort,
                            LocalPort = localPort,
                            State = conn.State.ToString(),
                            FirstSeen = DateTime.Now,
                            ProcessName = portMap.TryGetValue(localPort, out var pName) ? pName : "Desconhecido"
                        };

                        _activeConnections.TryAdd(key, record);

                        var suspicion = AnalyzeConnection(record);
                        if (suspicion != SuspicionLevel.None)
                        {
                            record.SuspicionLevel = suspicion;
                            snapshot.SuspiciousConnections.Add(record);

                            _logger.LogWarning($"[PortMonitor] ⚠️ Conexão suspeita: {record.ProcessName} " +
                                $"-> {remoteIp}:{remotePort} (Nível: {suspicion})");

                            _securityLog.LogSecurityEvent("PortMonitor", "SUSPICIOUS_CONNECTION",
                                $"Process={record.ProcessName} | Remote={remoteIp}:{remotePort} | " +
                                $"Local=:{localPort} | Level={suspicion}");

                            SuspiciousConnectionDetected?.Invoke(this, new SuspiciousConnectionEventArgs
                            {
                                Connection = record,
                                Level = suspicion,
                                Reason = GetSuspicionReason(record, suspicion)
                            });
                        }
                    }
                    else
                    {
                        // Update state if changed
                        record.State = conn.State.ToString();
                    }
                }

                // Detectar conexões encerradas
                var closedKeys = _activeConnections.Keys.Except(newConnections).ToList();
                foreach (var closedKey in closedKeys)
                {
                    if (_activeConnections.TryRemove(closedKey, out var closedRecord)
                        && !IsSafeIp(closedRecord.RemoteIp)
                        && !MetasploitPorts.Contains(closedRecord.RemotePort)
                        && !MetasploitPorts.Contains(closedRecord.LocalPort))
                    {
                        if (_suspiciousIpCounts.TryGetValue(closedRecord.RemoteIp, out var currentCount))
                        {
                            if (currentCount <= 1)
                                _suspiciousIpCounts.TryRemove(closedRecord.RemoteIp, out _);
                            else
                                _suspiciousIpCounts.TryUpdate(closedRecord.RemoteIp, currentCount - 1, currentCount);
                        }
                    }
                }

                // Verificar listeners em portas de Metasploit
                foreach (var listener in listeners)
                {
                    if (MetasploitPorts.Contains(listener.Port) && !SafePorts.Contains(listener.Port))
                    {
                        var processName = portMap.TryGetValue(listener.Port, out var pName) ? pName : "Desconhecido";
                        snapshot.SuspiciousListeners.Add(new ListenerRecord
                        {
                            Port = listener.Port,
                            Address = listener.Address.ToString(),
                            ProcessName = processName
                        });

                        _logger.LogWarning($"[PortMonitor] ⚠️ Listener em porta suspeita: :{listener.Port} ({processName})");
                    }
                }

                snapshot.ActiveConnections = _activeConnections.Values.ToList();
                snapshot.AllConnections = _activeConnections.Values.ToList();
                SnapshotUpdated?.Invoke(this, snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PortMonitor] Erro no scan: {ex.Message}");
            }
        }

        private SuspicionLevel AnalyzeConnection(ConnectionRecord conn)
        {
            if (IsSafeIp(conn.RemoteIp))
                return SuspicionLevel.None;

            if (SafePorts.Contains(conn.RemotePort) || SafePorts.Contains(conn.LocalPort))
                return SuspicionLevel.None;

            if (MetasploitPorts.Contains(conn.RemotePort))
                return SuspicionLevel.High;

            if (MetasploitPorts.Contains(conn.LocalPort))
                return SuspicionLevel.Critical;

            var count = _suspiciousIpCounts.AddOrUpdate(conn.RemoteIp, 1, (_, c) => c + 1);
            if (count > 30) 
                return SuspicionLevel.Medium;

            return SuspicionLevel.None;
        }

        private string GetSuspicionReason(ConnectionRecord conn, SuspicionLevel level)
        {
            if (MetasploitPorts.Contains(conn.LocalPort))
                return $"Porta local :{conn.LocalPort} é comumente usada por reverse shells (Metasploit/Meterpreter)";
            if (MetasploitPorts.Contains(conn.RemotePort))
                return $"Porta remota :{conn.RemotePort} é comumente usada por C2 servers (Metasploit)";
            if (_suspiciousIpCounts.TryGetValue(conn.RemoteIp, out var count) && count > 20)
                return $"IP {conn.RemoteIp} tem {count} conexões ativas (possível Command & Control)";
            return "Conexão com padrão suspeito";
        }

        private static bool IsSafeIp(string ip)
        {
            if (PrivateRanges.Any(r => ip.StartsWith(r))) return true;

            var trustedRanges = new[] { 
                "149.154.", "91.108.", // Telegram
                "185.60.", "157.240.", // Meta
                "142.250.", "172.217.", "216.58.", // Google
                "13.107.", "20.190.", "40.126.", // Microsoft
                "104.16.", "104.17.", "104.18.", // Cloudflare
                "162.159.", "185.199." // Discord / GitHub
            };

            return trustedRanges.Any(r => ip.StartsWith(r));
        }

        private readonly ConcurrentDictionary<int, string> _processNameCache = new();

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tcpClass, int reserved);

        private const int AF_INET = 2;       // IPv4
        private const int AF_INET6 = 23;     // IPv6
        private const int TCP_TABLE_OWNER_PID_ALL = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCPROW_OWNER_PID
        {
            public uint state;
            public uint localAddr;
            public uint localPort;
            public uint remoteAddr;
            public uint remotePort;
            public uint owningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MIB_TCP6ROW_OWNER_PID
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] localAddr;
            public uint localScopeId;
            public uint localPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public byte[] remoteAddr;
            public uint remoteScopeId;
            public uint remotePort;
            public uint state;
            public uint owningPid;
        }

        /// <summary>
        /// Obtém o mapeamento de Portas para Nomes de Processo de forma eficiente usando P/Invoke.
        /// Zero overhead de disco ou CPU comparado ao processo netstat legad.
        /// </summary>
        private Dictionary<int, string> GetPortProcessMapNative()
        {
            var map = new Dictionary<int, string>();
            PopulateTcpTableNative(map, AF_INET);
            PopulateTcpTableNative(map, AF_INET6);
            return map;
        }

        private void PopulateTcpTableNative(Dictionary<int, string> map, int ipVersion)
        {
            int bufferSize = 0;
            // Primeira chamada apenas para medir o tamanho do buffer necessário
            _ = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, ipVersion, TCP_TABLE_OWNER_PID_ALL, 0);

            if (bufferSize == 0) return;

            IntPtr pTable = Marshal.AllocHGlobal(bufferSize);
            try
            {
                uint result = GetExtendedTcpTable(pTable, ref bufferSize, false, ipVersion, TCP_TABLE_OWNER_PID_ALL, 0);
                if (result != 0) // NO_ERROR == 0
                {
                    if (result == 122) // ERROR_INSUFFICIENT_BUFFER (bufferSize pode ter mudado no meio tempo)
                    {
                        Marshal.FreeHGlobal(pTable);
                        pTable = Marshal.AllocHGlobal(bufferSize);
                        result = GetExtendedTcpTable(pTable, ref bufferSize, false, ipVersion, TCP_TABLE_OWNER_PID_ALL, 0);
                    }
                    if (result != 0) return; // Aborta nativo em caso de qualquer outro erro
                }

                int rowCount = Marshal.ReadInt32(pTable);
                IntPtr currentRow = pTable + 4; // Deslocamento após o "dwNumEntries" (DWORD / 4 bytes)

                bool isIpv4 = (ipVersion == AF_INET);
                int rowSize = isIpv4 ? Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID)) : Marshal.SizeOf(typeof(MIB_TCP6ROW_OWNER_PID));

                for (int i = 0; i < rowCount; i++)
                {
                    uint pid = 0;
                    uint portRaw = 0;

                    if (isIpv4)
                    {
                        var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(currentRow);
                        pid = row.owningPid;
                        portRaw = row.localPort;
                    }
                    else
                    {
                        var row = Marshal.PtrToStructure<MIB_TCP6ROW_OWNER_PID>(currentRow);
                        pid = row.owningPid;
                        portRaw = row.localPort;
                    }

                    // Network Byte Order (Big Endian) para Host Byte Order na localPort
                    int port = (int)(((portRaw & 0xFF) << 8) | ((portRaw & 0xFF00) >> 8));

                    if (port > 0 && pid > 0 && !map.ContainsKey(port))
                    {
                        if (!_processNameCache.TryGetValue((int)pid, out string? pName))
                        {
                            try
                            {
                                // [FIX:NULL-PORTMONITOR] O PROCESSO PODE JÁ TER MORRIDO.
                                //
                                // `SafeProcess.TryGet` devolve `null` quando o PID não
                                // existe mais — e isso é o caso NORMAL aqui, não uma
                                // exceção. A tabela TCP do sistema é lida num
                                // instante; o processo que tinha a conexão um
                                // milissegundo antes pode ter encerrado, e o
                                // Windows ainda mantém a linha na tabela até a
                                // próxima limpeza.
                                //
                                // O código acessava `p.ProcessName` sem verificar
                                // `p`, e o resultado era a exceção registrada no log
                                // do usuário a cada varredura:
                                //
                                //     NullReferenceException em
                                //     PortMonitorService.PopulateTcpTableNative
                                //
                                // O efeito era pior que o log: a exceção saía no
                                // meio do preenchimento do mapa, o `catch` do
                                // chamador abortava a leitura INTEIRA, e a lista de
                                // conexões do Shield ficava incompleta. Uma
                                // conexão encerrada fazia o painel inteiro não
                                // atualizar.
                                using var p = SafeProcess.TryGet((int)pid);

                                if (p != null)
                                {
                                    pName = p.ProcessName;
                                    if (!string.IsNullOrEmpty(pName))
                                    {
                                        _processNameCache.TryAdd((int)pid, pName);
                                    }
                                }
                            }
                            catch
                            {
                                pName = $"PID:{pid}";
                            }
                        }

                        // [FIX:NULL-PORTMONITOR] NOME NULO NÃO PODE ENTRAR NO MAPA.
                        //
                        // Mesmo com a guarda acima, `pName` pode chegar vazio
                        // se o cache tiver uma entrada sem nome. Um valor nulo
                        // aqui voltaria para a UI, que a exibe como coluna
                        // "Processo" — e a aba inteiro ficaria com uma célula
                        // em branco sem explicação.
                        if (string.IsNullOrEmpty(pName))
                        {
                            pName = $"PID:{pid}";
                        }

                        map[port] = pName;
                    }

                    currentRow += rowSize;
                }
            }
            finally
            {
                // Liberação vital para impedir memory leaks!
                Marshal.FreeHGlobal(pTable);
            }
        }

        /// <summary>
        /// Obtém o mapeamento usando C/C++ P/Invoke Primário e fallback automático para netstat.
        /// </summary>
        private Dictionary<int, string> GetPortProcessMap()
        {
            try
            {
                var nativeMap = GetPortProcessMapNative();
                if (nativeMap.Count > 0)
                {
                    return nativeMap;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PortMonitor] Fallback acionado. Erro no mapeamento Win32: {ex.Message}");
            }

            // Fallback (Método Original / Legado Seguro)
            var map = new Dictionary<int, string>();
            try
            {
                var psi = new ProcessStartInfo("netstat", "-ano")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return map;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 5) continue;

                    string lastPart = parts[^1];
                    if (int.TryParse(lastPart, out int pid) && pid > 0)
                    {
                        var localAddr = parts[1];
                        int portIndex = localAddr.LastIndexOf(':');
                        if (portIndex >= 0 && int.TryParse(localAddr.Substring(portIndex + 1), out int port))
                        {
                            if (!_processNameCache.TryGetValue(pid, out string? pName))
                            {
                                try
                                {
                                    using var p = SafeProcess.TryGet(pid);
                                    pName = p.ProcessName;
                                    _processNameCache.TryAdd(pid, pName);
                                }
                                catch
                                {
                                    pName = $"PID:{pid}";
                                }
                            }
                            map[port] = pName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[PortMonitor] Erro ao obter mapa de portas: {ex.Message}");
            }
            return map;
        }

        public ConnectionSnapshot GetCurrentSnapshot()
        {
            var active = _activeConnections.Values.ToList();
            return new ConnectionSnapshot
            {
                Timestamp = DateTime.Now,
                ActiveConnections = active,
                AllConnections = active,
                SuspiciousConnections = active.Where(c => c.SuspicionLevel != SuspicionLevel.None).ToList(),
                TotalConnections = active.Count
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopMonitoring();
            _scanTimer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    #region Models

    public enum SuspicionLevel { None, Low, Medium, High, Critical }

    public class ConnectionRecord
    {
        public string LocalEndpoint { get; set; } = string.Empty;
        public string RemoteEndpoint { get; set; } = string.Empty;
        public string RemoteIp { get; set; } = string.Empty;
        public int RemotePort { get; set; }
        public int LocalPort { get; set; }
        public string State { get; set; } = string.Empty;
        public string? ProcessName { get; set; }
        public DateTime FirstSeen { get; set; }
        public SuspicionLevel SuspicionLevel { get; set; }
    }

    public class ListenerRecord
    {
        public int Port { get; set; }
        public string Address { get; set; } = string.Empty;
        public string? ProcessName { get; set; }
    }

    public class ConnectionSnapshot
    {
        public DateTime Timestamp { get; set; }
        public int TotalConnections { get; set; }
        public int TotalListeners { get; set; }
        public List<ConnectionRecord> ActiveConnections { get; set; } = new();
        public List<ConnectionRecord> AllConnections { get; set; } = new();
        public List<ConnectionRecord> SuspiciousConnections { get; set; } = new();
        public List<ListenerRecord> SuspiciousListeners { get; set; } = new();
    }

    public class SuspiciousConnectionEventArgs : EventArgs
    {
        public ConnectionRecord Connection { get; set; } = new();
        public SuspicionLevel Level { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    #endregion
}
