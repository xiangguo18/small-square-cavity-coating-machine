using CommunityToolkit.Mvvm.ComponentModel;
using Small_square_cavity_coating_machine.Models.Security;

namespace Small_square_cavity_coating_machine.ViewModels.Security;

public sealed partial class PermissionOptionViewModel : ObservableObject
{
    public PermissionOptionViewModel(PermissionKey key)
    {
        Key = key;
        DisplayName = PermissionCatalog.GetDisplayName(key);
    }

    public PermissionKey Key { get; }

    public string DisplayName { get; }

    [ObservableProperty]
    private bool isAllowed;
}
