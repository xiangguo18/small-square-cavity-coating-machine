using System.Windows.Controls;
using Small_square_cavity_coating_machine.ViewModels.Equipment;
namespace Small_square_cavity_coating_machine.Views;
public partial class IO : Page
{
    public IO() { InitializeComponent(); }
    public IO(IoStatusViewModel viewModel) : this() { DataContext = viewModel; }
}
