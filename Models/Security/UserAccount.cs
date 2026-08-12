namespace Small_square_cavity_coating_machine.Models.Security;

public sealed record UserPermission(PermissionKey Key, bool IsAllowed);

public sealed record UserAccount(
    long Id,
    string UserName,
    bool IsBuiltInAdministrator,
    IReadOnlySet<PermissionKey> Permissions,
    byte[]? AvatarData = null)
{
    public bool HasPermission(PermissionKey permission) =>
        IsBuiltInAdministrator || Permissions.Contains(permission);
}
