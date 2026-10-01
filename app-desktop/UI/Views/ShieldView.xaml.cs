using System;
using System.Windows;
using System.Windows.Controls;
using VoltrisOptimizer.Services.License;
using VoltrisOptimizer.Services.License.Interfaces;
using VoltrisOptimizer.Services.License.Models;

namespace VoltrisOptimizer.UI.Views
{
    public partial class ShieldView : UserControl
    {
        private const double PageGutter = 40d;
        private const double ThreeColumnBreakpoint = 940d;
        private const double FourColumnBreakpoint = 1180d;

        public ShieldView()
        {
            InitializeComponent();

            DataContext = App.Services?.GetService(typeof(VoltrisOptimizer.UI.ViewModels.ShieldViewModel));

            // [FIX:SHIELD-CONTAGEM-ZERADA] SINCRONIZAR A CADA VISITAÇÃO DA ABA.
            //
            // O `ShieldViewModel` é um singleton resolvido do contêiner, então ele
            // é construído UMA vez — na primeira vez que o app resolve o serviço,
            // bem antes de o usuário abrir a aba. A recuperação do histórico que
            // corrigimos antes rodava no CONSTRUTOR, e por isso nunca viu as
            // ameaças: elas ainda não tinham acontecido.
            //
            // A prova está no log: a linha "[ShieldVM] Recuperando N ameaça(s)"
            // nunca apareceu, mesmo com cinco ameaças detectadas e com o
            // ViewModel claramente vivo (as linhas de rede e dispositivo
            // apareciam normalmente).
            //
            // A sincronização pertence ao momento em que a aba é EXIBIDA, e
            // não ao momento em que o objeto é construído. É a diferença entre
            // "ter os dados" e "mostrar os dados", e só a segunda interessa ao
            // usuário.
            Loaded += (s, e) =>
            {
                App.LoggingService?.LogTrace("[UI] Navegou para ShieldView");
                ApplyResponsiveColumns(ActualWidth);

                (DataContext as VoltrisOptimizer.UI.ViewModels.ShieldViewModel)
                    ?.SyncFromServiceOnDisplay();
            };

            // A assinatura original de `ApplyResponsiveColumns` é mantida: ela
            // recebe a LARGURA, e é o que o chamador já passava. `NewWidth` não
            // existe em `SizeChangedEventArgs` — a propriedade é `NewSize.Width`.
            SizeChanged += (s, e) => ApplyResponsiveColumns(e.NewSize.Width);
        }

        /// <summary>
        /// Abre o mesmo modal de compra usado pelos demais recursos PRO
        /// (Reparo Inteligente, Perfil Inteligente, Gamer Mode).
        /// </summary>
        private void OpenLicenseModal_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = App.Services?.GetService(typeof(ILicenseDialogService)) as ILicenseDialogService;
                var gate = App.Services?.GetService(typeof(VoltrisOptimizer.Services.Shield.ShieldLicenseGate)) as VoltrisOptimizer.Services.Shield.ShieldLicenseGate;

                dialog?.ShowLicenseBlocked(
                    new LicenseState
                    {
                        LicenseType = gate?.CurrentLicenseType ?? "None",
                        IsActive = false
                    },
                    VoltrisOptimizer.Services.Shield.ShieldLicenseGate.FeatureId);
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError("[ShieldView] Erro ao abrir modal de licença", ex);
            }
        }

        private void ApplyResponsiveColumns(double width)
        {
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            {
                return;
            }

            double available = Math.Max(0d, width - PageGutter);

            // A lista de módulos usa WrapPanel: cada item ajusta o próprio tamanho ao texto,
            // portanto o número de colunas acompanha a largura disponível sem cortar texto.
            // Não é necessário calcular colunas aqui.

            // Cartões de scan da aba Scans: 4 → 2 colunas.
            if (ScanCardsGrid != null)
            {
                ScanCardsGrid.Columns = available >= ThreeColumnBreakpoint ? 4 : 2;
            }
        }
    }
}
