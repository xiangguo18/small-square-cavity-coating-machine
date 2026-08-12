using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Services.Security;
using System.Windows.Threading;

namespace Small_square_cavity_coating_machine.ViewModels.Security;

public sealed partial class AccountShellViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly IAccountDialogService _dialogService;
    private readonly IAuthorizationService _authorization;
    private readonly DispatcherTimer _noticeTimer;

    public AccountShellViewModel(
        IAuthenticationService authentication,
        IAccountDialogService dialogService,
        IAuthorizationService authorization)
    {
        _authentication = authentication;
        _dialogService = dialogService;
        _authorization = authorization;
        _authentication.Session.CurrentUserChanged += Session_CurrentUserChanged;
        _authorization.AccessDenied += Authorization_AccessDenied;
        _noticeTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _noticeTimer.Tick += NoticeTimer_Tick;
    }

    public bool IsLoggedIn => _authentication.Session.IsAuthenticated;

    public bool IsGuest => !IsLoggedIn;

    public string GuestStatusText => "当前用户：未登录｜仅可查看";

    public string CurrentUserName =>
        _authentication.Session.CurrentUser?.UserName ?? "未登录";

    public byte[]? AvatarData => _authentication.Session.CurrentUser?.AvatarData;

    [ObservableProperty]
    private string authorizationNoticeText = string.Empty;

    [ObservableProperty]
    private bool isAuthorizationNoticeVisible;

    [RelayCommand]
    private void OpenAccount()
    {
        if (IsLoggedIn)
        {
            _dialogService.ShowProfile();
        }
        else
        {
            _dialogService.ShowLogin();
        }
    }

    [RelayCommand]
    private void Logout()
    {
        if (!IsLoggedIn)
        {
            AuthorizationNoticeText = "当前尚未登录，无需退出";
            IsAuthorizationNoticeVisible = true;
            _noticeTimer.Stop();
            _noticeTimer.Start();
            return;
        }

        if (_dialogService.ConfirmLogout(CurrentUserName))
        {
            _authentication.Logout();
        }
    }

    private void Session_CurrentUserChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(IsGuest));
        OnPropertyChanged(nameof(CurrentUserName));
        OnPropertyChanged(nameof(AvatarData));
    }

    private void Authorization_AccessDenied(object? sender, AuthorizationDeniedEventArgs e)
    {
        AuthorizationNoticeText = e.Message;
        IsAuthorizationNoticeVisible = true;
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void NoticeTimer_Tick(object? sender, EventArgs e)
    {
        _noticeTimer.Stop();
        IsAuthorizationNoticeVisible = false;
    }
}
