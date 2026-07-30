using Small_square_cavity_coating_machine.ViewModels;
using System.Windows.Controls;

namespace Small_square_cavity_coating_machine.Views
{
    /// <summary>
    /// Controlview.xaml 的交互逻辑
    /// </summary>
    public partial class Controlview : Page
    {
        public Controlview()
        {
            InitializeComponent();
        }

        public Controlview(ControlViewModel viewModel)
            : this()
        {
            DataContext = viewModel;
        }

    }
}
