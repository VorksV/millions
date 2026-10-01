using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using VoltrisOptimizer.Services;

namespace VoltrisOptimizer.Examples
{
    /// <summary>
    /// EXEMPLO DE USO DO GLOBALOPERATIONMANAGER
    /// 
    /// Este arquivo demonstra como integrar qualquer comando/operação
    /// com o GlobalOperationManager para prevenir concorrência.
    /// 
    /// APLICAR ESTE PADRÃO EM:
    /// - PreparePcCommand
    /// - CleanupCommand
    /// - OptimizePerformanceCommand
    /// - OptimizeNetworkCommand
    /// - Qualquer operação que use a Global Progress Bar
    /// </summary>
    public class GlobalOperationManagerUsageExample
    {
        #region Exemplo 1: Command com verificação simples

        public class SimpleCommand : ICommand
        {
            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter)
            {
                // Verificar se pode executar
                return !GlobalOperationManager.Instance.IsOperationRunning;
            }

            public async void Execute(object? parameter)
            {
                // Verificar novamente antes de executar
                if (!GlobalOperationManager.Instance.CanStartOperation("Minha Operação"))
                {
                    // CanStartOperation já exibe a mensagem ao usuário
                    return;
                }

                // Executar operação...
                await ExecuteOperationAsync();
            }

            private async Task ExecuteOperationAsync()
            {
                // Sua lógica aqui
                await Task.Delay(1000);
            }
        }

        #endregion

        #region Exemplo 2: Command com GlobalOperationManager completo

        public class AdvancedCommand : ICommand
        {
            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter)
            {
                return !GlobalOperationManager.Instance.IsOperationRunning;
            }

            public async void Execute(object? parameter)
            {
                await ExecuteWithGlobalOperationAsync();
            }

            private async Task ExecuteWithGlobalOperationAsync()
            {
                var success = await GlobalOperationManager.Instance.TryStartOperationAsync(
                    GlobalOperationState.PreparingPc,
                    "Preparar PC para Gaming",
                    async (progress, cancellationToken) =>
                    {
                        // ETAPA 1
                        progress.Report(new OperationProgress
                        {
                            Percentage = 0,
                            Message = "Iniciando preparação..."
                        });
                        await Task.Delay(1000, cancellationToken);

                        // ETAPA 2
                        progress.Report(new OperationProgress
                        {
                            Percentage = 25,
                            Message = "Otimizando configurações..."
                        });
                        await Task.Delay(2000, cancellationToken);

                        // ETAPA 3
                        progress.Report(new OperationProgress
                        {
                            Percentage = 50,
                            Message = "Aplicando tweaks..."
                        });
                        await Task.Delay(2000, cancellationToken);

                        // ETAPA 4
                        progress.Report(new OperationProgress
                        {
                            Percentage = 75,
                            Message = "Finalizando..."
                        });
                        await Task.Delay(1000, cancellationToken);

                        // ETAPA 5
                        progress.Report(new OperationProgress
                        {
                            Percentage = 100,
                            Message = "Concluído!"
                        });
                    }
                );

                if (success)
                {
                    // Operação concluída com sucesso
                    NotificationManager.ShowSuccess("Sucesso", "Operação concluída!");
                }
                else
                {
                    // Operação foi bloqueada ou cancelada
                    // Mensagem já foi exibida pelo GlobalOperationManager
                }
            }
        }

        #endregion

        #region Exemplo 3: Integração com PreparePcCommand existente

        /// <summary>
        /// EXEMPLO DE COMO MODIFICAR O PREPAREPCCOMMAND EXISTENTE
        /// </summary>
        public class PreparePcCommandExample
        {
            // ANTES (código antigo):
            /*
            private async void ExecutePreparePc()
            {
                GlobalProgressService.Instance.UpdateProgress(0, "Iniciando...");
                
                // ... lógica de preparação ...
                
                GlobalProgressService.Instance.UpdateProgress(100, "Concluído!");
                GlobalProgressService.Instance.HideProgress();
            }
            */

            // DEPOIS (código novo com GlobalOperationManager):
            private async void ExecutePreparePc()
            {
                // Verificar se pode executar
                if (!GlobalOperationManager.Instance.CanStartOperation("Preparar PC"))
                {
                    return; // Mensagem já foi exibida
                }

                // Executar com controle de concorrência
                var success = await GlobalOperationManager.Instance.TryStartOperationAsync(
                    GlobalOperationState.PreparingPc,
                    "Preparar PC para Gaming",
                    async (progress, cancellationToken) =>
                    {
                        // Sua lógica existente aqui
                        // Substituir GlobalProgressService.Instance.UpdateProgress por:
                        progress.Report(new OperationProgress
                        {
                            Percentage = 50,
                            Message = "Processando..."
                        });

                        // ... resto da lógica ...
                    }
                );

                if (success)
                {
                    NotificationManager.ShowSuccess("Sucesso", "PC preparado com sucesso!");
                }
            }
        }

        #endregion

        #region Exemplo 4: Binding no XAML

        /*
        EXEMPLO DE BINDING NO XAML:
        
        <Button Command="{Binding PreparePcCommand}"
                IsEnabled="{Binding Source={x:Static services:GlobalOperationManager.Instance}, 
                                    Path=IsOperationRunning, 
                                    Converter={StaticResource InverseBooleanConverter}}">
            <TextBlock Text="Preparar PC" />
        </Button>
        
        OU
        
        <ProgressBar Value="{Binding Source={x:Static services:GlobalOperationManager.Instance}, 
                                     Path=GlobalProgress, 
                                     Mode=OneWay}"
                     Visibility="{Binding Source={x:Static services:GlobalOperationManager.Instance}, 
                                          Path=IsOperationRunning, 
                                          Converter={StaticResource BoolToVisibilityConverter}}" />
        
        <TextBlock Text="{Binding Source={x:Static services:GlobalOperationManager.Instance}, 
                                  Path=GlobalStatusMessage, 
                                  Mode=OneWay}" />
        */

        #endregion

        #region Exemplo 5: Verificação em ViewModel

        public class ExampleViewModel
        {
            private readonly GlobalOperationManager _operationManager;

            public ExampleViewModel()
            {
                _operationManager = GlobalOperationManager.Instance;

                // Assinar evento de mudança de propriedade
                _operationManager.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(GlobalOperationManager.IsOperationRunning))
                    {
                        // Atualizar CanExecute dos comandos
                        OnCanExecuteChanged();
                    }
                };
            }

            public bool CanExecuteMyCommand()
            {
                return !_operationManager.IsOperationRunning;
            }

            private void OnCanExecuteChanged()
            {
                // Notificar que CanExecute mudou
                // CommandManager.InvalidateRequerySuggested();
            }
        }

        #endregion
    }
}
