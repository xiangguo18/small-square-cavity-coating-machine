using Microsoft.Win32;
using Small_square_cavity_coating_machine.Controls;
using Small_square_cavity_coating_machine.Services.Security;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Small_square_cavity_coating_machine.Views;

public partial class UserProfileWindow : Window
{
    private readonly IAuthenticationService _authentication;
    private readonly IAvatarImageService _avatarImageService;
    private readonly ByteArrayToImageSourceConverter _avatarConverter = new();
    private byte[]? _originalAvatarData;
    private byte[]? _pendingAvatarData;

    public UserProfileWindow(
        IAuthenticationService authentication,
        IAvatarImageService avatarImageService)
    {
        _authentication = authentication;
        _avatarImageService = avatarImageService;
        InitializeComponent();

        var user = authentication.Session.CurrentUser
            ?? throw new InvalidOperationException("请先登录。");
        DataContext = user;
        _originalAvatarData = user.AvatarData;
        _pendingAvatarData = user.AvatarData;
        UpdateAvatarPreview();
    }

    private void ChooseAvatarButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择头像图片",
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _pendingAvatarData = _avatarImageService.LoadAndNormalize(dialog.FileName);
            UpdateAvatarPreview();
            ErrorText.Text = string.Empty;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or InvalidOperationException)
        {
            ErrorText.Text = $"头像读取失败：{exception.Message}";
        }
    }

    private void ClearAvatarButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingAvatarData = null;
        UpdateAvatarPreview();
        ErrorText.Text = string.Empty;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var wantsPasswordChange =
            CurrentPasswordInput.Password.Length > 0
            || NewPasswordInput.Password.Length > 0
            || ConfirmPasswordInput.Password.Length > 0;

        if (wantsPasswordChange)
        {
            if (CurrentPasswordInput.Password.Length == 0
                || NewPasswordInput.Password.Length == 0)
            {
                ErrorText.Text = "修改密码时必须填写原密码和新密码。";
                return;
            }

            if (NewPasswordInput.Password != ConfirmPasswordInput.Password)
            {
                ErrorText.Text = "两次输入的新密码不一致。";
                return;
            }

            var passwordResult = _authentication.ChangeCurrentPassword(
                CurrentPasswordInput.Password,
                NewPasswordInput.Password);
            if (!passwordResult.IsSuccessful)
            {
                ErrorText.Text = passwordResult.ErrorMessage;
                return;
            }
        }

        if (!AvatarEquals(_originalAvatarData, _pendingAvatarData))
        {
            var avatarResult = _authentication.UpdateCurrentAvatar(_pendingAvatarData);
            if (!avatarResult.IsSuccessful)
            {
                ErrorText.Text = avatarResult.ErrorMessage;
                return;
            }
        }

        DialogResult = true;
        Close();
    }

    private void UpdateAvatarPreview()
    {
        DefaultAvatarIcon.Visibility = _pendingAvatarData is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        AvatarPreview.Source = _avatarConverter.Convert(
            _pendingAvatarData,
            typeof(ImageSource),
            null,
            CultureInfo.CurrentCulture) as ImageSource;
    }

    private static bool AvatarEquals(byte[]? left, byte[]? right) =>
        ReferenceEquals(left, right)
        || (left is not null && right is not null && left.AsSpan().SequenceEqual(right));
}
