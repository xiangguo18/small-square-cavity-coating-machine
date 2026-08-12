using Small_square_cavity_coating_machine.Services.Security;
using System.Windows;

namespace Small_square_cavity_coating_machine.Views;

public partial class PendingPasswordDialog : Window
{
    public PendingPasswordDialog(string userName, bool isCreating)
    {
        InitializeComponent();
        MessageText.Text = isCreating
            ? "新用户的用户名和密码尚未保存，是否立即创建该用户？"
            : $"用户“{userName}”的新密码尚未保存，是否立即修改密码？";
    }

    public PendingPasswordDecision Decision { get; private set; } = PendingPasswordDecision.Cancel;

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        Decision = PendingPasswordDecision.Save;
        DialogResult = true;
    }

    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        Decision = PendingPasswordDecision.Discard;
        DialogResult = false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Decision = PendingPasswordDecision.Cancel;
        DialogResult = null;
    }
}
