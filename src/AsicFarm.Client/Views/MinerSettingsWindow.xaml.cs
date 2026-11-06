using System.Windows;
using AsicFarm.Client.ViewModels;

namespace AsicFarm.Client.Views;

public partial class MinerSettingsWindow : Window
{
    public MinerSettingsWindow(MinerSettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}