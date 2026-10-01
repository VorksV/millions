using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoltrisOptimizer.Services.Gamer.Overlay.Models
{
    /// <summary>
    /// Configurações do overlay OSD - CORRIGIDO COM PROPRIEDADES FALTANTES
    /// </summary>
    public class OverlaySettings
    {
        /// <summary>
        /// Indica se o overlay está habilitado
        /// </summary>
        public bool IsEnabled { get; set; } = true; // CORREÇÃO: Habilitado por padrão

        /// <summary>
        /// Indica se o overlay deve iniciar automaticamente com o Modo Gamer
        /// </summary>
        public bool StartWithGame { get; set; } = false; // CORREÇÃO: Propriedade corrigida

        /// <summary>
        /// Indica se o overlay deve iniciar automaticamente com o Modo Gamer (nome alternativo)
        /// </summary>
        public bool AutoStartWithGamerMode { get; set; } = false; // CORREÇÃO: Iniciar desligado por padrão conforme pedido do usuário

        /// <summary>
        /// Métricas a serem exibidas
        /// </summary>
        public OverlayMetrics Metrics { get; set; } = new();

        /// <summary>
        /// Posição X do overlay na tela
        /// </summary>
        public int PositionX { get; set; } = 100; // CORREÇÃO: Propriedade adicionada

        /// <summary>
        /// Posição Y do overlay na tela
        /// </summary>
        public int PositionY { get; set; } = 100; // CORREÇÃO: Propriedade adicionada

        /// <summary>
        /// Mostrar FPS
        /// </summary>
        public bool ShowFps { get; set; } = true; // CORREÇÃO: Propriedade adicionada

        /// <summary>
        /// Mostrar CPU
        /// </summary>
        public bool ShowCpu { get; set; } = true; // CORREÇÃO: Propriedade adicionada

        /// <summary>
        /// Mostrar GPU
        /// </summary>
        public bool ShowGpu { get; set; } = true; // CORREÇÃO: Propriedade adicionada

        /// <summary>
        /// Mostrar Memória
        /// </summary>
        public bool ShowMemory { get; set; } = true; // CORREÇÃO: Propriedade adicionada

        /// <summary>
        /// Mostrar Temperatura
        /// </summary>
        public bool ShowTemperature { get; set; } = true; // CORREÇÃO: Propriedade adicionada

        /// <summary>
        /// Posição do overlay na tela
        /// </summary>
        public OverlayPosition Position { get; set; } = OverlayPosition.TopRight;

        /// <summary>
        /// Configurações de fonte
        /// </summary>
        public FontSettings Font { get; set; } = new();

        /// <summary>
        /// Margens do overlay (em pixels)
        /// </summary>
        public MarginSettings Margin { get; set; } = new();

        /// <summary>
        /// Opacidade do overlay (0.0 a 1.0)
        /// </summary>
        public double Opacity { get; set; } = 0.9;

        /// <summary>
        /// Cor do texto (formato ARGB)
        /// </summary>
        public string TextColor { get; set; } = "#FFFFFFFF"; // Branco

        /// <summary>
        /// Cor de fundo (formato ARGB, opcional)
        /// </summary>
        public string? BackgroundColor { get; set; } = null;

        /// <summary>
        /// Mostrar gráficos de FPS e FrameTime
        /// </summary>
        public bool ShowGraphs { get; set; } = false;

        /// <summary>
        /// Tamanho do gráfico (em pixels de altura)
        /// </summary>
        public int GraphHeight { get; set; } = 60;

        /// <summary>
        /// Carrega as configurações de um arquivo JSON
        /// </summary>
        public static OverlaySettings LoadFromFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var settings = JsonSerializer.Deserialize<OverlaySettings>(json, new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles, 
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip});
                    return settings ?? new OverlaySettings();
                }
            }
            catch { }

            return new OverlaySettings();
        }

        /// <summary>
        /// Sincroniza as propriedades planas (ShowFps, ShowCpu...) com as aninhadas (Metrics.*)
        /// </summary>
        public void SyncFlatWithMetrics()
        {
            if (Metrics != null)
            {
                ShowFps = Metrics.ShowFps;
                ShowCpu = Metrics.ShowCpuUsage;
                ShowGpu = Metrics.ShowGpuUsage;
                ShowMemory = Metrics.ShowRamUsage || Metrics.ShowVramUsage;
                ShowTemperature = Metrics.ShowCpuTemperature || Metrics.ShowGpuTemperature;
            }
        }

        /// <summary>
        /// Sincroniza as propriedades aninhadas (Metrics.*) com as planas (ShowFps, ShowCpu...)
        /// </summary>
        public void SyncMetricsWithFlat()
        {
            if (Metrics != null)
            {
                Metrics.ShowFps = ShowFps;
                Metrics.ShowCpuUsage = ShowCpu;
                Metrics.ShowGpuUsage = ShowGpu;
                if (!ShowMemory)
                {
                    Metrics.ShowRamUsage = false;
                    Metrics.ShowVramUsage = false;
                }
                if (!ShowTemperature)
                {
                    Metrics.ShowCpuTemperature = false;
                    Metrics.ShowGpuTemperature = false;
                }
            }
        }

        /// <summary>
        /// Salva as configurações em um arquivo JSON
        /// </summary>
        public void SaveToFile()
        {
            try
            {
                SyncFlatWithMetrics(); // Sincronizar antes de salvar
                
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VoltrisOptimizer"
                );
                var filePath = Path.Combine(appDataPath, "GamerOverlaySettings.json");
                
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions {  
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(filePath, json);
            }
            catch { }
        }
    }

    /// <summary>
    /// Métricas a serem exibidas no overlay
    /// </summary>
    public class OverlayMetrics
    {
        public bool ShowFps { get; set; } = true;
        public bool ShowFrameTime { get; set; } = true;
        public bool ShowCpuUsage { get; set; } = true;
        public bool ShowGpuUsage { get; set; } = true;
        public bool ShowRamUsage { get; set; } = true;
        public bool ShowVramUsage { get; set; } = true;
        public bool ShowCpuTemperature { get; set; } = false;
        public bool ShowGpuTemperature { get; set; } = true;
        public bool ShowCpuClock { get; set; } = true;
        public bool ShowGpuClock { get; set; } = true;
        public bool ShowInputLatency { get; set; } = false;
    }

    /// <summary>
    /// Posição do overlay na tela
    /// </summary>
    public enum OverlayPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        TopCenter,
        BottomCenter
    }

    /// <summary>
    /// Configurações de fonte
    /// </summary>
    public class FontSettings
    {
        public string FontFamily { get; set; } = "Consolas";
        public double FontSize { get; set; } = 14.0;
        public bool IsBold { get; set; } = true;
    }

    /// <summary>
    /// Configurações de margem
    /// </summary>
    public class MarginSettings
    {
        public int Top { get; set; } = 10;
        public int Right { get; set; } = 10;
        public int Bottom { get; set; } = 10;
        public int Left { get; set; } = 10;
    }
}


