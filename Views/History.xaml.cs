using System.Windows.Controls;
using Small_square_cavity_coating_machine.ViewModels.History;

namespace Small_square_cavity_coating_machine.Views;

/// <summary>
/// History.xaml 的交互逻辑。
/// </summary>
public partial class History : Page
{
    public History()
    {
        InitializeComponent();
    }

    public History(HistoryViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
    }

    private void TrendChartView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {

    }
}
