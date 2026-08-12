using Small_square_cavity_coating_machine.Models.Security;

namespace Small_square_cavity_coating_machine.Services.Security;

public sealed class UserSession : IUserSession
{
    private readonly IUserRepository _repository;
    private UserAccount? _currentUser;

    public UserSession(IUserRepository repository)
    {
        _repository = repository;
    }

    public UserSession(IUserRepository repository, string initialUserName)
        : this(repository)
    {
        _currentUser = repository.GetByUserName(initialUserName)
            ?? throw new InvalidOperationException($"找不到默认用户 {initialUserName}。");
    }

    public event EventHandler? CurrentUserChanged;

    public UserAccount? CurrentUser => _currentUser;

    public bool IsAuthenticated => _currentUser is not null;

    public void SetCurrentUser(UserAccount user)
    {
        _currentUser = user ?? throw new ArgumentNullException(nameof(user));
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshCurrentUser()
    {
        if (_currentUser is null)
        {
            return;
        }

        var refreshed = _repository.GetById(_currentUser.Id);
        if (refreshed is null)
        {
            throw new InvalidOperationException("当前用户已不存在。");
        }

        SetCurrentUser(refreshed);
    }

    public void SignOut()
    {
        if (_currentUser is null)
        {
            return;
        }

        _currentUser = null;
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly IUserSession _session;

    public AuthorizationService(IUserSession session)
    {
        _session = session;
        _session.CurrentUserChanged += (_, _) => AccessChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? AccessChanged;

    public event EventHandler<AuthorizationDeniedEventArgs>? AccessDenied;

    public string CurrentUserName => _session.CurrentUser?.UserName ?? "未登录";

    public bool CanOperate(PermissionKey permission) =>
        _session.CurrentUser?.HasPermission(permission) == true;

    public bool TryAuthorize(PermissionKey permission)
    {
        if (CanOperate(permission))
        {
            return true;
        }

        var message = _session.IsAuthenticated
            ? "当前账号无此操作权限"
            : "请先登录，当前为只读查看";
        AccessDenied?.Invoke(this, new AuthorizationDeniedEventArgs(message));
        return false;
    }
}
