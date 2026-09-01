using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.Services.Security;
using System.Collections.ObjectModel;

namespace Small_square_cavity_coating_machine.ViewModels.Security;

public sealed partial class UserManagementViewModel : ObservableObject, IDisposable
{
    private readonly IUserRepository _repository;
    private readonly IUserSession _session;
    private readonly IAuthorizationService _authorization;
    private readonly IAuthenticationService _authentication;
    private readonly IUserManagementDialogService _dialogService;
    private readonly IEquipmentControlService? _controlService;
    private readonly IUiDispatcher? _dispatcher;

    public UserManagementViewModel(
        IUserRepository repository,
        IUserSession session,
        IAuthorizationService authorization,
        IAuthenticationService authentication,
        IUserManagementDialogService dialogService,
        IEquipmentControlService? controlService = null,
        IUiDispatcher? dispatcher = null)
    {
        _repository = repository;
        _session = session;
        _authorization = authorization;
        _authentication = authentication;
        _dialogService = dialogService;
        _controlService = controlService;
        _dispatcher = dispatcher;
        _authorization.AccessChanged += Authorization_AccessChanged;
        if (_controlService is not null) _controlService.Changed += ControlServiceChanged;

        foreach (var permission in PermissionCatalog.All)
        {
            PermissionOptions.Add(new PermissionOptionViewModel(permission));
        }

        ReloadUsers();
        SelectedUser = Users.FirstOrDefault();
        RefreshInterlockState();
    }

    public ObservableCollection<UserAccount> Users { get; } = [];

    public ObservableCollection<PermissionOptionViewModel> PermissionOptions { get; } = [];

    public bool CanManageUsers => _authorization.CanOperate(PermissionKey.UserManagement);

    public bool CanToggleMaintenanceInterlock => _session.CurrentUser?.IsBuiltInAdministrator == true;

    public string MaintenanceInterlockButtonText => IsMaintenanceInterlockBypassed ? "互锁恢复" : "互锁解除";

    public bool IsGuest => !_session.IsAuthenticated;

    public bool IsCurrentUserSelected =>
        _session.CurrentUser is not null
        && SelectedUser?.Id == _session.CurrentUser.Id;

    public bool CanEditUserName => CanManageUsers && IsCreating;

    public bool CanEditPassword => CanManageUsers || IsCurrentUserSelected;

    public bool CanEditPermissions => CanManageUsers && !IsBuiltInAdministrator;

    public bool IsUserNameReadOnly => !CanEditUserName;

    public bool HasPendingPassword => !string.IsNullOrEmpty(PendingPassword);

    public string PasswordHintText => CanManageUsers
        ? "新建时必填；编辑时留空表示不修改"
        : IsCurrentUserSelected
            ? "输入新密码后单击保存；留空不修改"
            : "只能修改当前登录账号的密码";

