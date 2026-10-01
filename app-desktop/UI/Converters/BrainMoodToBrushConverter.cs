using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Paleta do rosto da IA.
    ///
    /// REGRA DE DESIGN (item 5 do pedido): o circulo NAO pode virar arco-iris.
    /// Entao a paleta tem TRES familias e nao dezoito:
    ///
    ///   AZUL/CIANO  - a familia normal. Idle, Attentive, Thinking, Working,
    ///                 Focused, Surprised, Succeeded. 9 de 18 estados.
    ///   VERDE/TEAL  - so resultado POSITIVO real. Satisfied, Happy, Motivated.
    ///   AMBAR->VERM - so problema real. Concerned, Frustrated, Furious,
    ///                 Errored. E uma rampa de severidade, nao 4 cores.
    ///   FRIOS       - estado emocional sem "problema": Sad, Weeping, Bored.
    ///   APAGADO     - Brain desligado. Sleeping.
    ///
    /// Dentro de cada familia, a rampa vai de claro/apagado para saturado. O
    /// efeito de "a cor mudou" e sempre reforcado pela EXPRESSAO, nunca
    /// substituido por ela - e por isso que 18 estados nao viram 18 cores
    /// distintas na tela.
    ///
    /// IMPORTANTE - os modos sao divididos por TIPO DE DESTINO:
    /// modos "Color*" devolvem <see cref="Color"/> (para GradientStop.Color) e os
    /// demais devolvem pincel ja CONGELADO (para Fill/Stroke). Atribuir uma
    /// Color a um Fill quebra em runtime.
    ///
    /// Tudo aqui e cacheado por humor. O rosto pode pedir pincel 30x por
    /// segundo durante uma transicao e o custo continua sendo uma leitura de
    /// dicionario - nenhuma alocacao no caminho quente.
    /// </summary>
    public class BrainMoodToBrushConverter : IValueConverter
    {
        private static readonly Dictionary<BrainMood, Color> Palette = new()
        {
            // ── apagado ──────────────────────────────────────────────
            [BrainMood.Sleeping] = Color.FromRgb(0x55, 0x60, 0x7E),   // ardósia frio

            // ── família azul/ciano: família normal ──────────────────
            [BrainMood.Idle] = Color.FromRgb(0x31, 0xA8, 0xFF),       // azul da marca
            [BrainMood.Attentive] = Color.FromRgb(0x4F, 0xB8, 0xFF), // um passo acima do idle
            [BrainMood.Thinking] = Color.FromRgb(0x7C, 0x8C, 0xF8),  // índigo: está "calculando"
            [BrainMood.Working] = Color.FromRgb(0x22, 0xD3, 0xEE),    // ciano técnico
            [BrainMood.Focused] = Color.FromRgb(0x5C, 0xA8, 0xFF),   // azul concentrado
            [BrainMood.Surprised] = Color.FromRgb(0x7D, 0xD3, 0xFC), // gelo: um pico breve

            // ── família verde: só resultado positivo real ───────────
            [BrainMood.Satisfied] = Color.FromRgb(0x2F, 0xC9, 0xA0),
            [BrainMood.Happy] = Color.FromRgb(0x34, 0xD3, 0x99),
            [BrainMood.Motivated] = Color.FromRgb(0x46, 0xE0, 0xB0),
            [BrainMood.Succeeded] = Color.FromRgb(0x10, 0xB9, 0x81),

            // ── rampa de severidade: âmbar -> vermelho ──────────────
            [BrainMood.Concerned] = Color.FromRgb(0xE0, 0xA8, 0x14),
            [BrainMood.Frustrated] = Color.FromRgb(0xD4, 0x7A, 0x46), // laranja queimado, não berrante
            [BrainMood.Furious] = Color.FromRgb(0xE5, 0x45, 0x45),
            [BrainMood.Errored] = Color.FromRgb(0xD8, 0x3B, 0x4A),

            // ── frios: emoción, não problema ────────────────────────
            [BrainMood.Sad] = Color.FromRgb(0x4C, 0x8F, 0xE0),
            [BrainMood.Weeping] = Color.FromRgb(0x5B, 0x8F, 0xD6),
            [BrainMood.Bored] = Color.FromRgb(0x6B, 0x7A, 0x8F)
        };

        private static readonly Dictionary<BrainMood, SolidColorBrush> SolidCache = new();
        private static readonly Dictionary<BrainMood, SolidColorBrush> SoftCache = new();
        private static readonly Dictionary<BrainMood, SolidColorBrush> FaintCache = new();
        private static readonly Dictionary<BrainMood, SolidColorBrush> AuraCache = new();
        private static readonly Dictionary<BrainMood, SolidColorBrush> GlowCache = new();
        private static readonly Dictionary<string, RadialGradientBrush> HaloCache = new();
        private static readonly Dictionary<BrainMood, RadialGradientBrush> IrisCache = new();
        private static readonly object CacheGate = new object();

        private static Color ColorFor(BrainMood mood) =>
            Palette.TryGetValue(mood, out var c) ? c : Palette[BrainMood.Idle];

        private static SolidColorBrush BrushFor(Dictionary<BrainMood, SolidColorBrush> cache, BrainMood mood, byte alpha)
        {
            lock (CacheGate)
            {
                if (cache.TryGetValue(mood, out var cached)) return cached;
                var src = ColorFor(mood);
                var brush = new SolidColorBrush(Color.FromArgb(alpha, src.R, src.G, src.B));
                brush.Freeze();
                cache[mood] = brush;
                return brush;
            }
        }

        /// <summary>
        /// Halo radial em ANEL: transparente no centro, pico na borda,
        /// transparente fora. O centro transparente e essencial - o campo da
        /// face e opaco, entao um gradiente com pico no meio nao apareceria nada.
        /// Sem BlurEffect: custo zero de GPU, que e o que importa num app que
        /// existe para medir custo de sistema.
        /// </summary>
        private static RadialGradientBrush HaloFor(BrainMood mood, double peak)
        {
            var key = mood.ToString() + "|" + peak.ToString(CultureInfo.InvariantCulture);
            lock (CacheGate)
            {
                if (HaloCache.TryGetValue(key, out var cached)) return cached;

                var c = ColorFor(mood);
                var clear = Color.FromArgb(0, c.R, c.G, c.B);
                var brush = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5
                };
                brush.GradientStops.Add(new GradientStop(clear, 0.00));
                brush.GradientStops.Add(new GradientStop(clear, peak - 0.14));
                brush.GradientStops.Add(new GradientStop(c, peak));
                brush.GradientStops.Add(new GradientStop(clear, 1.00));
                brush.Freeze();

                HaloCache[key] = brush;
                return brush;
            }
        }

        /// <summary>
        /// Gradiente da iris: miolo puxado para o branco e borda no acento do
        /// humor. Um unico plano de cor no olho faz a figura ler como "desenhada
        /// com caneta"; o degrade radial e o que da profundidade sem custar nada.
        /// </summary>
        private static RadialGradientBrush IrisFor(BrainMood mood)
        {
            lock (CacheGate)
            {
                if (IrisCache.TryGetValue(mood, out var cached)) return cached;

                var c = ColorFor(mood);
                var brush = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.42, 0.34),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.72,
                    RadiusY = 0.72
                };
                brush.GradientStops.Add(new GradientStop(Mix(c, Colors.White, 0.62), 0.00));
                brush.GradientStops.Add(new GradientStop(Mix(c, Colors.White, 0.16), 0.55));
                brush.GradientStops.Add(new GradientStop(c, 1.00));
                brush.Freeze();

                IrisCache[mood] = brush;
                return brush;
            }
        }

        // ─────────── API estática para quem aplica a expressão em C# ───────────
        // AiFaceView não usa Binding de mood (dependência cíclica em runtime),
        // então precisa pegar os pincéis direto daqui.

        /// <summary>Cor de acento crua do humor. Base de todas as derivadas.</summary>
        public static Color Accent(BrainMood mood) => ColorFor(mood);

        public static SolidColorBrush Solid(BrainMood mood) => BrushFor(SolidCache, mood, 255);
        public static SolidColorBrush Soft(BrainMood mood) => BrushFor(SoftCache, mood, 120);
        public static SolidColorBrush Faint(BrainMood mood) => BrushFor(FaintCache, mood, 48);

        /// <summary>Halo largo, pico em 72% do raio.</summary>
        public static RadialGradientBrush Halo(BrainMood mood) => HaloFor(mood, 0.72);

        /// <summary>Halo rente ao contorno da face, pico em 88% do raio.</summary>
        public static RadialGradientBrush HaloEdge(BrainMood mood) => HaloFor(mood, 0.88);

        /// <summary>Gradiente radial da íris (miolo claro -&gt; borda no acento).</summary>
        public static RadialGradientBrush Iris(BrainMood mood) => IrisFor(mood);

        /// <summary>Mistura linear de duas cores. Usado na transição de humor.</summary>
        public static Color Mix(Color a, Color b, double t)
        {
            if (t <= 0.0) return a;
            if (t >= 1.0) return b;
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        /// <summary>
        /// Transição de cor entre dois humores. Usa o MESMO <paramref name="t"/>
        /// da interpolação da geometria, então cor e forma mudam juntas - cor
        /// LEADING ou LAGGING a forma denuncia que são dois sistemas.
        /// </summary>
        public static Color Blend(BrainMood from, BrainMood to, double t) =>
            Mix(ColorFor(from), ColorFor(to), t);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var mood = value is BrainMood m ? m : BrainMood.Idle;
            string mode = parameter as string ?? "Solid";

            switch (mode)
            {
                // --- modos que devolvem Color ( GradientStop.Color ) ---
                case "Color":
                    return ColorFor(mood);
                case "ColorSoft":
                {
                    var c = ColorFor(mood);
                    return Color.FromArgb(78, c.R, c.G, c.B);
                }
                case "ColorFaint":
                {
                    var c = ColorFor(mood);
                    return Color.FromArgb(38, c.R, c.G, c.B);
                }

                // --- modos que devolvem SolidColorBrush ( Fill / Stroke ) ---
                case "Soft":
                    return BrushFor(SoftCache, mood, 120);
                case "Faint":
                    return BrushFor(FaintCache, mood, 48);
                case "Aura":
                    return BrushFor(AuraCache, mood, 42);
                case "Glow":
                    return BrushFor(GlowCache, mood, 90);
                case "Halo":
                    return HaloFor(mood, 0.72);
                case "HaloEdge":
                    return HaloFor(mood, 0.88);
                case "Iris":
                    return IrisFor(mood);
                default:
                    return BrushFor(SolidCache, mood, 255);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
