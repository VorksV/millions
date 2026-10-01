using System;
using System.Windows.Media;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Resolve o ícone e a cor de temperatura para notificações.
    ///
    /// POR QUE EXISTE
    /// A notificação de "Alívio Térmico" usava o ícone genérico de aviso
    /// (triângulo laranja) e não comunicava calor. O usuário passou a pedir o
    /// MESMO ícone por cor dos cartões de CPU e GPU do Dashboard — é o que
    /// torna a notificação reconhecível de imediato, sem ler o texto.
    ///
    /// Os limiares abaixo são COPIADOS de <see cref="ThermalIndicatorConverter"/>
    /// para que cartão e notificação nunca discordem sobre a severidade. Se um
    /// mudar, o outro precisa mudar junto — por isso ficam centralizados aqui
    /// e o conversor delega para cá.
    ///
    /// ÍCONE: termômetro — o mesmo traçado usado em DashboardView.xaml
    /// (GamerDashCpuTemp / GamerDashGpuTemp), inclusive na versão preenchida.
    /// </summary>
    public static class ThermalGlyphResolver
    {
        /// <summary>Traçado do termômetro (24x24), idêntico ao dos cartões do Dashboard.</summary>
        public const string ThermometerGeometry =
            "M14,14.76 V5.5 A4.5,4.5 0 0,0 10,5.5 V14.76 A6.5,6.5 0 1,0 14,14.76 Z " +
            "M12,2 A1.5,1.5 0 0,0 10.5,3.5 V15.32 A4.5,4.5 0 1,0 13.5,15.32 V3.5 A1.5,1.5 0 0,0 12,2 Z";

        // ── Paleta idêntica à do ThermalIndicatorConverter ──
        public static readonly Color Unknown = Color.FromRgb(107, 114, 128);   // #6B7280
        public static readonly Color Critical = Color.FromRgb(255, 59, 48);   // #FF3B30  >= 95
        public static readonly Color High = Color.FromRgb(239, 68, 68);       // #EF4444  >= 90
        public static readonly Color Warm = Color.FromRgb(251, 146, 60);      // #FB923C  >= 80
        public static readonly Color Mild = Color.FromRgb(245, 158, 11);      // #F59E0B  >= 70
        public static readonly Color Good = Color.FromRgb(16, 185, 129);      // #10B981  <  70

        /// <summary>
        /// Mapa temperatura -> cor. É a ÚNICA fonte de verdade de severidade
        /// térmica; o <see cref="ThermalIndicatorConverter"/> usa este mesmo
        /// método, garantindo que cartão, widget e notificação mostrem a mesma
        /// severidade para a mesma temperatura.
        /// </summary>
        public static Color ColorForTemperature(double celsius)
        {
            if (double.IsNaN(celsius) || celsius <= 0) return Unknown;
            if (celsius >= 95) return Critical;
            if (celsius >= 90) return High;
            if (celsius >= 80) return Warm;
            if (celsius >= 70) return Mild;
            return Good;
        }

        /// <summary>
        /// Devolve o glifo do termômetro e a cor de destaque para a temperatura
        /// informada. Mesmo contrato de <c>DeviceGlyphResolver.Resolve</c>, usado
        /// pelas notificações do Shield.
        /// </summary>
        public static (Geometry glyph, Color accent) Resolve(double celsius)
        {
            Geometry? g = null;
            try
            {
                g = Geometry.Parse(ThermometerGeometry);
                g.Freeze();
            }
            catch
            {
                // Falha de parse não pode impedir a notificação: o chamador
                // cai no ícone genérico de aviso.
            }

            return (g!, ColorForTemperature(celsius));
        }
    }
}
