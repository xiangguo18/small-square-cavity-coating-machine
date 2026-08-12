using Small_square_cavity_coating_machine.Models.Security;

namespace Small_square_cavity_coating_machine.Services.Security;

public interface IUserRepository
{
    void Initialize();

    IReadOnlyList<UserAccount> GetAll();

    UserAccount? GetById(long id);

    UserAccount? GetByUserName(string userName);

    UserAccount Create(string userName, string password, IEnumerable<PermissionKey> permissions);

    UserAccount Update(
        long id,
        string userName,
        string? newPassword,
        IEnumerable<PermissionKey> permissions);

    void Delete(long id);

    bool VerifyPassword(string userName, string password);

    UserAccount UpdateAvatar(long id, byte[]? avatarData);

    UserAccount ChangePassword(long id, string currentPassword, string newPassword);
}

public interface IUserSession
{
    event EventHandler? CurrentUserChanged;

    UserAccount? CurrentUser { get; }

    bool IsAuthenticated { get; }

    void SetCurrentUser(UserAccount user);

    void RefreshCurrentUser();

    void SignOut();
}

public interface IAuthorizationService
{
    event EventHandler? AccessChanged;

    event EventHandler<AuthorizationDeniedEventArgs>? AccessDenied;

    string CurrentUserName { get; }

    bool CanOperate(PermissionKey permission);

    bool TryAuthorize(PermissionKey permission);
}

public sealed class AuthorizationDeniedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public interface IUserManagementDialogService
{
    bool ConfirmDelete(string userName);

    PendingPasswordDecision ConfirmPendingPassword(string userName, bool isCreating);
}

public enum PendingPasswordDecision
{
    Save,
    Discard,
    Cancel
}

public interface IAuthenticationService
{
    IUserSession Session { get; }

    AuthenticationResult Login(string userName, string password);

    void Logout();

    AuthenticationResult ChangeCurrentPassword(
        string currentPassword,
        string newPassword);

    AuthenticationResult SetCurrentPassword(string newPassword);

    AuthenticationResult UpdateCurrentAvatar(byte[]? avatarData);
}

public interface IAccountDialogService
{
    void ShowLogin();

    void ShowProfile();

    bool ConfirmLogout(string userName);
}

public interface IAvatarImageService
{
    byte[] LoadAndNormalize(string filePath);
}

public interface IPasswordHasher
{
    PasswordHashResult Hash(string password);

    bool Verify(string password, string hash, string salt, int iterations);
}

public sealed record PasswordHashResult(string Hash, string Salt, int Iterations);

public sealed record AuthenticationResult(bool IsSuccessful, string ErrorMessage)
{
    public static AuthenticationResult Success { get; } = new(true, string.Empty);

    public static AuthenticationResult Failed(string message) => new(false, message);
}
