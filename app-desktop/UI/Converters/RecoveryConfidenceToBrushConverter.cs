using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VoltrisOptimizer.Services.Recovery;

namespace VoltrisOptimizer.UI.Converters
{
    /// <summary>
    /// Converte o nível de confiança de um arquivo recuperado na cor do rótulo.
    ///
    /// A cor responde a UMA pergunta: o que o usuário vai obter ao extrair
    /// este arquivo. Ela não decora a categoria do arquivo nem o resultado da
    /// varredura — o motor jáfiltrou o que é lixo, e pintar cada item de uma
    /// cor diferente faria a lista virar arco-íris ilegível.
    ///
    ///   Integral   verde    os bytes estão dentro do registro MFT
    ///   Confirmado verde    assinatura confere e é contíguo
    ///   Parcial    âmbar    assinatura confere, mas a cópia sai com buracos
    ///   Provável   azul     formato sem magic number, contíguo
    ///   Incerto    cinza    sem magic number e fragmentado
    ///
    /// Cinza para "incerto" e não vermelho de propósito: um item incerto não
    /// é erro, é ausência de prova. Vermelho ali faria o usuário achar que
    /// algo deu errado na varredura.
    /// </summary>
    public class RecoveryConfidenceToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int rank;
            if (value is RecoveryConfidence conf)
                rank = (int)conf;
            else if (value is int r)
                rank = r;
            else
                return Resolve("TextSecondaryBrush", Color.FromRgb(140, 148, 160));

            switch (rank)
            {
                case 0: // Integral
                case 1: // Confirmado
                    return Resolve("SuccessBrush", Color.FromRgb(52, 199, 89));
                case 2: // Parcial
                    return Resolve("WarningBrush", Color.FromRgb(255, 170, 0));
                case 3: // Provável
                    return Resolve("InfoBrush", Color.FromRgb(86, 156, 214));
                default: // Incerto
                    return Resolve("TextSecondaryBrush", Color.FromRgb(140, 148, 160));
            }
        }

        /// <summary>
        /// Busca o pincel no recurso do tema ativo, para que a lista acompanhe
        /// a troca claro/escuro. A cor fixa é só o fallback quando o recurso
        /// não existe — o que aconteceria se alguém registrar este converter
        /// fora da árvore de temas.
        /// </summary>
        private static Brush Resolve(string resourceKey, Color fallback)
        {
            try
            {
                var app = Application.Current;
                if (app != null)
                {
                    var brush = app.TryFindResource(resourceKey) as Brush;
                    if (brush != null)
                        return brush;
                }
            }
            catch
            {
                // Recurso indisponível durante a inicialização do tema: cai no
                // fallback em vez de derrubar a linha inteira da lista.
            }
            return new SolidColorBrush(fallback);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
