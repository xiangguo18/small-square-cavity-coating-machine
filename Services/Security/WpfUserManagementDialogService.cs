using System.Windows;
using Small_square_cavity_coating_machine.Views;

namespace Small_square_cavity_coating_machine.Services.Security;

public sealed class WpfUserManagementDialogService : IUserManagementDialogService
{
    public bool ConfirmDelete(string userName) => MessageBox.Show(
        $"确定要删除用户“{userName}”吗？此操作无法撤销。",
        "删除用户",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning,
        MessageBoxResult.No) == MessageBoxResult.Yes;

    public PendingPasswordDecision ConfirmPendingPassword(string userName, bool isCreating)
    {
        var dialog = new PendingPasswordDialog(userName, isCreating)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
        return dialog.Decision;
    }
}
