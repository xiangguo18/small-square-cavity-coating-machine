using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.ViewModels.Recipes;
using System.Windows;

namespace Small_square_cavity_coating_machine.Views;

public partial class NewRecipeLayerWindow : Window
{
    private readonly NewRecipeLayerViewModel _viewModel;

    public NewRecipeLayerWindow(NewRecipeLayerViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public RecipeLayer? CreatedLayer { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.TryBuildLayer(out var layer, out var error))
        {
            MessageBox.Show(
                this,
                error,
                "参数校验",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        CreatedLayer = layer;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
