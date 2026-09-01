using System.Windows.Controls;
using Small_square_cavity_coating_machine.ViewModels.Equipment;
namespace Small_square_cavity_coating_machine.Views;
public partial class Parameter : Page
{
    public Parameter() { InitializeComponent(); }
    public Parameter(ParameterSettingsViewModel viewModel) : this() { DataContext = viewModel; }
}
