using System;

using System.IO;

using System.Text.Json;

using System.Threading;

namespace VoltrisOptimizer.UI.Widgets
{
    /// <summary>
    /// Configuração persistente do widget flutuante
    /// </summary>
    public class WidgetConfig
    {
        private static readonly string ConfigDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Voltris");
        private static readonly string ConfigPath = Path.Combine(ConfigDirectory, "widget-config.json");
        private static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        //========================= POSIÇÃO =========================
        public double PositionX { get; set; } = 100;

        public double PositionY { get; set; } = 100;

        public bool HasSavedPosition { get; set; } = false;

        //========================= VISIBILIDADE =========================
        public bool IsVisible { get; set; } = true;

        public bool AutoStart { get; set; } = true;

        public DateTime LastModified { get; set; } = DateTime.UtcNow;

        //========================= AUTO-HIDE (COLLAPSE) =========================
        public bool IsCollapsed { get; set; } = false;

        //========================= CONFIGURAÇÕES AVANÇADAS =========================
        public bool OverlayEnabled { get; set; } = true;

        public bool SmartMode { get; set; } = true;

        public bool GamerOnlyMode { get; set; } = false;

        public bool StartWithWindows { get; set; } = false;

        //========================= VISIBILIDADE DE MÉTRICAS DO OVERLAY =========================
        public bool ShowFps { get; set; } = true;

        public bool ShowFrameTime { get; set; } = true;

        public bool ShowCpuUsage { get; set; } = true;

        public bool ShowGpuUsage { get; set; } = true;

        public bool ShowRamUsage { get; set; } = true;

        public bool ShowVramUsage { get; set; } = true;

        public bool ShowCpuTemp { get; set; } = true;

        public bool ShowGpuTemp { get; set; } = true;

        public bool ShowCpuClock { get; set; } = true;

        public bool ShowGpuClock { get; set; } = true;

        public bool ShowInputLatency { get; set; } = true;

        //========================= VISIBILIDADE DE MÉTRICAS DO WIDGET FLUTUANTE =========================
        // Controladas pelo menu de contexto (botão direito no widget)
        public bool WidgetShowCpu { get; set; } = true;

        public bool WidgetShowRam { get; set; } = true;

        public bool WidgetShowDisk { get; set; } = true;

        public bool WidgetShowTemp { get; set; } = true;

        public bool WidgetShowFps { get; set; } = true;

        //========================= LOAD (ROBUSTO) =========================
        public static WidgetConfig Load()
        {
            _lock.EnterReadLock();

            try
            {
                if (!File.Exists(ConfigPath))
                    return CreateDefault("Arquivo não existe");

                try
                {
                    var json = File.ReadAllText(ConfigPath);

                    if (string.IsNullOrWhiteSpace(json))
                        return CreateDefault("JSON vazio");

                    var config = JsonSerializer.Deserialize<WidgetConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                    if (config == null)
                        return CreateDefault("Deserialize retornou null");

                    //SANITIZAÇÃO CRÍTICA
                    config.Sanitize();

                    return config;
                }
                catch
                {
                    return CreateDefault("Erro ao ler/deserializar JSON");
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        //========================= SAVE =========================
        public void Save()
        {
            _lock.EnterWriteLock();
            try
            {
                Directory.CreateDirectory(ConfigDirectory);

                LastModified = DateTime.UtcNow;

                var options = new JsonSerializerOptions { WriteIndented = true,
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(this, options);

                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                // Log sem crash em produção
                System.Diagnostics.Debug.WriteLine($"[WidgetConfig] Erro ao salvar: {ex.Message}");
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        //========================= SANITIZAÇÃO (CRÍTICO) =========================
        private void Sanitize()
        {
            //POSIÇÃO INVÁLIDA, RESET
            if (double.IsNaN(PositionX) || double.IsInfinity(PositionX) || PositionX < -500)
                PositionX = 100;

            if (double.IsNaN(PositionY) || double.IsInfinity(PositionY) || PositionY < -500)
                PositionY = 100;

            //Evitar estado inválido
            if (!OverlayEnabled)
                SmartMode = false;
        }

        //========================= DEFAULT SEGURO =========================
        private static WidgetConfig CreateDefault(string reason)
        {
            var config = new WidgetConfig
            {
                IsVisible = true,
                HasSavedPosition = false,
                OverlayEnabled = true,
                SmartMode = true
            };

            return config;
        }
    }
}
