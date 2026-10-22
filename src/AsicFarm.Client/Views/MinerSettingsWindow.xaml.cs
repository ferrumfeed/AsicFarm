using System.Windows;
using AsicFarm.Client.ViewModels;

namespace AsicFarm.Client.Views;

public partial class MinerSettingsWindow : Window
{
    public MinerSettingsWindow(MinerSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}