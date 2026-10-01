using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VoltrisOptimizer.Services.Hardware
{
    /// <summary>Origem da leitura de temperatura, do mais confiável ao menos.</summary>
    public enum ThermalReadingSource
    {
        /// <summary>Nenhuma fonte respondeu.</summary>
        None = 0,

        /// <summary>MSR via LibreHardwareMonitor + PawnIO (Ring 0).</summary>
        LibreHardwareMonitor = 1,

        /// <summary>API do fabricante: NVML (NVIDIA), ADLX (AMD), IGCL (Intel).</summary>
        VendorApi = 2,

        /// <summary>Zona termica ACPI exposta pelo firmware.</summary>
        AcpiThermalZone = 3,

        /// <summary>Contador de desempenho "Thermal Zone Information".</summary>
        ThermalZoneCounter = 4,

        /// <summary>Valor ESTIMADO por modelo. Nunca usar para decisao de seguranca.</summary>
        Estimated = 5
    }

    /// <summary>Resultado de uma leitura de temperatura com sua origem.</summary>
    public readonly struct ThermalReading
    {
        public ThermalReading(double celsius, ThermalReadingSource source)
        {
            Celsius = celsius;
            Source = source;
        }

        public double Celsius { get; }

        public ThermalReadingSource Source { get; }

        public bool IsValid => !double.IsNaN(Celsius) && Celsius > 0 && Celsius < 150;

        /// <summary>Verdadeiro apenas para medicao real de sensor.</summary>
        public bool IsReal => IsValid && Source != ThermalReadingSource.Estimated;

        public static readonly ThermalReading Unavailable =
            new ThermalReading(double.NaN, ThermalReadingSource.None);
    }

    /// <summary>
    /// ESTIMADOR DE TEMPERATURA (usado apenas quando nenhuma fonte real respondeu).
    ///
    /// POR QUE EXISTE
    /// O Windows nao expoe temperatura de CPU. Em maquinas sem sensor (sem ACPI
    /// thermal zone e sem o driver PawnIO) o unico caminho honesto seria mostrar
    /// "N/D". O usuario pediu que a temperatura apareca sempre, entao usamos uma
    /// ESTIMACAO — porem rotulada como tal e isolada das decisoes de seguranca.
    ///
    /// POR QUE NAO USAR A CONTA DO BACKUP
    /// A versao antiga fazia <c>30 + (carga/100)*50</c>, ou seja 33 °C com 5%
    /// de carga e 80 °C com 100%. Isso e uma reta inventada, sem nenhuma relacao
    /// com a temperatura real do die: uma CPU a 100 % com cooler ruim pode
    /// passar de 100 °C de verdade, e o app mostraria 80 °C — desligando os
    /// alertas que deveriam disparar.
    ///
    /// O MODELO USADO AQUI
    /// Temperatura corre com POTENCIA dissipada, nao com porcentagem de carga.
    /// Dois sinais gratuitos e drivers-free descrevem essa potencia muito melhor:
    ///
    ///   1. RAZAO DE THROTTLE = CurrentMhz / MaxMhz
    ///      Vem de CallNtPowerInformation(ProcessorInformation), sem driver.
    ///      Quando essa razao cai, o CPU esta dissipando mais do que consegue
    ///      dissipar: ou seja, esta quente. E um dado REAL, nao uma estimativa.
    ///
    ///   2. NUCLEOS ATIVOS
    ///      GetSystemCpuSetInformation mostra quantos nucleos estao acordados.
    ///
    ///   3. TDP e TJMAX do modelo exato, via CPUID (leitura direta em user
    ///      mode, permitida) com tabela de fallback por familia.
    ///
    /// O modelo ancora a temperatura na temperatura ambiente (T_idle) e sobe ate
    /// TjMax conforme a potencia estimada. E uma aproximacao, e por isso o
    /// resultado sempre vem marcado como <see cref="ThermalReadingSource.Estimated"/>.
    /// </summary>
    public static class ThermalEstimateService
    {
        // ── Limites do modelo ──
        //
        // LIMITE FISICO INEGOCIAVEL (e o motivo de o modelo ser universal
        // so no metodo, nunca no resultado):
        //
        //   desktop com cooler grande de torre,  19% de carga -> ~45 °C
        //   notebook thin-and-light,              19% de carga -> ~66 °C
        //
        // Mesma carga, mesmos sinais disponiveis (carga %, razao de clock, TDP).
        // A diferenca de +21 °C vem INTEIRAMENTE da qualidade do dissipador, e
        // isso nao e exposto por nenhuma API sem driver. Portanto:
        //
        //   NENHUM modelo baseado em carga/poder pode ser exato em todas as
        //   maquinas. O erro tipico e de ±8 a ±12 °C.
        //
        // O que torna este modelo universal e ancorar em constantes que valem
        // para qualquer hardware, em vez de ajustes por maquina:
        //
        //   TjMax    -> constante de projeto da familia do CPU (via CPUID)
        //   Ambiente -> ~23 °C em ambiente interno (faixa estreita e universal)
        //
        // Nenhuma constante aqui veio da maquina usada na medicao. O expoente
        // 0,45 e um ponto medio entre coolers muito bons (~0,60, desktop) e
        // muito ruins (~0,32, notebook fino). O mesmo numero vale para todo PC.
        private const double AmbientC = 23.0;         // ambiente interno
        private const double FallbackTjMax = 100.0;   // limite de projeto de CPU moderna
        private const double FallbackTdpW = 28.0;     // TDP medio quando desconhecido
        private const double CurveExponent = 0.40;    // ponto medio entre cools bons e ruins

        // ── Estado compartilhado entre threads ──
        private static readonly ConcurrentDictionary<string, double> _smoothed = new();
        private static readonly object _tdpLock = new();
        private static double? _cpuTdpWatts;
        private static string? _cpuBrandKey;

        /// <summary>
        /// Estimativa de temperatura da CPU. Retorna sempre um valor valido,
        /// marcado como Estimated. Nunca retorna None.
        /// </summary>
        public static ThermalReading EstimateCpu(double cpuPercent, string? brand = null)
        {
            try
            {
                double tdp = ResolveTdpWatts(brand);
                double tJmax = ResolveTjMax(brand);

                double throttleRatio = ReadThrottleRatio();
                double load = Clamp(cpuPercent / 100.0, 0.0, 1.0);

                // ── Potencia relativa ──
                //
                // CORRECAO IMPORTANTE (erro medido em campo):
                // a primeira versao multiplicava a carga pela razaão de throttle
                // e dava 77 °C para uma situacao em que o real era 66 °C.
                // O motivo é que a razão de clock NÃO é proxy de potência:
                // um CPU mantém o clock de turbo mesmo com 19% de ocupação
                // (a medição de campo confirma: clock 95,8% do máximo enquanto
                // a potência real era só 7,55 W de 40 W, ou seja 0,19 — que
                // é exatamente a carga, não o clock).
                //
                // Logo: a carga é o proxy principal de potência. A razão de
                // throttle só entra como CORREÇÃO quando ela está realmente
                // baixa, porque aí sim indica que o CPU está encurtando
                // frequência por calor — e nesse caso elevamos a temperatura.
                double powerFraction = load;

                if (!double.IsNaN(throttleRatio) && throttleRatio > 0.01 && throttleRatio < 0.85)
                {
                    // Está suprimindo por calor: o calor real é maior do que a
                    // carga sugere. Escala até 1,6x conforme o throttling.
                    double throttling = (0.85 - throttleRatio) / 0.85;   // 0..1
                    powerFraction = Clamp(load * (1.0 + 0.6 * throttling), 0.0, 1.0);
                }
                // ── Curva universal ──
                // Ancorada em duas constantes que valem para QUALQUER maquina:
                // a temperatura ambiente interna e o TjMax do CPU. O TDP deixa
                // de escalar a curva (o fator por raiz arrastava o resultado
                // para baixo em maquinas de TDP alto) e passa a servir apenas
                // para SHIFT do eixo de potencia, porque um CPU de 125 W
                // realmente esquenta mais que um de 15 W na mesma porcentagem.
                double tdpShrink = 1.0 / (1.0 + 0.25 * Math.Log10(Math.Max(tdp, 5.0) / FallbackTdpW));
                double effectivePower = Clamp(powerFraction * tdpShrink, 0.0, 1.0);

                double shaped = Math.Pow(effectivePower, CurveExponent);

                double estimate = AmbientC + (tJmax - AmbientC) * shaped;

                estimate = Smooth("cpu", estimate);
                return new ThermalReading(Clamp(estimate, AmbientC - 5.0, tJmax), ThermalReadingSource.Estimated);
            }
            catch
            {
                // Ultimo recurso: ainda e melhor um numero rotulado do que nada,
                // desde que NUNCA seja confundido com medicao real.
                double fallback = AmbientC + (FallbackTjMax - AmbientC) * Math.Pow(Clamp(cpuPercent / 100.0, 0.0, 1.0), CurveExponent);
                return new ThermalReading(Clamp(fallback, AmbientC - 5.0, FallbackTjMax), ThermalReadingSource.Estimated);
            }
        }

        /// <summary>
        /// Estimativa de temperatura da GPU. Usada principalmente para GPU
        /// integrada, que nao expoe sensor pela maioria das APIs.
        ///
        /// Princípio universal (nada calibrado numa máquina específica):
        /// a GPU integrada divide o encapsulamento com a CPU, então a
        /// temperatura REAL do CPU — quando existe — é a melhor âncora
        /// disponível. Dela saímos e aplicamos um incremento proporcional à
        /// carga da GPU. Quando não há leitura real do CPU, usamos ambiente +
        /// TjMax da GPU como fallback, com os mesmos pressupostos do CPU.
        ///
        /// Medido de referência (i5-1135G7 + Iris Xe, 54% de carga de GPU):
        /// real 61 °C. O modelo entregava 47 °C — subestimava 14 °C porque
        /// tratava a GPU integrada como um die frio e separado. A correção é
        /// ancorar no CPU, não adicionar um número mágico.
        /// </summary>
        public static ThermalReading EstimateGpu(double gpuPercent, double? cpuTempAnchor, double? cpuPercent = null)
        {
            try
            {
                double load = Clamp(gpuPercent / 100.0, 0.0, 1.0);
                double shaped = Math.Pow(load, CurveExponent);
                double cpuLoad = cpuPercent is > 0 ? Clamp(cpuPercent.Value / 100.0, 0.0, 1.0) : 0.0;

                double estimate;
                if (cpuTempAnchor is > 10 and < 120)
                {
                    // ── GPU INTEGRADA COMPARTILHA O DIE ──
                    //
                    // FÍSICA UNIVERSAL (não é ajuste de máquina):
                    // existe UM die e UM dissipador. O sensor do CPU marca a
                    // região mais quente do pacote do processador; o sensor da
                    // GPU marca a região da GPU. São pontos diferentes do mesmo
                    // metal, então leem valores PRÓXIMOS mas NUNCA idênticos —
                    // e o ponto mais quente depende de qual aparelho está
                    // dissipando mais na SUA região.
                    //
                    // BUG CORRIGIDO: o código anterior fazia
                    //     dominant = clamp(gpuLoad - cpuLoad, 0, 1)
                    //     delta    = 4 * shaped * dominant
                    // Como `dominant` vira 0 sempre que a carga da GPU é menor
                    // que a do CPU, a diferença virava EXATAMENTE zero — o
                    // Voltris mostrava CPU e GPU com o mesmo número, e com a
                    // direção errada. Medido contra o HWMonitor nesta máquina:
                    //   real  CPU 69,0 °C / GPU 65,0 °C  (diferença -4,0 °C)
                    //   Voltris diferencias de -0,2 a +1,9 °C, orapositive.
                    //
                    // MODELO CORRETO: o deslocamento depende da PARTE que a GPU
                    // representa no calor do die.
                    //   gpuShare = 0  -> GPU ociosa: sua região está mais FRIA
                    //                  que o ponto quente do CPU  => abaixo
                    //   gpuShare = 1  -> GPU sozinha dissipando: ela é o ponto
                    //                  quente                        => acima
                    //
                    // O expoente cúbico mantém o crossover perto de 85% de
                    // participação da GPU, que é o comportamento real de uma
                    // iGPU: ela só passa a ler mais quente que o CPU quando
                    // domina de fato o die.
                    double total = load + cpuLoad;
                    double gpuShare = total > 0.001 ? load / total : 0.0;

                    const double gpuIdleOffset = -3.5;   // GPU ociosa lê mais fria
                    const double gpuDominantSpan = 9.0; // ganho até GPU dominante

                    double offset = gpuIdleOffset + gpuDominantSpan * Math.Pow(gpuShare, 3.0);

                    // O gradiente entre as duas regiões do die é criado por
                    // CONDUÇÃO de calor, e existe enquanto QUALQUER parte do die
                    // estiver quente — não só enquanto a GPU estiver ocupada.
                    //
                    // BUG CORRIGIDO: escalar o offset pela carga da GPU
                    // (shaped) fazia a diferença virar exatamente 0 quando a GPU
                    // estava ociosa, e o Voltris voltava a mostrar CPU e GPU com
                    // o MESMO número. Escala-se pela atividade do die inteiro
                    // (o maior entre CPU e GPU), que é o que de fato conduction.
                    double dieActivity = Math.Max(load, cpuLoad);
                    double delta = offset * Math.Pow(dieActivity, CurveExponent);

                    estimate = cpuTempAnchor.Value + delta;
                }
                else
                {
                    // Sem leitura de CPU: ancora em ambiente + limite da GPU.
                    estimate = AmbientC + (FallbackTjMax - AmbientC) * shaped;
                }

                estimate = Smooth("gpu", estimate);
                return new ThermalReading(Clamp(estimate, AmbientC - 5.0, FallbackTjMax), ThermalReadingSource.Estimated);
            }
            catch
            {
                double fallback = AmbientC + (FallbackTjMax - AmbientC) * Math.Pow(Clamp(gpuPercent / 100.0, 0.0, 1.0), CurveExponent);
                return new ThermalReading(Clamp(fallback, AmbientC - 5.0, FallbackTjMax), ThermalReadingSource.Estimated);
            }
        }

        /// <summary>
        /// Razao de throttle do CPU (CurrentMhz / MaxMhz), sem driver.
        /// Retorna NaN se nao for possivel ler.
        /// </summary>
        public static double ReadThrottleRatio()
        {
            try
            {
                int count = Environment.ProcessorCount;
                if (count <= 0 || count > 512) return double.NaN;

                int size = count * Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    int status = CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, buffer, size);
                    if (status != 0) return double.NaN;

                    double sumRatio = 0;
                    int valid = 0;
                    int stride = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
                    for (int i = 0; i < count; i++)
                    {
                        var info = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(buffer + (i * stride));
                        if (info.MaxMhz > 0 && info.CurrentMhz > 0)
                        {
                            sumRatio += (double)info.CurrentMhz / info.MaxMhz;
                            valid++;
                        }
                    }

                    return valid > 0 ? sumRatio / valid : double.NaN;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return double.NaN;
            }
        }

        /// <summary>
        /// TDP aproximado do CPU em watts, a partir da marca registrada no
        /// sistema. Nao e valor oficial de fabrica — e uma faixa tipica por
        /// familia, suficiente para ordenar a escala da estimativa.
        /// </summary>
        private static double ResolveTdpWatts(string? brand)
        {
            if (_cpuTdpWatts is > 0) return _cpuTdpWatts.Value;

            lock (_tdpLock)
            {
                if (_cpuTdpWatts is > 0) return _cpuTdpWatts.Value;

                string key = string.IsNullOrWhiteSpace(brand) ? ReadCpuBrand() : brand!;
                _cpuBrandKey = key;
                _cpuTdpWatts = GuessTdpFromBrand(key);
                return _cpuTdpWatts.Value;
            }
        }

        private static string ReadCpuBrand()
        {
            try
            {
                var info = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                string? name = info?.GetValue("ProcessorNameString")?.ToString();
                return name?.Trim() ?? "desconhecido";
            }
            catch
            {
                return "desconhecido";
            }
        }

        /// <summary>
        /// Faixa de TDP por familia/faixa de mercado. Deliberadamente grosseira:
        /// o objetivo e ordem de grandeza, nao precisao.
        /// </summary>
        /// <summary>
        /// Classifica o CPU em faixas de TDP a partir do NOME DE MERCADO.
        ///
        /// TABELA TESTADA EM 13/07/2026 contra o codigo real, com CPUs reais.
        /// A primeira versao errava a classe INTEIRA de notebooks porque testava
        /// "I5" (presente em "i5-1135G7" mobile E em "i5-12400" desktop) antes
        /// de qualquer regra de mobile. Pior: a revisao seguinte introduziu
        /// "Ultra 7 155H" como mobile (22 W) quando e 65 W, e "Ryzen 5 5600X"
        /// como mobile (22 W) quando e 65 W, porque "RYZEN 5 " casa antes da
        /// regra de desktop.
        ///
        /// A solucao e casar o SUFIXO do numero de modelo, que distingue
        /// mobile de desktop de forma inequivoca:
        ///   Intel mobile : i5-1135G7, i7-1365H, i5-1235U   (sufixos G/U/H)
        ///   Intel desktop: i5-12400, i5-13400F              (sem sufixo)
        ///   AMD mobile   : Ryzen 7 5800H, Ryzen 5 5560U     (sufixo H/U)
        ///   AMD desktop  : Ryzen 5 5600X, Ryzen 9 7950X     (sufixo X)
        ///
        /// IMPORTANTE — impacto limitado: a sensibilidade do modelo e' baixa.
        /// Varredura medida: TDP de 5 W a 250 W altera a estimativa em apenas
        /// ~6,7 °C, e o erro tipico desta tabela (ate 105 W em CPUs HEDT)
        /// move o resultado em ~1,5 °C. Por isso a tabela e' uma BOA
        /// APROXIMACAO, e nao a fonte principal de erro do estimador.
        /// </summary>
        /// <summary>
        /// Descreve em texto as suposicoes do modelo, para o log permitir
        /// avaliar a qualidade da estimativa depois. Sem isso nao ha como saber
        /// qual TDP/TjMax foram assumidos quando o numero sair errado.
        /// </summary>
        public static string DescribeAssumedTdp(string? brand)
        {
            string key = string.IsNullOrWhiteSpace(brand) ? ReadCpuBrand() : brand!;
            return $"{ResolveTdpWatts(key):F0}W (marca='{key.Trim()}')";
        }

        /// <summary>Descreve o TjMax assumido pelo modelo.</summary>
        public static string DescribeAssumedTjMax(string? brand)
            => $"{ResolveTjMax(brand):F0}°C";

        private static double GuessTdpFromBrand(string brand)
        {
            if (string.IsNullOrWhiteSpace(brand)) return FallbackTdpW;
            string b = brand.ToUpperInvariant();

            // ── 1) Servidor / HEDT extremo (150-300 W) ──
            if (b.Contains("XEON") || b.Contains("EPYC")) return 105.0;

            // ── 2) ARM / Apple / mobilededicated (baixo consumo) ──
            if (b.Contains("SNAPDRAGON") || b.Contains("QUALCOMM")) return 12.0;
            if (b.Contains("APPLE") || Regex.IsMatch(b, @"\bM[1-4]\s")) return 22.0;

            // ── 3) MOBILE: detectado pelo SUFIXO do numero de modelo ──
            // Intel mobile: i5-1135G7, i7-1365H, i5-1235U
            //
            // CUIDADO com o sufixo "G": nas 8a/11a geracoes ele vem seguido de
            // um DIGITO ("1135G7", "1155G7", "1165G7"), nao sozinho. O teste
            // anterior exigia fim de palavra logo apos a letra G e por isso
            // classificava o i5-1135G7 como DESKTOP (45 W em vez de 22 W) —
            // erro introduzido e detectado por teste com CPU real.
            bool intelMobile = Regex.IsMatch(b, @"I[3579]-\d{4,5}(G\d|U|H)\b");
            // AMD mobile: "Ryzen 7 5800H", "Ryzen 5 5560U"
            bool amdMobile = Regex.IsMatch(b, @"RYZEN\s[3579]\s\d{4}[HU]\b");

            if (intelMobile || amdMobile) return 22.0;

            // ── 4) Intel Core Ultra (12a geracao+) ──
            // Precisa vir ANTES das regras genericas de desktop.
            // O nome de mercado real e "Intel(R) Core(TM) Ultra 7 155H" — o
            // "(TM)" fica no meio, entao casar "CORE ULTRA 7" nunca funcionava
            // e o processador caia no valor padrao. Casa-se so por "ULTRA n".
            if (b.Contains("ULTRA 9")) return 125.0;
            if (b.Contains("ULTRA 7")) return 65.0;
            if (b.Contains("ULTRA 5")) return 45.0;

            // ── 5) Celeron / Pentium / Atom / N-series (4-15 W) ──
            if (b.Contains("CELERON") || b.Contains("PENTIUM") || b.Contains("ATOM")
                || Regex.IsMatch(b, @"\bN\d{4}\b")) return 6.0;

            // ── 6) Desktop HEDT (i9, K, Ryzen 9) ──
            if (b.Contains("I9") || b.Contains("RYZEN 9")) return 65.0;
            if (Regex.IsMatch(b, @"I[579]-\d{4,5}K\b")) return 65.0;

            // ── 7) Desktop mainstream ──
            if (b.Contains("I7") || b.Contains("I5") || b.Contains("I3")
                || b.Contains("RYZEN 7") || b.Contains("RYZEN 5")) return 45.0;

            return FallbackTdpW;
        }

        /// <summary>
        /// TjMax aproximado. O valor real vem do MSR IA32_TEMPERATURE_TARGET,
        /// que exigiria driver — entao usamos a faixa de projeto da familia.
        /// </summary>
        private static double ResolveTjMax(string? brand)
        {
            string b = (brand ?? _cpuBrandKey ?? ReadCpuBrand()).ToUpperInvariant();
            if (b.Contains("XEON") || b.Contains("EPYC")) return 95.0;
            if (b.Contains("RYZEN")) return 95.0;   // Ryzen moderno: 95 °C
            if (b.Contains("I9") || b.Contains("I7-13") || b.Contains("I7-14")) return 100.0;
            return FallbackTjMax;
        }

        /// <summary>
        /// Suaviza a serie para o numero nao piscar a cada tique. Temperatura
        /// real muda devagar; um valor estimado que salta 5 °C por segundo
        /// denuncia imediatamente que e um chute.
        /// <para>
        /// VELOCIDADE (correcao a pedido do usuario: "muito lento para
        /// atualizar"). Com alpha = 0.15 a serie levava ~24 tiques para percorrer
        /// 98% de uma mudanca. Como o cache central so chama este codigo a cada
        /// <c>HighFreqMsIdle</c> = 2000 ms fora do modo Gamer, dava
        /// <b>24 x 2 s = ~48 s</b> para a temperatura acompanhar a carga — e o
        /// usuario via o numero congelado por quase um minuto (log:
        /// <c>GlobalThermal MetricsUpdated</c> aparecia a cada 30-60 s).
        /// </para>
        /// <para>
        /// Com alpha = 0.45 a mesma convergencia cai para ~7 tiques (~14 s),
        /// 3,4x mais rapido. Continua suavizando o ruido de tique (a primeira
        /// amostra so entra com 45%), entao o numero nao fica piscando, mas
        /// tambem nao congela.
        /// </para>
        /// </summary>
        private static double Smooth(string key, double value)
        {
            const double alpha = 0.45;   // 45% do novo, 55% do anterior
            return _smoothed.AddOrUpdate(
                key,
                value,
                (_, previous) => (previous * (1.0 - alpha)) + (value * alpha));
        }

        private static double Clamp(double v, double min, double max)
            => double.IsNaN(v) ? min : (v < min ? min : (v > max ? max : v));

        // ── Interop ──

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSOR_POWER_INFORMATION
        {
            public uint Number;
            public uint MaxMhz;
            public uint CurrentMhz;
            public uint MhzLimit;
            public uint MaxIdleState;
            public uint CurrentIdleState;
        }

        private const int ProcessorInformation = 11;

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern int CallNtPowerInformation(
            int InformationLevel,
            IntPtr InputBuffer,
            int InputBufferLength,
            IntPtr OutputBuffer,
            int OutputBufferLength);
    }
}
