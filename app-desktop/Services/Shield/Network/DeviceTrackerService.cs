using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoltrisOptimizer.Interfaces;

namespace VoltrisOptimizer.Services.Shield.Network
{
    public class DeviceTrackerService
    {
        private readonly ILoggingService _logger;
        private readonly DeviceIdentificationEngine _identificationEngine;
        private readonly List<NetworkDevice> _knownDevices;
        private readonly object _knownDevicesLock = new();
        private readonly SemaphoreSlim _identificationSemaphore;
        private string _gatewayIP;

        /// <summary>
        /// Primeira varredura concluída. Antes dela, os dispositivos apenas
        /// existentes são registrados SEM notificar — senão, a cada abertura
        /// do app, todos os dispositivos da rede eram tratados como "novos".
        /// </summary>
        private bool _baselineEstablished;

        /// <summary>
        /// Notificações de conexão/desconexão só têm sentido a partir da
        /// segunda varredura em diante.
        /// </summary>
        public bool BaselineEstablished => _baselineEstablished;

        public event EventHandler<DeviceEventArgs> NewDeviceDetected;
        public event EventHandler<DeviceEventArgs> DeviceDisconnected;
        
        public IReadOnlyList<NetworkDevice> KnownDevices
        {
            get
            {
                lock (_knownDevicesLock)
                {
                    return _knownDevices.ToList().AsReadOnly();
                }
            }
        }
        
        public DeviceTrackerService(ILoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _identificationEngine = new DeviceIdentificationEngine(_logger);
            _knownDevices = new List<NetworkDevice>();
            _identificationSemaphore = new SemaphoreSlim(10, 10); // Máximo 10 identificações simultâneas
        }
        
