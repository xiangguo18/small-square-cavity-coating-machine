using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.ViewModels.Recipes;
using System.Windows.Controls;

namespace Small_square_cavity_coating_machine.Views;

public partial class Process : Page
{
    private readonly ProcessViewModel _viewModel;

    public Process(ProcessViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void RecipeGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid)
        {
            _viewModel.SetSelectedLayers(grid.SelectedItems.Cast<RecipeLayer>());
        }
    }
}