    public bool CanDelete =>
        CanManageUsers
        && SelectedUser is not null
        && !SelectedUser.IsBuiltInAdministrator
        && !IsCurrentUserSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyPropertyChangedFor(nameof(IsCurrentUserSelected))]
    [NotifyPropertyChangedFor(nameof(CanEditPassword))]
    [NotifyPropertyChangedFor(nameof(PasswordHintText))]
    private UserAccount? selectedUser;

    [ObservableProperty]
    private string userName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingPassword))]
    private string pendingPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditUserName))]
    [NotifyPropertyChangedFor(nameof(CanEditPermissions))]
    [NotifyPropertyChangedFor(nameof(IsUserNameReadOnly))]
    private bool isBuiltInAdministrator;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditUserName))]
    [NotifyPropertyChangedFor(nameof(IsUserNameReadOnly))]
    private bool isCreating;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isStatusError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaintenanceInterlockButtonText))]
    private bool isMaintenanceInterlockBypassed;

    partial void OnSelectedUserChanged(UserAccount? value)
    {
        if (value is null)
        {
            DeleteCommand.NotifyCanExecuteChanged();
            return;
        }

        IsCreating = false;
        UserName = value.UserName;
        PendingPassword = string.Empty;
        IsBuiltInAdministrator = value.IsBuiltInAdministrator;
        foreach (var option in PermissionOptions)
        {
            option.IsAllowed = value.HasPermission(option.Key);
        }

        SetStatus(GetSelectionStatus(value), false);
        DeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void NewUser()
    {
        if (!TryResolvePendingPasswordChange())
        {
            return;
        }

        if (!EnsureCanManageUsers())
        {
            SetStatus("当前用户仅可查看，无操作权限。", true);
            return;
        }

        SelectedUser = null;
        IsCreating = true;
        UserName = string.Empty;
        PendingPassword = string.Empty;
        IsBuiltInAdministrator = false;
        foreach (var option in PermissionOptions)
        {
            option.IsAllowed = false;
        }

        SetStatus("请输入用户名和密码，并选择需要开通的操作权限。", false);
        DeleteCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Save() => SaveCore();

    private bool SaveCore()
    {
        if (!CanManageUsers)
        {
            if (IsCurrentUserSelected)
            {
                return SaveCurrentUserPassword();
            }
            else
            {
                EnsureCanManageUsers();
                SetStatus(
                    IsGuest
                        ? "请先登录后再修改密码。"
                        : "只能修改当前登录账号的密码。",
                    true);
            }

            return false;
        }

        try
        {
            var permissions = PermissionOptions
                .Where(option => option.IsAllowed)
                .Select(option => option.Key)
                .ToArray();

            UserAccount saved;
            if (IsCreating)
            {
                saved = _repository.Create(UserName, PendingPassword, permissions);
            }
            else if (SelectedUser is not null)
            {
                saved = _repository.Update(
                    SelectedUser.Id,
                    UserName,
                    string.IsNullOrEmpty(PendingPassword) ? null : PendingPassword,
                    permissions);
            }
            else
            {
                SetStatus("请先选择用户，或单击“新建”。", true);
                return false;
            }

            if (saved.Id == _session.CurrentUser?.Id)
            {
                _session.RefreshCurrentUser();
            }

            ReloadUsers(saved.Id);
            PendingPassword = string.Empty;
            SetStatus("用户信息已保存。", false);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            SetStatus(exception.Message, true);
            return false;
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (!TryResolvePendingPasswordChange())
        {
            return;
        }

        if (!EnsureCanManageUsers())
        {
            SetStatus("当前用户仅可查看，无操作权限。", true);
            return;
        }

        if (!CanDelete || SelectedUser is null)
        {
            SetStatus(
                IsCurrentUserSelected
                    ? "不能删除当前正在登录的账号。"
                    : "当前用户不能删除该账号。",
                true);
            return;
        }

        var deleteIndex = Users.IndexOf(SelectedUser);
        if (!_dialogService.ConfirmDelete(SelectedUser.UserName))
        {
            return;
        }

        try
        {
            _repository.Delete(SelectedUser.Id);
            ReloadUsers();
            SelectedUser = Users.Count == 0
                ? null
                : Users[Math.Min(deleteIndex, Users.Count - 1)];
            SetStatus("用户已删除。", false);
        }
        catch (InvalidOperationException exception)
        {
            SetStatus(exception.Message, true);
        }
    }

    private void ReloadUsers(long? selectedId = null)
    {
        var targetId = selectedId ?? SelectedUser?.Id;
        Users.Clear();
        foreach (var user in _repository.GetAll())
        {
            Users.Add(user);
        }

        if (targetId is not null)
        {
            SelectedUser = Users.FirstOrDefault(user => user.Id == targetId);
        }
    }

    private void Authorization_AccessChanged(object? sender, EventArgs e)
    {
        ReloadUsers(SelectedUser?.Id);
        OnPropertyChanged(nameof(CanManageUsers));
        OnPropertyChanged(nameof(IsGuest));
        OnPropertyChanged(nameof(IsCurrentUserSelected));
        OnPropertyChanged(nameof(CanEditUserName));
        OnPropertyChanged(nameof(CanEditPassword));
        OnPropertyChanged(nameof(CanEditPermissions));
        OnPropertyChanged(nameof(IsUserNameReadOnly));
        OnPropertyChanged(nameof(PasswordHintText));
        OnPropertyChanged(nameof(CanDelete));
        DeleteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanToggleMaintenanceInterlock));
        ToggleMaintenanceInterlockCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanToggleMaintenanceInterlock))]
    private async Task ToggleMaintenanceInterlockAsync()
    {
        if (_controlService is null)
        {
            SetStatus("互锁控制服务未加载。", true);
            return;
        }
        var target = !IsMaintenanceInterlockBypassed;
        var result = await _controlService.SetPassInterlockAsync(target).ConfigureAwait(false);
        PostToUi(() => SetStatus(result.Message, result.Outcome != ControlWriteOutcome.Confirmed));
    }

    private void ControlServiceChanged(object? sender, EventArgs e)
    {
        if (_dispatcher is null) RefreshInterlockState();
        else _dispatcher.Post(RefreshInterlockState);
    }

    private void RefreshInterlockState()
    {
        var point = _controlService?.Point(EquipmentGroups.PassInterlock);
        IsMaintenanceInterlockBypassed = point is { Quality: AlarmQuality.Good, Value: true };
        ApplicationStatusViewModel.Instance.MaintenanceBypassActive = IsMaintenanceInterlockBypassed;
    }

    public void NotifyRestrictedAccountEditorClicked()
    {
        if (!CanManageUsers)
        {
            _authorization.TryAuthorize(PermissionKey.UserManagement);
            SetStatus(
                IsGuest
                    ? "未登录状态只能查看用户信息。"
                    : "当前账号只能修改自己的密码。",
                true);
        }
    }

    public void NotifyRestrictedPermissionEditorClicked()
    {
        if (!CanManageUsers)
        {
            _authorization.TryAuthorize(PermissionKey.UserManagement);
            SetStatus("只有拥有用户权限的账号才能修改权限。", true);
        }
    }

    public bool TryResolvePendingPasswordChange()
    {
        if (!HasPendingPassword || !_session.IsAuthenticated)
        {
            return true;
        }

        var decision = _dialogService.ConfirmPendingPassword(UserName, IsCreating);
        if (decision == PendingPasswordDecision.Cancel)
        {
            return false;
        }

        if (decision == PendingPasswordDecision.Discard)
        {
            PendingPassword = string.Empty;
            return true;
        }

        return SaveCore();
    }

    public bool TrySelectUser(UserAccount user)
    {
        if (SelectedUser?.Id == user.Id)
        {
            return true;
        }

        if (!TryResolvePendingPasswordChange())
        {
            return false;
        }

        SelectedUser = user;
        return true;
    }

    private bool SaveCurrentUserPassword()
    {
        if (string.IsNullOrEmpty(PendingPassword))
        {
            SetStatus("请输入新密码。", true);
            return false;
        }

        var result = _authentication.SetCurrentPassword(PendingPassword);
        if (!result.IsSuccessful)
        {
            SetStatus(result.ErrorMessage, true);
            return false;
        }

        var currentUserId = _session.CurrentUser!.Id;
        ReloadUsers(currentUserId);
        PendingPassword = string.Empty;
        SetStatus("当前账号密码已修改。", false);
        return true;
    }

    private string GetSelectionStatus(UserAccount selectedUser)
    {
        if (IsGuest)
        {
            return "未登录状态仅可查看用户信息。";
        }

        if (CanManageUsers)
        {
            return selectedUser.IsBuiltInAdministrator
                ? "内置管理员账号不可删除，用户名和权限固定；可以修改密码。"
                : IsCurrentUserSelected
                    ? "可以管理账号，但不能删除当前正在登录的账号。"
                    : "可以修改用户资料、密码和操作权限。";
        }

        return IsCurrentUserSelected
            ? "当前账号只能修改密码，其他资料和权限只读。"
            : "只能查看其他用户的信息。";
    }

    private bool EnsureCanManageUsers() =>
        _authorization.TryAuthorize(PermissionKey.UserManagement);

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    private void PostToUi(Action action)
    {
        if (_dispatcher is null) action();
        else _dispatcher.Post(action);
    }

    public void Dispose()
    {
        _authorization.AccessChanged -= Authorization_AccessChanged;
        if (_controlService is not null) _controlService.Changed -= ControlServiceChanged;
    }
}
