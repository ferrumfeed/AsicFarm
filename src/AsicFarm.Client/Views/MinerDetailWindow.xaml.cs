using System.Windows;
using AsicFarm.Client.ViewModels;

namespace AsicFarm.Client.Views;

public partial class MinerDetailWindow : Window
{
    public MinerDetailViewModel ViewModel { get; }

    public MinerDetailWindow(MinerDetailViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}