using Small_square_cavity_coating_machine.ViewModels.Security;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Controls;

namespace Small_square_cavity_coating_machine.Views;

public partial class User : Page
{
    private UserManagementViewModel? _viewModel;
    private PasswordBox? _passwordInput;
    private bool _isRestoringSelection;

    public User()
    {
        InitializeComponent();
    }

    public User(UserManagementViewModel viewModel)
        : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private void PasswordInput_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel is not null && sender is PasswordBox passwordBox)
        {
            _passwordInput = passwordBox;
            _viewModel.PendingPassword = passwordBox.Password;
        }
    }

    private void UserNameInput_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is not null && !_viewModel.CanEditUserName)
        {
            _viewModel.NotifyRestrictedAccountEditorClicked();
            e.Handled = true;
        }
    }

    private void PasswordInput_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is not null && !_viewModel.CanEditPassword)
        {
            _viewModel.NotifyRestrictedAccountEditorClicked();
            e.Handled = true;
        }
    }

    private void PermissionCheckBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is not null && !_viewModel.CanEditPermissions)
        {
            _viewModel.NotifyRestrictedPermissionEditorClicked();
            e.Handled = true;
        }
    }

    private void UserList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null || _isRestoringSelection || sender is not ListBox listBox)
        {
            return;
        }

        if (listBox.SelectedItem is not Models.Security.UserAccount selectedUser)
        {
            return;
        }

        if (_viewModel.TrySelectUser(selectedUser))
        {
            return;
        }

        _isRestoringSelection = true;
        listBox.SelectedItem = _viewModel.SelectedUser;
        _isRestoringSelection = false;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserManagementViewModel.PendingPassword)
            && _passwordInput is not null
            && _passwordInput.Password != _viewModel?.PendingPassword)
        {
            _passwordInput.Password = _viewModel?.PendingPassword ?? string.Empty;
        }
    }
}
