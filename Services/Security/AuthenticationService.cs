using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.History;

namespace Small_square_cavity_coating_machine.Services.Security;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _repository;
    private readonly IOperationLogRepository _operationLogRepository;

    public AuthenticationService(
        IUserRepository repository,
        IUserSession session,
        IOperationLogRepository operationLogRepository)
    {
        _repository = repository;
        Session = session;
        _operationLogRepository = operationLogRepository;
    }

    public IUserSession Session { get; }

    public AuthenticationResult Login(string userName, string password)
    {
        var normalizedName = userName?.Trim() ?? string.Empty;
        if (normalizedName.Length == 0 || string.IsNullOrEmpty(password))
        {
            return AuthenticationResult.Failed("请输入用户名和密码。");
        }

        if (!_repository.VerifyPassword(normalizedName, password))
        {
            Log(normalizedName, "登录失败", false, "用户名或密码错误");
            return AuthenticationResult.Failed("用户名或密码错误。");
        }

        var user = _repository.GetByUserName(normalizedName);
        if (user is null)
        {
            return AuthenticationResult.Failed("用户不存在。");
        }

        Session.SetCurrentUser(user);
        Log(user.UserName, "登录", true, string.Empty);
        return AuthenticationResult.Success;
    }

    public void Logout()
    {
        var userName = Session.CurrentUser?.UserName;
        if (userName is null)
        {
            return;
        }

        Log(userName, "退出登录", true, string.Empty);
        Session.SignOut();
    }

    public AuthenticationResult ChangeCurrentPassword(
        string currentPassword,
        string newPassword)
    {
        var user = Session.CurrentUser;
        if (user is null)
        {
            return AuthenticationResult.Failed("请先登录。");
        }

        try
        {
            _repository.ChangePassword(user.Id, currentPassword, newPassword);
            Session.RefreshCurrentUser();
            Log(user.UserName, "修改密码", true, string.Empty);
            return AuthenticationResult.Success;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            Log(user.UserName, "修改密码", false, exception.Message);
            return AuthenticationResult.Failed(exception.Message);
        }
    }

    public AuthenticationResult SetCurrentPassword(string newPassword)
    {
        var user = Session.CurrentUser;
        if (user is null)
        {
            return AuthenticationResult.Failed("请先登录。");
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            return AuthenticationResult.Failed("新密码不能为空。");
        }

        try
        {
            _repository.Update(user.Id, user.UserName, newPassword, user.Permissions);
            Session.RefreshCurrentUser();
            Log(user.UserName, "修改密码", true, string.Empty);
            return AuthenticationResult.Success;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            Log(user.UserName, "修改密码", false, exception.Message);
            return AuthenticationResult.Failed(exception.Message);
        }
    }

    public AuthenticationResult UpdateCurrentAvatar(byte[]? avatarData)
    {
        var user = Session.CurrentUser;
        if (user is null)
        {
            return AuthenticationResult.Failed("请先登录。");
        }

        try
        {
            _repository.UpdateAvatar(user.Id, avatarData);
            Session.RefreshCurrentUser();
            Log(user.UserName, avatarData is null ? "恢复默认头像" : "修改头像", true, string.Empty);
            return AuthenticationResult.Success;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return AuthenticationResult.Failed(exception.Message);
        }
    }

    private void Log(string userName, string action, bool successful, string failureReason)
    {
        _operationLogRepository.Add(new OperationLogRecord(
            DateTimeOffset.Now,
            userName,
            "用户会话",
            action,
            string.Empty,
            successful,
            !successful,
            failureReason,
            false));
    }
}
