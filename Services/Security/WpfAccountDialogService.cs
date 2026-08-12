using Small_square_cavity_coating_machine.Views;
using System.Windows;

namespace Small_square_cavity_coating_machine.Services.Security;

public sealed class WpfAccountDialogService : IAccountDialogService
{
    private readonly IAuthenticationService _authentication;
    private readonly IAvatarImageService _avatarImageService;

    public WpfAccountDialogService(
        IAuthenticationService authentication,
        IAvatarImageService avatarImageService)
    {
        _authentication = authentication;
        _avatarImageService = avatarImageService;
    }

    public void ShowLogin()
    {
        var window = new LoginWindow(_authentication)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public void ShowProfile()
    {
        if (!_authentication.Session.IsAuthenticated)
        {
            return;
        }

        var window = new UserProfileWindow(_authentication, _avatarImageService)
        {
            Owner = Application.Current.MainWindow
        };
        window.ShowDialog();
    }

    public bool ConfirmLogout(string userName) => MessageBox.Show(
        $"确定要退出用户“{userName}”吗？退出后所有控制操作将立即禁用。",
        "退出登录",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question,
        MessageBoxResult.No) == MessageBoxResult.Yes;
}
