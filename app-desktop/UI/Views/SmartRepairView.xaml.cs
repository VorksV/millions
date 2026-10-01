using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Views
{
    public partial class SmartRepairView : UserControl
    {
        public SmartRepairView()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[SmartRepairView] InitializeComponent falhou: {ex.Message}");
                return;
            }

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SmartRepairViewModel? vm = null;

            if (DataContext == null)
            {
                try
                {
                    var locator = Application.Current?.FindResource("Locator") as ViewModelLocator;
                    if (locator?.SmartRepairVM is SmartRepairViewModel svm)
                        vm = svm;
                    else
                        return;
                }
                catch
                {
                    return;
                }
            }
            else if (DataContext is SmartRepairViewModel existingVm)
            {
                vm = existingVm;
            }

            if (vm == null) return;

            // Reseta ANTES de vincular o DataContext para evitar flash do overlay (apenas se não estiver rodando)
            if (vm.IsIdle && (vm.IsCompleted || vm.IsCancelled || vm.IsSimulated))
            {
                vm.Reset();
            }

            if (DataContext == null)
            {
                DataContext = vm;
            }

            vm.LogEntries.CollectionChanged -= LogEntries_CollectionChanged;
            vm.LogEntries.CollectionChanged += LogEntries_CollectionChanged;

            vm.PropertyChanged -= Vm_PropertyChanged;
            vm.PropertyChanged += Vm_PropertyChanged;

            // Inicia o scan após a UI ter chance de renderizar
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (vm.CanStartScan)
                {
                    vm.StartScanCommand.Execute(null);
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SmartRepairViewModel.CurrentStepName) || 
                e.PropertyName == nameof(SmartRepairViewModel.CompletedSteps))
            {
                var vm = sender as SmartRepairViewModel;
                if (vm == null || StepsListView.Items.Count == 0) return;
                
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    object? targetItem = null;
                    foreach (var item in vm.Modules)
                    {
                        if (item.Status == VoltrisOptimizer.Services.SmartRepair.StepStatus.Running)
                        {
                            targetItem = item;
                            break;
                        }
                    }

                    if (targetItem != null)
                    {
                        StepsListView.UpdateLayout(); StepsListView.ScrollIntoView(targetItem);
                    }
                    else if (vm.IsCompleted && StepsListView.Items.Count > 0)
                    {
                        StepsListView.ScrollIntoView(StepsListView.Items[StepsListView.Items.Count - 1]);
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void LogEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Reset)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (LogListView.Items.Count > 0)
                    {
                        var lastItem = LogListView.Items[LogListView.Items.Count - 1];
                        LogListView.ScrollIntoView(lastItem);

                        var scrollViewer = GetScrollViewer(LogListView);
                        LogListView.UpdateLayout(); if (scrollViewer != null)
                        {
                            scrollViewer.ScrollToBottom();
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private ScrollViewer? GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer viewer) return viewer;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
