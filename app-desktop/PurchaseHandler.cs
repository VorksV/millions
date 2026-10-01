using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using VoltrisOptimizer.Services;
public partial class PurchaseHandler
{
    private async void OnPurchaseButtonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                // URL da API com parâmetros de plano
                var apiUrlBuilder = new StringBuilder("https://voltris.com.br/api/pagamento");
                apiUrlBuilder.Append("?plan=pro");

                // Pode será ajustado para diferentes planos
                string apiUrl = apiUrlBuilder.ToString();

                HttpResponseMessage response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var jsonDocument = JsonDocument.Parse(jsonResponse);

                    if (jsonDocument.RootElement.TryGetProperty("init_point", out var urlElement))
                    {
                        string paymentUrl = urlElement.GetString();

                        if (!string.IsNullOrEmpty(paymentUrl))
                        {
                            // Open URL in default browserá
                            ProcessStartInfo psi = new ProcessStartInfo
                            {
                                FileName = paymentUrl,
                                UseShellExecute = true
                            };

                            Process.Start(psi);
                        }
                        else
                        {
                            MessageBox.Show(LocalizationService.Instance.GetString("PurchaseInvalidPaymentUrl"), LocalizationService.Instance.GetString("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else if (jsonDocument.RootElement.TryGetProperty("error", out var errorElement))
                    {
                        string errorMsg = errorElement.GetString() ?? "Erro desconhecido";
                        MessageBox.Show($"Erro na API: {errorMsg}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        MessageBox.Show("Resposta da API em formato inesperado.", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    MessageBox.Show($"Falha na requisição: {response.StatusCode}\n{errorContent}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao processar pagamento: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