        public async Task UpdateDevicesAsync(List<NetworkDevice> scannedDevices, string gatewayIP = null)
        {
            try
            {
                _gatewayIP = gatewayIP;
                
                _logger.LogInfo($"[DeviceTracker] Atualizando {scannedDevices.Count} dispositivos...");
                
                // Identificar dispositivos em paralelo com limite de concorrência
                var identificationTasks = scannedDevices.Select(async device =>
                {
                    await _identificationSemaphore.WaitAsync();
                    try
                    {
                        var result = await _identificationEngine.IdentifyDeviceAsync(device, gatewayIP);
                        
                        device.Vendor = Prefer(result.Vendor, device.Vendor);
                        device.DeviceType = Prefer(result.DeviceType, device.DeviceType);
                        device.DeviceCategory = Prefer(result.DeviceCategory, device.DeviceCategory);
                        device.DeviceModel = Prefer(result.DeviceModel, device.DeviceModel);
                        device.OperatingSystem = Prefer(result.OperatingSystem, device.OperatingSystem);
                        device.FriendlyName = Prefer(result.FriendlyName, device.FriendlyName);
                        device.Icon = Prefer(result.Icon, device.Icon);
                        device.IsGateway |= result.IsGateway;
                        device.IsPortable |= result.IsPortable;
                        device.ConfidenceLevel = Math.Max(result.ConfidenceLevel, device.ConfidenceLevel);
                        
                        return device;
                    }
                    finally
                    {
                        _identificationSemaphore.Release();
                    }
                });
                
                var identifiedDevices = await Task.WhenAll(identificationTasks);
                
                // Detectar novos dispositivos (com lock thread-safe)
                var isBaselineScan = !_baselineEstablished;

                lock (_knownDevicesLock)
                {
                    foreach (var device in identifiedDevices)
                    {
                        var existing = _knownDevices.FirstOrDefault(d => d.MacAddress == device.MacAddress);

                        if (existing == null)
                        {
                            // Novo dispositivo
                            device.IsNew = true;
                            _knownDevices.Add(device);

                            // O gateway (roteador) é infraestrutura da própria
                            // rede, não um dispositivo que "entrou". Nunca notifica.
                            bool isGateway = device.IsGateway
                                          || (device.IPAddress != null && _gatewayIP != null
                                              && string.Equals(device.IPAddress, _gatewayIP, StringComparison.OrdinalIgnoreCase));

                            if (isBaselineScan)
                            {
                                // Primeira varredura: registra a linha de base
                                // em silêncio. É o que evita notificar todos os
                                // dispositivos a cada inicialização do app.
                                _logger.LogInfo($"[DeviceTracker] Linha de base: {device.FriendlyName} ({device.DeviceType}) - {device.IPAddress}");
                            }
                            else if (isGateway)
                            {
                                _logger.LogInfo($"[DeviceTracker] Gateway registrado (sem notificação): {device.IPAddress}");
                            }
                            else
                            {
                                _logger.LogSuccess($"[DeviceTracker] Novo dispositivo: {device.FriendlyName} ({device.DeviceType}) - {device.IPAddress}");
                                NewDeviceDetected?.Invoke(this, new DeviceEventArgs { Device = device });
                            }
                        }
                        else
                        {
                            // Atualizar dispositivo existente
                            existing.IsOnline = true;
                            existing.LastSeen = DateTime.Now;
                            existing.IPAddress = device.IPAddress;
                            existing.Hostname = device.Hostname;
                            existing.Vendor = device.Vendor;
                            existing.DeviceType = device.DeviceType;
                            existing.DeviceCategory = device.DeviceCategory;
                            existing.DeviceModel = device.DeviceModel;
                            existing.OperatingSystem = device.OperatingSystem;
                            existing.FriendlyName = device.FriendlyName;
                            existing.Icon = device.Icon;
                            existing.IsGateway = device.IsGateway;
                            existing.IsPortable = device.IsPortable;
                            existing.OpenPorts = device.OpenPorts;
                            existing.ConfidenceLevel = device.ConfidenceLevel;
                            existing.IsNew = false;
                        }
                    }

                    // Detectar dispositivos desconectados (mais agressivo - 30s timeout)
                    // Só depois da linha de base: antes disso, tudo pareceria
                    // "desconectado" e o app notificaria a lista inteira ao abrir.
                    var offlineThreshold = DateTime.Now.AddSeconds(-30);
                    var offlineDevices = _knownDevices
                        .Where(d => d.IsOnline &&
                                   !identifiedDevices.Any(s => s.MacAddress == d.MacAddress) &&
                                   d.LastSeen < offlineThreshold)
                        .ToList();

                    foreach (var device in offlineDevices)
                    {
                        device.IsOnline = false;
                        _logger.LogInfo($"[DeviceTracker] Dispositivo offline: {device.FriendlyName} ({device.IPAddress})");
                        if (!isBaselineScan)
                        {
                            DeviceDisconnected?.Invoke(this, new DeviceEventArgs { Device = device });
                        }
                    }

                    _logger.LogSuccess($"[DeviceTracker] Atualização concluída: {GetOnlineDeviceCount()}/{GetTotalDeviceCount()} dispositivos online");
                }

                if (isBaselineScan)
                {
                    _baselineEstablished = true;
                    _logger.LogInfo($"[DeviceTracker] Linha de base de rede estabelecida com {_knownDevices.Count} dispositivo(s). A partir da próxima varredura, somente mudanças reais geram notificação.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("[DeviceTracker] Erro ao atualizar dispositivos", ex);
            }
        }
        
        private static bool IsUseful(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "Other", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "Unknown Device", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "Desconhecido", StringComparison.OrdinalIgnoreCase);
        }

        private static string Prefer(string preferred, string fallback)
        {
            return IsUseful(preferred) ? preferred : fallback;
        }

        public void ClearNewFlags()
        {
            lock (_knownDevicesLock)
            {
                foreach (var device in _knownDevices)
                {
                    device.IsNew = false;
                }
            }
        }
        
        public int GetOnlineDeviceCount()
        {
            lock (_knownDevicesLock)
            {
                return _knownDevices.Count(d => d.IsOnline);
            }
        }
        
        public int GetTotalDeviceCount()
        {
            lock (_knownDevicesLock)
            {
                return _knownDevices.Count;
            }
        }
        
        public void ClearIdentificationCache()
        {
            _identificationEngine.ClearCache();
            _logger.LogInfo("[DeviceTracker] Cache de identificação limpo");
        }
    }
    
    public class DeviceEventArgs : EventArgs
    {
        public NetworkDevice Device { get; set; }
    }
}
