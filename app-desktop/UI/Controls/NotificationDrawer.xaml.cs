using System;

using System.ComponentModel;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using VoltrisOptimizer.UI.ViewModels;

namespace VoltrisOptimizer.UI.Controls 
{
 public partial class NotificationDrawer : UserControl 
{
    public NotificationDrawer()
    {
        App.LoggingService?.LogInfo("[DRAWER - DEBUG] NotificationDrawer.CONSTRUTOR InitializeComponent() chamado");
        InitializeComponent();
        App.LoggingService?.LogInfo("[DRAWER - DEBUG] NotificationDrawer.CONSTRUTOR InitializeComponent() OK");
        App.LoggingService?.LogInfo($"[DRAWER - DEBUG] DrawerRoot is null? {DrawerRoot == null}");
        App.LoggingService?.LogInfo($"[DRAWER - DEBUG] DrawerTransform is null? {DrawerTransform == null}");
        DataContextChanged += OnDataContextChanged;
        App.LoggingService?.LogInfo("[DRAWER - DEBUG] NotificationDrawer.CONSTRUTOR DataContextChanged registrado");
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        App.LoggingService?.LogInfo($"[DRAWER - DEBUG] OnDataContextChanged OldValue = {e.OldValue?.GetType().Name}, NewValue = {e.NewValue?.GetType().Name}");

        if (e.OldValue is INotifyPropertyChanged oldVm)
        {
            oldVm.PropertyChanged -= ViewModel_PropertyChanged;
            App.LoggingService?.LogInfo("[DRAWER - DEBUG] PropertyChanged REMOVIDO do VM antigo");
        }

        if (e.NewValue is INotifyPropertyChanged newVm)
        {
            newVm.PropertyChanged += ViewModel_PropertyChanged;
            App.LoggingService?.LogInfo("[DRAWER - DEBUG] PropertyChanged ADICIONADO ao VM novo");
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        App.LoggingService?.LogInfo($"[DRAWER - DEBUG] ViewModel_PropertyChanged PropertyName = {e.PropertyName}");

        if (e.PropertyName == nameof(NotificationDrawerViewModel.IsOpen))
        {
            try
            {
                if (DataContext is NotificationDrawerViewModel vm)
                {
                    App.LoggingService?.LogInfo($"[DRAWER - DEBUG] vm.IsOpen = {vm.IsOpen}. DataContext type = {DataContext.GetType().Name}");

                    if (vm.IsOpen)
                        Open();
                    else
                        Close();
                }
                else
                {
                    App.LoggingService?.LogError("[DRAWER - DEBUG] DataContext NÃO NotificationDrawerViewModel!");
                }
            }
            catch (Exception ex)
            {
                App.LoggingService?.LogError($"[DRAWER - DEBUG] EXCEÇÃO em ViewModel_PropertyChanged: {ex.Message}", ex);
            }
        }
    }

    public void Open()
    {
        App.LoggingService?.LogInfo("[DRAWER - DEBUG] NotificationDrawer.Open() INÍCIO");

        try
        {
            App.LoggingService?.LogInfo($"[DRAWER - DEBUG] DrawerRoot is null? {DrawerRoot == null}");
            App.LoggingService?.LogInfo($"[DRAWER - DEBUG] DrawerTransform is null? {DrawerTransform == null}");
            App.LoggingService?.LogInfo($"[DRAWER - DEBUG] DataContext is null? {DataContext == null}");

            if (DataContext is NotificationDrawerViewModel vm)
            {
                App.LoggingService?.LogInfo("[DRAWER - DEBUG] DataContext NotificationDrawerViewModel. Chamando vm.SyncNotifications()...");
                vm.SyncNotifications();
                App.LoggingService?.LogInfo("[DRAWER - DEBUG] vm.SyncNotifications() retornou");
            }
            else
            {
                App.LoggingService?.LogError($"[DRAWER - DEBUG] DataContext NÃO NotificationDrawerViewModel! Type = {DataContext?.GetType().FullName}");
            }

            if (DrawerRoot == null)
            {
                App.LoggingService?.LogError("[DRAWER - DEBUG] DrawerRoot NULL! Abortando Open().");
                return;
            }

            DrawerRoot.Visibility = Visibility.Visible;
            App.LoggingService?.LogInfo("[DRAWER - DEBUG] DrawerRoot.Visibility = Visible");

            DrawerRoot.Opacity = 1;
            App.LoggingService?.LogInfo("[DRAWER - DEBUG] DrawerRoot.Opacity = 1");

            if (DrawerTransform != null)
            {
                DrawerTransform.X = 0;
                App.LoggingService?.LogInfo("[DRAWER - DEBUG] DrawerTransform.X = 0");
            }
            else
            {
                App.LoggingService?.LogError("[DRAWER - DEBUG] DrawerTransform NULL!");
            }

            App.LoggingService?.LogInfo("[DRAWER - DEBUG] NotificationDrawer.Open() FIM - Drawer deveria estar visível");
        }
        catch (Exception ex)
        {
            App.LoggingService?.LogError($"[DRAWER - DEBUG] EXCEÇÃO em NotificationDrawer.Open(): {ex.Message}", ex);
        }
    }

    public void Close()
    {
        App.LoggingService?.LogInfo("[DRAWER - DEBUG] NotificationDrawer.Close() INÍCIO");

        try
        {
            if (DrawerRoot == null)
            {
                App.LoggingService?.LogError("[DRAWER - DEBUG] DrawerRoot NULL em Close()!");
                return;
            }

            DrawerRoot.Visibility = Visibility.Collapsed;
            App.LoggingService?.LogInfo("[DRAWER - DEBUG] DrawerRoot.Visibility = Collapsed");

            DrawerRoot.Opacity = 0;

            if (DrawerTransform != null)
                DrawerTransform.X = 400;

            App.LoggingService?.LogInfo("[DRAWER - DEBUG] NotificationDrawer.Close() FIM");
        }
        catch (Exception ex)
        {
            App.LoggingService?.LogError($"[DRAWER - DEBUG] EXCEÇÃO em NotificationDrawer.Close(): {ex.Message}", ex);
        }
    }

    public bool IsOpen
    {
        get
        {
            var open = DrawerRoot?.Visibility == Visibility.Visible;
            App.LoggingService?.LogInfo($"[DRAWER - DEBUG] IsOpen getter = {open} (DrawerRoot.Visibility = {DrawerRoot?.Visibility})");
            return open;
        }
    }

    private void NotificationItem_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (sender is Border border && border.DataContext is NotificationItemViewModel item)
            {
                item.MarkAsReadCommand?.Execute(null);
            }
        }
        catch (Exception ex)
        {
            App.LoggingService?.LogError($"[NotificationDrawer] Erro no Click: {ex.Message}", ex);
        }
    }
 }
 }

