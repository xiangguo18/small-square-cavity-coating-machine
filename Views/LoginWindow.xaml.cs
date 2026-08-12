using Small_square_cavity_coating_machine.Services.Security;
using System.Windows;

namespace Small_square_cavity_coating_machine.Views;

public partial class LoginWindow : Window
{
    private readonly IAuthenticationService _authentication;

    public LoginWindow(IAuthenticationService authentication)
    {
        _authentication = authentication;
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => UserNameInput.Focus();

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var result = _authentication.Login(UserNameInput.Text, PasswordInput.Password);
        if (!result.IsSuccessful)
        {
            ErrorText.Text = result.ErrorMessage;
            PasswordInput.Clear();
            PasswordInput.Focus();
            return;
        }

        DialogResult = true;
        Close();
    }
}
