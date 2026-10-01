using System;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Controls
{
    /// <summary>
    /// UM estado visual do rosto: nao um desenho, um conjunto de escalares.
    ///
    /// A expressao e COMPOSTA (item 2 do pedido) em vez de trocada: cada humor e
    /// apenas um vetor de numeros, e a geometria em <see cref="AiFaceGeometry"/>
    /// e derivada desses numeros. Transicao entre dois humores = interpolacao
    /// destes campos, o que da o crossfade organico sem "trocar a imagem".
    ///
    /// Nao e estado do Brain: e a representacao visual de um estado do Brain.
    /// A fonte da verdade continua sendo <c>VoltrisBrainV2</c>.
    /// </summary>
    internal struct AiFaceExpression
    {
        // ── rosto: olhos ───────────────────────────────────────────────
        /// <summary>Abertura da palpebra. 0 = fechada, 1 = neutra, 1.3 = arregalada.</summary>
        public double EyeAperture;

        /// <summary>Largura da fenda palpebral (multiplicador).</summary>
        public double EyeWidth;

        /// <summary>Inclinacao do eixo do olho em graus. + = canto externo sobe.</summary>
        public double EyeTilt;

        /// <summary>Curvatura da palpebra superior. 0 = fenda, 1 = arredondada.</summary>
        public double EyeCurve;

        /// <summary>Compressao da palpebra inferior (0..1). Marca rugas/estresse.</summary>
        public double EyeSquint;

        public double IrisScale;
        public double PupilScale;

        /// <summary>Anel da iris. 0 = simples, 1 = anel duplo (intensidade).</summary>
        public double IrisRing;

        public double GlintScale;

        // ── rosto: sobrancelhas ────────────────────────────────────────
        public double BrowInnerLift;
        public double BrowOuterLift;
        /// <summary>
        /// Arco da sobrancelha, -1..1. 0 = reta, +1 = arco alto (surpresa/feliz),
        /// -1 = ponta externa caida (triste/preocupado). NAO e 0..1: raiva
        /// precisa de sobrancelha reta e tensa, nao arqueada.
        /// </summary>
        public double BrowCurve;
        public double BrowThickness;
        public double BrowOpacity;

        /// <summary>Assimetria 0..1: a sobrancelha interna esquerdo sobe e a direita desce.</summary>
        public double BrowAsymmetry;

        // ── rosto: boca ────────────────────────────────────────────────
        public double MouthWidth;
        public double MouthOpen;

        /// <summary>Curvatura do selo labial. -1 = boca para baixo, +1 = sorriso.</summary>
        public double MouthCurve;

        /// <summary>Compressao dos labios 0..1 (boca "tensa").</summary>
        public double MouthPress;

        // ── rosto: detalhe ─────────────────────────────────────────────
        public double NoseOpacity;
        public double CheekOpacity;

        /// <summary>Subida das maos de rosto (leem como "elevacao" do humor).</summary>
        public double CheekLift;

        /// <summary>Opacidade do contorno facial. Da a "presenca" do rosto.</summary>
        public double Contour;

        // ── luz e cor ──────────────────────────────────────────────────
        /// <summary>Intensidade do brilho dos olhos e do acento.</summary>
        public double Glow;

        /// <summary>Intensidade da aureola em anel.</summary>
        public double Halo;

        /// <summary>Volume de lagrima. So CHORANDO usa acima de zero.</summary>
        public double Tear;

        // ── movimento (microanimacoes) ─────────────────────────────────
        public double HeadTilt;
        public double HeadLean;
        public double HeadDrop;
        public double BreathAmp;
        public double BreathPeriod;
        public double Tremor;

        /// <summary>Multiplicador da frequencia de piscada. 0.25 = quase parado.</summary>
        public double BlinkRate;

        /// <summary>Multiplicador da frequencia de saccade. 0 = olhar fixo.</summary>
        public double SaccadeRate;

        public double GazeBiasX;
        public double GazeBiasY;

        /// <summary>Estado neutro de referencia (repouso do rosto, nunca "apagado").</summary>
        public static AiFaceExpression Neutral => new AiFaceExpression
        {
            EyeAperture = 0.84,
            EyeWidth = 1.00,
            EyeTilt = 0.0,
            EyeCurve = 0.55,
            EyeSquint = 0.0,
            IrisScale = 1.00,
            PupilScale = 1.00,
            IrisRing = 0.45,
            GlintScale = 1.00,

            BrowInnerLift = 0.0,
            BrowOuterLift = 0.0,
            BrowCurve = 0.15,
            BrowThickness = 1.00,
            BrowOpacity = 0.82,
            BrowAsymmetry = 0.0,

            MouthWidth = 1.00,
            MouthOpen = 0.0,
            MouthCurve = 0.14,
            MouthPress = 0.15,

            NoseOpacity = 0.20,
            CheekOpacity = 0.10,
            CheekLift = 0.0,
            Contour = 0.55,

            Glow = 0.50,
            Halo = 0.34,
            Tear = 0.0,

            HeadTilt = 0.0,
            HeadLean = 0.0,
            HeadDrop = 0.0,
            BreathAmp = 0.018,
            BreathPeriod = 3.40,
            Tremor = 0.0,

            BlinkRate = 1.0,
            SaccadeRate = 1.0,
            GazeBiasX = 0.0,
            GazeBiasY = 0.0
        };

        /// <summary>Interpolacao linear campo a campo. Tudo e escalar, entao e seguro.</summary>
        public static AiFaceExpression Lerp(in AiFaceExpression a, in AiFaceExpression b, double t)
        {
            if (t <= 0.0) return a;
            if (t >= 1.0) return b;

            return new AiFaceExpression
            {
                EyeAperture = a.EyeAperture + (b.EyeAperture - a.EyeAperture) * t,
                EyeWidth = a.EyeWidth + (b.EyeWidth - a.EyeWidth) * t,
                EyeTilt = a.EyeTilt + (b.EyeTilt - a.EyeTilt) * t,
                EyeCurve = a.EyeCurve + (b.EyeCurve - a.EyeCurve) * t,
                EyeSquint = a.EyeSquint + (b.EyeSquint - a.EyeSquint) * t,
                IrisScale = a.IrisScale + (b.IrisScale - a.IrisScale) * t,
                PupilScale = a.PupilScale + (b.PupilScale - a.PupilScale) * t,
                IrisRing = a.IrisRing + (b.IrisRing - a.IrisRing) * t,
                GlintScale = a.GlintScale + (b.GlintScale - a.GlintScale) * t,

                BrowInnerLift = a.BrowInnerLift + (b.BrowInnerLift - a.BrowInnerLift) * t,
                BrowOuterLift = a.BrowOuterLift + (b.BrowOuterLift - a.BrowOuterLift) * t,
                BrowCurve = a.BrowCurve + (b.BrowCurve - a.BrowCurve) * t,
                BrowThickness = a.BrowThickness + (b.BrowThickness - a.BrowThickness) * t,
                BrowOpacity = a.BrowOpacity + (b.BrowOpacity - a.BrowOpacity) * t,
                BrowAsymmetry = a.BrowAsymmetry + (b.BrowAsymmetry - a.BrowAsymmetry) * t,

                MouthWidth = a.MouthWidth + (b.MouthWidth - a.MouthWidth) * t,
                MouthOpen = a.MouthOpen + (b.MouthOpen - a.MouthOpen) * t,
                MouthCurve = a.MouthCurve + (b.MouthCurve - a.MouthCurve) * t,
                MouthPress = a.MouthPress + (b.MouthPress - a.MouthPress) * t,

                NoseOpacity = a.NoseOpacity + (b.NoseOpacity - a.NoseOpacity) * t,
                CheekOpacity = a.CheekOpacity + (b.CheekOpacity - a.CheekOpacity) * t,
                CheekLift = a.CheekLift + (b.CheekLift - a.CheekLift) * t,
                Contour = a.Contour + (b.Contour - a.Contour) * t,

                Glow = a.Glow + (b.Glow - a.Glow) * t,
                Halo = a.Halo + (b.Halo - a.Halo) * t,
                Tear = a.Tear + (b.Tear - a.Tear) * t,

                HeadTilt = a.HeadTilt + (b.HeadTilt - a.HeadTilt) * t,
                HeadLean = a.HeadLean + (b.HeadLean - a.HeadLean) * t,
                HeadDrop = a.HeadDrop + (b.HeadDrop - a.HeadDrop) * t,

                // Periodo de respiracao e fracao de interpolacao, nao valor interpolado:
                // misturar os dois periodos produz uma respiracao sem frequencia definida.
                BreathAmp = a.BreathAmp + (b.BreathAmp - a.BreathAmp) * t,
                BreathPeriod = t < 0.5 ? a.BreathPeriod : b.BreathPeriod,
                Tremor = a.Tremor + (b.Tremor - a.Tremor) * t,

                BlinkRate = a.BlinkRate + (b.BlinkRate - a.BlinkRate) * t,
                SaccadeRate = a.SaccadeRate + (b.SaccadeRate - a.SaccadeRate) * t,
                GazeBiasX = a.GazeBiasX + (b.GazeBiasX - a.GazeBiasX) * t,
                GazeBiasY = a.GazeBiasY + (b.GazeBiasY - a.GazeBiasY) * t
            };
        }
    }

    /// <summary>
    /// A ponte "Brain -> Visual State -> expressao".
    ///
    /// Cada <see cref="BrainMood"/> (derivado de dados REAIS do Brain) vira um
    /// <see cref="AiFaceExpression"/>. Nao ha logica, temporizador, polling nem
    /// deteccao aqui: e uma tabela. A unica inteligencia do VOLTRIS continua
    /// sendo o <c>VoltrisBrainV2</c>.
    /// </summary>
    internal static class AiFaceExpressionLibrary
    {
        private static AiFaceExpression Build(
            double aperture = 0.84, double width = 1.00, double tilt = 0.0,
            double curve = 0.55, double squint = 0.0,
            double iris = 1.00, double pupil = 1.00, double ring = 0.45, double glint = 1.00,
            double browIn = 0.0, double browOut = 0.0, double browCurve = 0.15,
            double browThick = 1.00, double browOp = 0.82, double browAsym = 0.0,
            double mouthW = 1.00, double mouthO = 0.0, double mouthC = 0.14, double mouthPress = 0.15,
            double nose = 0.20, double cheek = 0.10, double cheekLift = 0.0, double contour = 0.55,
            double glow = 0.50, double halo = 0.34, double tear = 0.0,
            double headTilt = 0.0, double headLean = 0.0, double headDrop = 0.0,
            double breathAmp = 0.018, double breathPeriod = 3.40, double tremor = 0.0,
            double blink = 1.0, double saccade = 1.0, double gazeX = 0.0, double gazeY = 0.0)
        {
            return new AiFaceExpression
            {
                EyeAperture = aperture, EyeWidth = width, EyeTilt = tilt, EyeCurve = curve, EyeSquint = squint,
                IrisScale = iris, PupilScale = pupil, IrisRing = ring, GlintScale = glint,
                BrowInnerLift = browIn, BrowOuterLift = browOut, BrowCurve = browCurve,
                BrowThickness = browThick, BrowOpacity = browOp, BrowAsymmetry = browAsym,
                MouthWidth = mouthW, MouthOpen = mouthO, MouthCurve = mouthC, MouthPress = mouthPress,
                NoseOpacity = nose, CheekOpacity = cheek, CheekLift = cheekLift, Contour = contour,
                Glow = glow, Halo = halo, Tear = tear,
                HeadTilt = headTilt, HeadLean = headLean, HeadDrop = headDrop,
                BreathAmp = breathAmp, BreathPeriod = breathPeriod, Tremor = tremor,
                BlinkRate = blink, SaccadeRate = saccade, GazeBiasX = gazeX, GazeBiasY = gazeY
            };
        }

        /// <summary>
        /// Traducao estado do Brain -> expressao. Todas as 18 entradas sao
        /// microexpressoes, nao caricaturas: um unico par de sobrancelhas e uma
        /// unica boca mudam de geometria, e o resto do rosto continua o mesmo
        /// rosto.
        /// </summary>
        public static AiFaceExpression For(BrainMood mood)
        {
            switch (mood)
            {
                // ── REPOUSO ────────────────────────────────────────────
                case BrainMood.Sleeping:
                    return Build(
                        aperture: 0.05, width: 0.95, curve: 0.50,
                        browIn: -0.6, browOut: -0.4, browCurve: -0.05, browOp: 0.62,
                        mouthW: 0.70, mouthC: 0.06, mouthPress: 0.30,
                        nose: 0.14, contour: 0.34,
                        glow: 0.16, halo: 0.16,
                        breathAmp: 0.026, breathPeriod: 5.20,
                        blink: 0.25, saccade: 0.0);

                // ── NEUTRO ─────────────────────────────────────────────
                case BrainMood.Idle:
                    return Build();

                // ── ATENTO: contexto novo, oriented, ainda calmo ───────
                case BrainMood.Attentive:
                    return Build(
                        aperture: 0.95, width: 1.00, tilt: -0.6, curve: 0.50,
                        browIn: 0.9, browOut: 1.3, browCurve: 0.00, browOp: 0.95,
                        mouthW: 0.92, mouthC: 0.05, mouthPress: 0.20,
                        glow: 0.62, halo: 0.42, contour: 0.58,
                        breathAmp: 0.020, breathPeriod: 2.80,
                        blink: 1.25, gazeY: -0.6);

                // ── PENSANDO: olhar para cima e para o lado, cara de quem
                //    resolve: uma sobrancelha sobe, a outra nao. ──────
                case BrainMood.Thinking:
                    return Build(
                        aperture: 0.66, width: 0.96, tilt: -1.6, curve: 0.62,
                        iris: 1.05,
                        browIn: 1.6, browOut: 0.2, browCurve: -0.10, browOp: 0.90, browAsym: 0.85,
                        mouthW: 0.82, mouthC: -0.05, mouthPress: 0.55,
                        glow: 0.60, halo: 0.45, contour: 0.50,
                        breathAmp: 0.016, breathPeriod: 3.00,
                        headTilt: -1.2, blink: 0.80, saccade: 0.7,
                        gazeX: -1.6, gazeY: -1.9);

                // ── PROCESSANDO: firme, focado, boca levemente aberta ──
                case BrainMood.Working:
                    return Build(
                        aperture: 0.74, width: 0.99, tilt: -1.2, curve: 0.45,
                        pupil: 0.90,
                        browIn: -0.4, browOut: 0.3, browCurve: 0.05, browOp: 0.88,
                        mouthW: 0.86, mouthO: 0.34, mouthC: 0.02, mouthPress: 0.10,
                        glow: 0.78, halo: 0.50, contour: 0.60,
                        breathAmp: 0.014, breathPeriod: 1.90,
                        headDrop: 0.3, blink: 0.95);

                // ── CONCENTRACAO: quase imobil. Imobilidade = foco. ───
                case BrainMood.Focused:
                    return Build(
                        aperture: 0.52, width: 0.98, tilt: -2.0, curve: 0.30, squint: 0.50,
                        iris: 1.08, pupil: 0.82,
                        browIn: -0.9, browOut: 0.6, browCurve: -0.05, browThick: 1.15, browOp: 0.95,
                        mouthW: 0.80, mouthC: -0.12, mouthPress: 0.80,
                        glow: 0.85, halo: 0.45, contour: 0.72,
                        breathAmp: 0.010, breathPeriod: 2.60,
                        blink: 0.55, saccade: 0.35, gazeY: 0.3);

                // ── SATISFEITO: olhos macios, sorriso de boca fechada ──
                case BrainMood.Satisfied:
                    return Build(
                        aperture: 0.60, width: 0.98, tilt: 0.8, curve: 0.72,
                        iris: 0.95,
                        browIn: 0.4, browOut: 0.9, browCurve: 0.20, browOp: 0.72,
                        mouthW: 1.05, mouthC: 0.62, mouthPress: 0.05,
                        cheek: 0.20, cheekLift: 0.5,
                        glow: 0.62, halo: 0.48, contour: 0.50,
                        breathAmp: 0.022, breathPeriod: 3.20, blink: 0.90);

                // ── FELIZ: meia-lua nos olhos, sorriso de verdade ───────
                case BrainMood.Happy:
                    return Build(
                        aperture: 0.44, width: 1.02, tilt: 1.4, curve: 0.88,
                        iris: 0.90,
                        browIn: 1.0, browOut: 1.7, browCurve: 0.50, browOp: 0.88,
                        mouthW: 1.12, mouthO: 0.20, mouthC: 0.95, mouthPress: 0.0,
                        cheek: 0.30, cheekLift: 1.0,
                        glow: 0.75, halo: 0.55, contour: 0.58,
                        breathAmp: 0.026, breathPeriod: 1.70,
                        headDrop: -0.9, blink: 1.20);

                // ── MOTIVADO: olhar aceso, inclinado a frente ─────────
                case BrainMood.Motivated:
                    return Build(
                        aperture: 0.90, width: 1.02, tilt: 0.4, curve: 0.55,
                        pupil: 1.05,
                        browIn: 1.2, browOut: 1.5, browCurve: 0.05, browOp: 0.95,
                        mouthW: 1.02, mouthO: 0.06, mouthC: 0.55, mouthPress: 0.10,
                        glow: 0.80, halo: 0.52, contour: 0.66,
                        breathAmp: 0.024, breathPeriod: 2.00,
                        headLean: 0.6, blink: 1.10);

                // ── PREOCUPADO: V invertido nas sobrancelhas, boca curta ─
                case BrainMood.Concerned:
                    return Build(
                        aperture: 0.98, width: 0.97, tilt: 0.6, curve: 0.60,
                        browIn: 2.1, browOut: -0.5, browCurve: -0.30, browOp: 0.92, browAsym: 0.25,
                        mouthW: 0.78, mouthC: -0.28, mouthPress: 0.40,
                        glow: 0.66, halo: 0.46, contour: 0.55,
                        breathAmp: 0.020, breathPeriod: 1.55,
                        headTilt: 1.4, tremor: 0.30, blink: 1.30);

                // ── TRISTE: o V invertido ao contrario de raiva ─────────
                case BrainMood.Sad:
                    return Build(
                        aperture: 0.55, width: 0.96, tilt: -1.0, curve: 0.62,
                        iris: 0.92,
                        browIn: 2.4, browOut: -1.1, browCurve: -0.35, browOp: 0.85,
                        mouthW: 0.84, mouthC: -0.72, mouthPress: 0.30,
                        nose: 0.30, cheek: 0.12,
                        glow: 0.30, halo: 0.24, contour: 0.40,
                        breathAmp: 0.014, breathPeriod: 4.60,
                        headDrop: 1.5, headTilt: 0.8,
                        blink: 0.70, gazeY: 1.6);

                // ── FRUSTRADO: sobrancelha comprimindo, tensao na boca ──
                case BrainMood.Frustrated:
                    return Build(
                        aperture: 0.60, width: 0.95, tilt: -1.8, curve: 0.35, squint: 0.65,
                        pupil: 0.85,
                        browIn: -2.0, browOut: 0.9, browCurve: -0.25, browThick: 1.20, browOp: 1.0,
                        mouthW: 0.86, mouthO: 0.22, mouthC: -0.35, mouthPress: 0.70,
                        glow: 0.55, halo: 0.40, contour: 0.60,
                        breathAmp: 0.022, breathPeriod: 1.35,
                        headLean: 0.9, tremor: 0.35, blink: 0.85);

                // ── RAIVA/ALERTA: a unica expressao de forca total ─────
                case BrainMood.Furious:
                    return Build(
                        aperture: 0.44, width: 1.02, tilt: -3.2, curve: 0.22, squint: 0.85,
                        iris: 1.12, pupil: 0.70, ring: 1.0,
                        browIn: -3.0, browOut: 1.6, browCurve: -0.15, browThick: 1.35, browOp: 1.0,
                        mouthW: 1.00, mouthO: 0.62, mouthC: -0.55, mouthPress: 0.0,
                        nose: 0.34, cheek: 0.22, cheekLift: -0.5,
                        glow: 1.0, halo: 0.72, contour: 0.85,
                        breathAmp: 0.030, breathPeriod: 1.20,
                        headDrop: -0.4, tremor: 0.90,
                        blink: 0.30, saccade: 1.6);

                // ── SURPRESA: olhos arregalados, sobrancelha no teto ───
                case BrainMood.Surprised:
                    return Build(
                        aperture: 1.28, width: 1.06, curve: 0.85,
                        iris: 0.88, pupil: 1.15,
                        browIn: 2.8, browOut: 3.4, browCurve: 0.65, browOp: 0.95,
                        mouthW: 0.66, mouthO: 0.55, mouthC: 0.05, mouthPress: 0.0,
                        nose: 0.28,
                        glow: 0.90, halo: 0.60, contour: 0.70,
                        breathAmp: 0.030, breathPeriod: 1.15,
                        headDrop: -0.8, blink: 1.50, saccade: 2.0);

                // ── CHORANDO: triste + lagrima, tudo muito discreto ────
                case BrainMood.Weeping:
                    return Build(
                        aperture: 0.44, width: 0.95, tilt: -0.6, curve: 0.70,
                        browIn: 3.0, browOut: -1.2, browCurve: -0.40, browOp: 0.88,
                        mouthW: 0.80, mouthC: -0.85, mouthO: 0.16, mouthPress: 0.25,
                        nose: 0.30, cheek: 0.20,
                        tear: 1.0,
                        glow: 0.28, halo: 0.26, contour: 0.36,
                        breathAmp: 0.022, breathPeriod: 2.40,
                        headDrop: 2.0, tremor: 0.40,
                        blink: 0.60, gazeY: 2.2);

                // ── SUCESSO: resolvido, sorriso nitido, olhar nivelado ─
                case BrainMood.Succeeded:
                    return Build(
                        aperture: 0.82, width: 1.00, tilt: 0.4, curve: 0.55,
                        browIn: 0.8, browOut: 1.1, browCurve: 0.15, browOp: 0.90,
                        mouthW: 1.08, mouthO: 0.05, mouthC: 0.72, mouthPress: 0.05,
                        cheek: 0.18, cheekLift: 0.6,
                        glow: 0.92, halo: 0.55, contour: 0.72,
                        breathAmp: 0.020, breathPeriod: 2.30,
                        headDrop: -0.5, blink: 0.85);

                // ── ERRO: pincao na testa e boca dura, sem caricatura ───
                case BrainMood.Errored:
                    return Build(
                        aperture: 1.00, width: 0.98, tilt: -1.0, curve: 0.42,
                        pupil: 0.80, ring: 1.0,
                        browIn: -1.6, browOut: 1.0, browCurve: -0.20, browThick: 1.15, browOp: 1.0,
                        mouthW: 0.90, mouthO: 0.34, mouthC: -0.50, mouthPress: 0.35,
                        nose: 0.30,
                        glow: 0.85, halo: 0.60, contour: 0.68,
                        breathAmp: 0.026, breathPeriod: 1.30,
                        tremor: 0.55, blink: 0.75);

                // ── CANSADO: meio fechado, arrastado, sem cor ──────────
                default:
                    return Build(
                        aperture: 0.40, width: 0.97, tilt: 0.8, curve: 0.50,
                        browIn: -0.4, browOut: -0.2, browCurve: -0.15, browOp: 0.60,
                        mouthW: 0.90, mouthC: 0.0, mouthPress: 0.45,
                        nose: 0.16, contour: 0.35,
                        glow: 0.28, halo: 0.20,
                        breathAmp: 0.022, breathPeriod: 6.00,
                        headDrop: 0.9, headTilt: -1.2,
                        blink: 0.60, saccade: 0.5);
            }
        }
    }
}
