namespace Small_square_cavity_coating_machine.Models.Security;

public enum PermissionKey
{
    IoStatus = 1,
    ParameterSettings = 2,
    SystemStatus = 3,
    ProcessRecipe = 4,
    HistoryRecords = 5,
    UserManagement = 6
}

public static class PermissionCatalog
{
    public static IReadOnlyList<PermissionKey> All { get; } =
    [
        PermissionKey.IoStatus,
        PermissionKey.ParameterSettings,
        PermissionKey.SystemStatus,
        PermissionKey.ProcessRecipe,
        PermissionKey.HistoryRecords,
        PermissionKey.UserManagement
    ];

    public static string GetDisplayName(PermissionKey permission) => permission switch
    {
        PermissionKey.IoStatus => "I/O状态",
        PermissionKey.ParameterSettings => "参数设置",
        PermissionKey.SystemStatus => "系统状态",
        PermissionKey.ProcessRecipe => "工艺配方",
        PermissionKey.HistoryRecords => "历史记录",
        PermissionKey.UserManagement => "用户权限",
        _ => permission.ToString()
    };
}
