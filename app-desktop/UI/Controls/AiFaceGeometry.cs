using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VoltrisOptimizer.UI.Controls
{
    /// <summary>
    /// Geometria do rosto da IA - 100% PARAMETRICA.
    ///
    /// Nao existe "desenho do humor X". Existe UM rosto, definido por uma
    /// malha de medidas, e uma funcao que transforma um
    /// <see cref="AiFaceExpression"/> em curvas. Mudar de expressao nao troca
    /// imagem: muda numeros, e os numeros sao convertidos em Beziers.
    ///
    /// MALHA (a regua de tudo). O rosto inteiro cabe em 0..100 nos dois eixos,
    /// entao o Viewbox do UserControl escala sem recorte em 100% / 125% / 150%:
    ///
    ///     contorno facial   x 18.5..81.5   y 13..90
    ///     olhos             (36.5, 46) e (63.5, 46)   fenda 13.2 x 10.8
    ///     sobrancelhas      y 34.2   meia-largura 7.4
    ///     nariz             y 53..61
    ///     boca              (50, 68.5)   meia-largura 9.6
    ///     lagrimas          sob a palpebra inferior de cada olho
    ///
    /// Por que 36.5 e 63.5 e nao 33 e 67 como antes: a distancia interocular
    /// de um rosto humano e ~1/3 da largura, e os olhos ocupam ~15% cada. Com
    /// 30 de separacao e fenda 12.4 o rosto le como "duas bolas num circulo".
    ///
    /// CUSTO: cada funcao devolve uma Geometry CONGELADA. As partes que mudam de
    /// forma a cada transicao (sobrancelha, fenda, palpebra, boca, nariz) sao
    /// reconstruidas so quando um parametro muda de verdade - o resto do rosto
    /// (iris, pupila, brilho, bochecha) e Ellipse com Transform, que a GPU
    /// resolve sozinha. Nada de Bitmap, Video, WebView ou shader.
    /// </summary>
    internal static class AiFaceGeometry
    {
        // ───────────────────────── METRICAS ─────────────────────────

        public const double FaceCx = 50.0;
        public const double FaceCy = 51.5;
        public const double FaceHalfW = 31.5;
        public const double FaceTop = 13.0;
        public const double FaceChin = 90.0;

        public const double EyeOffsetX = 13.5;   // distancia do centro ate o olho
        public const double EyeCy = 46.0;
        public const double EyeHalfW = 6.6;
        public const double EyeHalfH = 5.4;

        public const double BrowY = 34.2;
        public const double BrowHalfW = 7.4;

        public const double MouthCy = 68.5;
        public const double MouthHalfW = 9.6;

        // ───────────────────────── CONTORNO ─────────────────────────

        /// <summary>
        /// Silhueta: testa larga, maos de rosto cheias, mandibula afinando para
        /// um queixo arredondado. E o que separa "rosto" de "circulo com
        /// olhos" - a mudanca de raio da curva do queixo ao longo do caminho.
        /// </summary>
        public static readonly Geometry FacePlate = BuildFacePlate();

        private static Geometry BuildFacePlate()
        {
            double cx = FaceCx;
            double t = FaceTop;
            double b = FaceChin;
            double w = FaceHalfW;
            double cheekY = FaceCy + 4.0;
            double jawY = FaceCy + 24.0;

            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(cx, t), isFilled: true, isClosed: true);
                // testa -> Have de cima
                c.BezierTo(new Point(cx - w * 0.62, t), new Point(cx - w, t + 11.0), new Point(cx - w, t + 22.5), true, true);
                // Have ate a mao de rosto
                c.BezierTo(new Point(cx - w, cheekY), new Point(cx - w * 0.95, cheekY + 6.0), new Point(cx - w * 0.70, jawY), true, true);
                // mandibula -> queixo
                c.BezierTo(new Point(cx - w * 0.46, b - 4.6), new Point(cx - w * 0.20, b), new Point(cx, b), true, true);
                c.BezierTo(new Point(cx + w * 0.20, b), new Point(cx + w * 0.46, b - 4.6), new Point(cx + w * 0.70, jawY), true, true);
                // queixo -> Have
                c.BezierTo(new Point(cx + w * 0.95, cheekY + 6.0), new Point(cx + w, cheekY), new Point(cx + w, t + 22.5), true, true);
                // testa -> topo
                c.BezierTo(new Point(cx + w, t + 11.0), new Point(cx + w * 0.62, t), new Point(cx, t), true, true);
            }
            g.Freeze();
            return g;
        }

        // ───────────────────────── OLHOS ─────────────────────────

        /// <summary>
        /// Marcos de uma fenda palpebral, ja resolvidos para curvas. O olho e
        /// desenhado por DUAS Beziers (palpebra superior e inferior) que partem
        /// dos mesmos cantos: e por isso que a fenda e uma amendoa e nao um
        /// circulo, e por isso que a linha de cilio fica exatamente na borda do
        /// preenchimento em vez de "por cima" dele.
        ///
        /// O canto externo sobe com <paramref name="tilt"/> e o interno desce na
        /// mesma medida: a fenda INCLINA, como a de um rosto de verdade.
        /// </summary>
        private readonly struct EyeFrame
        {
            public readonly Point Inner;
            public readonly Point Outer;
            public readonly Point UpC1, UpC2;
            public readonly Point LoC1, LoC2;
            public readonly double UpAmp, LoAmp;
            public readonly double Width;

            public EyeFrame(double cx, double tiltDeg, double widthScale, double aperture, double curve, double squint)
            {
                double hw = EyeHalfW * Math.Max(0.35, widthScale);
                double ap = Math.Max(0.0, aperture);

                // curva 0 = fenda seca; curva 1 = palpebra superior cheia e
                // inferior rasa, que e o que faz o olho parecer redondo.
                double up = EyeHalfH * ap * (0.58 + 0.42 * Clamp01(curve));
                double lo = EyeHalfH * ap * (0.54 - 0.28 * Clamp01(curve)) * (1.0 - 0.72 * Clamp01(squint));

                double rad = tiltDeg * Math.PI / 180.0;
                double lift = hw * 0.30 * Math.Max(-1.4, Math.Min(1.4, rad * 3.0));

                Inner = new Point(cx - hw, EyeCy + lift);
                Outer = new Point(cx + hw, EyeCy - lift);

                double w = hw * 2.0;
                UpC1 = new Point(Inner.X + w * 0.30, Inner.Y - up * 1.04);
                UpC2 = new Point(Outer.X - w * 0.33, Outer.Y - up * 1.00);
                LoC1 = new Point(Outer.X - w * 0.30, Outer.Y + lo);
                LoC2 = new Point(Inner.X + w * 0.35, Inner.Y + lo * 0.82);

                UpAmp = up;
                LoAmp = lo;
                Width = w;
            }
        }

        /// <summary>Fenda palpebral preenchida (esclera escura).</summary>
        public static Geometry EyeShape(double cx, in AiFaceExpression e) =>
            EyeShape(cx, e.EyeTilt, e.EyeWidth, e.EyeAperture, e.EyeCurve, e.EyeSquint);

        public static Geometry EyeShape(double cx, double tiltDeg, double widthScale, double aperture, double curve, double squint)
        {
            var f = new EyeFrame(cx, tiltDeg, widthScale, aperture, curve, squint);
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(f.Inner, isFilled: true, isClosed: true);
                c.BezierTo(f.UpC1, f.UpC2, f.Outer, true, true);
                c.BezierTo(f.LoC1, f.LoC2, f.Inner, true, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Linha de cilio: SO a palpebra superior, traco grosso. E a peca que
        /// mais "desenha" o olho - um olho humano e definido pela palpebra, nao
        /// pelo globo. Ela acompanha a fenda exatamente, entao fecha junto.
        /// </summary>
        public static Geometry EyeLidUpper(double cx, in AiFaceExpression e) =>
            EyeLidUpper(cx, e.EyeTilt, e.EyeWidth, e.EyeAperture, e.EyeCurve);

        public static Geometry EyeLidUpper(double cx, double tiltDeg, double widthScale, double aperture, double curve)
        {
            var f = new EyeFrame(cx, tiltDeg, widthScale, aperture, curve, 0.0);
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(f.Inner, isFilled: false, isClosed: false);
                c.BezierTo(f.UpC1, f.UpC2, f.Outer, true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Marca da palpebra inferior, parcial e fina. Aparece so quando a fenda
        /// esta aberta o bastante - em olho semicerrado ela vira ruido.
        /// </summary>
        public static Geometry EyeLidLower(double cx, in AiFaceExpression e)
        {
            var f = new EyeFrame(cx, e.EyeTilt, e.EyeWidth, e.EyeAperture, e.EyeCurve, e.EyeSquint);
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(f.Inner.X + f.Width * 0.40, f.Inner.Y + f.LoAmp * 0.90), isFilled: false, isClosed: false);
                c.BezierTo(
                    new Point(f.Inner.X + f.Width * 0.55, f.Inner.Y + f.LoAmp * 1.05),
                    new Point(f.Outer.X - f.Width * 0.58, f.Outer.Y + f.LoAmp * 1.00),
                    new Point(f.Outer.X - f.Width * 0.42, f.Outer.Y + f.LoAmp * 0.86),
                    true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Olho fechado: um unico arco que PESA para baixo. Circulo "achatado"
        /// fechado pareceria um olho de robô; o arco e o que le como pálpebra.
        /// </summary>
        public static Geometry EyeShut(double cx, double tiltDeg, double widthScale)
        {
            double hw = EyeHalfW * Math.Max(0.35, widthScale);
            double rad = tiltDeg * Math.PI / 180.0;
            double lift = hw * 0.30 * Math.Max(-1.4, Math.Min(1.4, rad * 3.0));
            double x0 = cx - hw, y0 = EyeCy + lift;
            double x1 = cx + hw, y1 = EyeCy - lift;
            double sag = 1.9;

            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(x0, y0 - 0.4), isFilled: false, isClosed: false);
                c.BezierTo(
                    new Point(x0 + hw * 0.85, y0 + sag * 1.25),
                    new Point(x1 - hw * 0.85, y1 + sag * 1.25),
                    new Point(x1, y1 - 0.4), true, false);
            }
            g.Freeze();
            return g;
        }

        // ─────────────────────── SOBRANCELHAS ───────────────────────

        /// <summary>
        /// Sobrancelha como forma PREENCHIDA e afilada, nao como traco: o lado
        /// interno e 2.5x mais grosso que o externo, que e como uma sobrancelha
        /// real afina. Um traco uniforme le como rabisco.
        ///
        /// <paramref name="isLeft"/> inverte a direcao para que a forma fique
        /// espelhada sem precisar de Matrix (Geometry.Transform lanca em
        /// geometria congelada).
        /// </summary>
        public static Geometry Brow(double cx, bool isLeft, in AiFaceExpression e)
        {
            double innerLift = e.BrowInnerLift;
            double outerLift = e.BrowOuterLift;

            double innerX, outerX, innerY, outerY;
            if (isLeft)
            {
                innerX = cx + BrowHalfW * 0.86;   // canto interno (nariz)
                outerX = cx - BrowHalfW;
                innerY = BrowY - innerLift;
                outerY = BrowY - outerLift;
            }
            else
            {
                innerX = cx - BrowHalfW * 0.86;
                outerX = cx + BrowHalfW;
                innerY = BrowY - innerLift;
                outerY = BrowY - outerLift;
            }

            double t0 = 1.15 * e.BrowThickness;   // meia-espessura interna
            double t1 = 0.44 * e.BrowThickness;   // meia-espessura externa
            double arch = e.BrowCurve * 3.0;      // -1..1 -> +/- 3 unidades

            double mx = (innerX + outerX) * 0.5;
            double my = (innerY + outerY) * 0.5;

            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(innerX, innerY - t0), isFilled: true, isClosed: true);
                c.BezierTo(
                    new Point(mx - 1.2, my - t0 - arch),
                    new Point(mx + 1.2, my - t1 - arch),
                    new Point(outerX, outerY - t1), true, true);
                c.LineTo(new Point(outerX, outerY + t1), true, true);
                c.BezierTo(
                    new Point(mx + 1.2, my + t1 + arch * 0.55),
                    new Point(mx - 1.2, my + t0 + arch * 0.55),
                    new Point(innerX, innerY + t0), true, true);
            }
            g.Freeze();
            return g;
        }

        // ─────────────────────────── BOCA ───────────────────────────

        /// <summary>
        /// Selo labial. O ponto de controle FICA ABAIXO do centro quando o humor
        /// e positivo, o que faz os cantos subirem - e por isso que "curva
        /// positiva = sorriso" com uma unica Bezier e um unico numero.
        /// </summary>
        public static Geometry MouthSeam(in AiFaceExpression e)
        {
            double hw = MouthHalfW * Math.Max(0.30, e.MouthWidth);
            double y = MouthCy;
            double ctrl = y + 5.0 * Math.Max(-1.6, Math.Min(1.6, e.MouthCurve));

            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(FaceCx - hw, y), isFilled: false, isClosed: false);
                c.BezierTo(
                    new Point(FaceCx - hw * 0.45, ctrl),
                    new Point(FaceCx + hw * 0.45, ctrl),
                    new Point(FaceCx + hw, y), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Boca aberta: lente entre o arco superior (labio) e o inferior. So e
        /// construida acima de ~5% de abertura; abaixo disso a boca e um traco
        /// e nada de geometria e'alterada.
        /// </summary>
        public static Geometry MouthOpen(in AiFaceExpression e)
        {
            double hw = MouthHalfW * Math.Max(0.30, e.MouthWidth);
            double y = MouthCy;
            double curve = 5.0 * Math.Max(-1.6, Math.Min(1.6, e.MouthCurve));
            double open = 6.2 * Math.Max(0.0, Math.Min(1.4, e.MouthOpen));

            double upCtrl = y + curve - open * 0.92;
            double loCtrl = y + curve + open * 1.00;

            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(FaceCx - hw, y), isFilled: true, isClosed: true);
                c.BezierTo(
                    new Point(FaceCx - hw * 0.45, upCtrl),
                    new Point(FaceCx + hw * 0.45, upCtrl),
                    new Point(FaceCx + hw, y), true, true);
                c.BezierTo(
                    new Point(FaceCx + hw * 0.45, loCtrl),
                    new Point(FaceCx - hw * 0.45, loCtrl),
                    new Point(FaceCx - hw, y), true, true);
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Linha dos dentes, encaixada logo abaixo do arco superior da boca
        /// aberta. Representa a denticao cerrada de raiva sem virar careta.
        /// </summary>
        public static Geometry MouthTeeth(in AiFaceExpression e)
        {
            double hw = MouthHalfW * Math.Max(0.30, e.MouthWidth) * 0.78;
            double y = MouthCy + 5.0 * Math.Max(-1.6, Math.Min(1.6, e.MouthCurve)) - 1.4 * Math.Min(1.4, e.MouthOpen);

            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(FaceCx - hw, y), isFilled: false, isClosed: false);
                c.BezierTo(
                    new Point(FaceCx - hw * 0.4, y + 1.1),
                    new Point(FaceCx + hw * 0.4, y + 1.1),
                    new Point(FaceCx + hw, y), true, false);
            }
            g.Freeze();
            return g;
        }

        // ─────────────────────────── NARIZ ─────────────────────────

        /// <summary>
        /// Duas tracos minimos: a ponte e a base. Opacidade baixissima
        /// (0.14..0.34). E o detalhe que mais humaniza um rosto - sem ele a
        /// figura le como mascara; com ele, le como pessoa. Por isso existe
        /// mesmo no rosto "tecnologico".
        /// </summary>
        public static readonly Geometry Nose = BuildNose();

        private static Geometry BuildNose()
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(48.7, 53.0), isFilled: false, isClosed: false);
                c.BezierTo(new Point(49.4, 56.6), new Point(50.5, 58.8), new Point(52.9, 59.6), true, false);

                c.BeginFigure(new Point(47.5, 60.3), isFilled: false, isClosed: false);
                c.BezierTo(new Point(48.7, 61.4), new Point(50.8, 61.4), new Point(52.0, 60.3), true, false);
            }
            g.Freeze();
            return g;
        }

        // ───────────────────────── LAGRIMAS ────────────────────────

        /// <summary>
        /// Gota. Nao e um circulo com blur: e uma gota de verdade (larga em cima,
        /// pontuda embaixo) porque a silhueta e o que faz ela ler como lagrima.
        /// </summary>
        public static Geometry TearDrop(double cx, double cy, double size) =>
            BuildTear(cx, cy, size);

        private static Geometry BuildTear(double cx, double cy, double s)
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(cx, cy - s * 1.15), isFilled: true, isClosed: true);
                c.BezierTo(
                    new Point(cx + s * 0.92, cy - s * 0.10),
                    new Point(cx + s * 0.96, cy + s * 0.86),
                    new Point(cx, cy + s * 1.15), true, true);
                c.BezierTo(
                    new Point(cx - s * 0.96, cy + s * 0.86),
                    new Point(cx - s * 0.92, cy - s * 0.10),
                    new Point(cx, cy - s * 1.15), true, true);
            }
            g.Freeze();
            return g;
        }

        // ────────────────────── ICONE DE RAIO ──────────────────────

        /// <summary>
        /// O raio do hover (item 7 do pedido).
        ///
        /// Nao e um raio de emoji: e um simbolo de energia com a ponta superior
        /// chanfrada e dois encaixes horizontais retos. O chanfro e o que separa
        /// "desenhado" de "achado" - uma ponta viva demais grita icone de
        /// sistema. Mede 17.8 x 35.2 num espaco de 100, ou seja, cerca de 1/3 do
        /// rosto: presente, nunca dominante.
        /// </summary>
        public static readonly Geometry Bolt = BuildBolt();

        private static Geometry BuildBolt()
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(56.1, 32.4), isFilled: true, isClosed: true);
                c.LineTo(new Point(51.9, 32.4), true, true);   // chanfro da ponta
                c.LineTo(new Point(41.1, 52.8), true, true);   // ombro esquerdo
                c.LineTo(new Point(48.5, 52.8), true, true);   // entalhe
                c.LineTo(new Point(44.7, 67.6), true, true);   // ponta inferior
                c.LineTo(new Point(58.9, 45.6), true, true);   // aresta direita
                c.LineTo(new Point(51.1, 45.6), true, true);   // entalhe
            }
            g.Freeze();
            return g;
        }

        /// <summary>
        /// Nucleo luminoso do raio: uma fatia estreita clara ao longo da
        /// espinha. E o que faz o simbolo parecer METAL e nao "desenho
        /// preenchido de cor".
        /// </summary>
        public static readonly Geometry BoltCore = BuildBoltCore();

        private static Geometry BuildBoltCore()
        {
            var g = new StreamGeometry();
            using (StreamGeometryContext c = g.Open())
            {
                c.BeginFigure(new Point(52.0, 35.2), isFilled: true, isClosed: true);
                c.LineTo(new Point(48.9, 35.2), true, true);
                c.LineTo(new Point(45.6, 51.0), true, true);
                c.LineTo(new Point(48.2, 51.0), true, true);
            }
            g.Freeze();
            return g;
        }

        // ─────────────────────────── HELPERS ───────────────────────

        private static double Clamp01(double v) => v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);

        /// <summary>
        /// Texto de debug: todas as medidas numa string. Usado pelo arnes de
        /// teste para provar que a malha nao estourou 0..100 em nenhum dos 18
        /// estados - e a garantia de que nao haverá recorte em 150% de escala.
        /// </summary>
        public static string Describe(in AiFaceExpression e) =>
            string.Format(CultureInfo.InvariantCulture,
                "ap={0:0.00} w={1:0.00} tilt={2:0.0} curve={3:0.00} sq={4:0.00} " +
                "browIn={5:0.00} browOut={6:0.00} browArc={7:0.00} asym={8:0.00} " +
                "mouthW={9:0.00} mouthO={10:0.00} mouthC={11:0.00} press={12:0.00} " +
                "glow={13:0.00} halo={14:0.00} tear={15:0.00} breath={16:0.000}/{17:0.00}s tremor={18:0.00}",
                e.EyeAperture, e.EyeWidth, e.EyeTilt, e.EyeCurve, e.EyeSquint,
                e.BrowInnerLift, e.BrowOuterLift, e.BrowCurve, e.BrowAsymmetry,
                e.MouthWidth, e.MouthOpen, e.MouthCurve, e.MouthPress,
                e.Glow, e.Halo, e.Tear, e.BreathAmp, e.BreathPeriod, e.Tremor);
    }
}
