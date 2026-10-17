using System.Windows;
using AsicFarm.Client.ViewModels;

namespace AsicFarm.Client;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        viewModel.Schedules.SetMinerProvider(() => viewModel.Miners.ToList());
    }
}