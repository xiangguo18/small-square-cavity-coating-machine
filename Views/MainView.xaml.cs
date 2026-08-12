using Small_square_cavity_coating_machine.Views;
using Small_square_cavity_coating_machine.Services;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;


namespace Small_square_cavity_coating_machine.Views
{
    /// <summary>
    /// MainView.xaml 的交互逻辑
    /// </summary>
    public partial class MainView : Window
    {
        private const double CollapsedMenuWidth = 80d;
        private const double ExpandedMenuWidth = 260d;
        private static readonly TimeSpan FullMenuAnimationDuration = TimeSpan.FromSeconds(0.5);

        // 提前创建六个页面对象
        private readonly IO _ioPage;
        private readonly Parameter _parameterPage;
        private readonly Controlview _controlmPage;
        private readonly Process _processPage;
        private readonly History _historyPage;
        private readonly User _userPage;
        private readonly ViewModels.Security.UserManagementViewModel _userManagementViewModel;
        private readonly ViewModels.Security.AccountShellViewModel _accountShellViewModel;
        private RadioButton? _selectedMenuButton;
        private long _menuAnimationVersion;

        public MainView(ApplicationServices services)
        {
            _userManagementViewModel = services.UserManagementViewModel;
            _accountShellViewModel = services.AccountShellViewModel;
            _ioPage = new IO();
            _parameterPage = new Parameter();
            _controlmPage = new Controlview(services.ControlViewModel);
            _processPage = new Process(services.ProcessViewModel);
            _historyPage = new History(services.HistoryViewModel);
            _userPage = new User(services.UserManagementViewModel);

            InitializeComponent();
            DataContext = services.AccountShellViewModel;
            _selectedMenuButton = SystemMenuButton;

            // 未登录用户也可以查看实时状态；所有写命令仍由权限服务拦截。
            PageNavigation.Navigate(_controlmPage);
        }

        private void MenuToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            AnimateMenuWidth(ExpandedMenuWidth);
        }

        private void MenuToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            AnimateMenuWidth(CollapsedMenuWidth);
        }

        private void AnimateMenuWidth(double targetWidth)
        {
            var currentWidth = gridMenu1.ActualWidth;
            if (!double.IsFinite(currentWidth) || currentWidth <= 0)
            {
                currentWidth = gridMenu1.Width;
            }

            var animationVersion = ++_menuAnimationVersion;

            // Preserve the current animated width before replacing an in-flight animation.
            gridMenu1.BeginAnimation(FrameworkElement.WidthProperty, null);
            gridMenu1.Width = currentWidth;

            var remainingDistance = Math.Abs(targetWidth - currentWidth);
            if (remainingDistance < 0.5d)
            {
                CommitMenuWidth(targetWidth);
                return;
            }

            var fullDistance = ExpandedMenuWidth - CollapsedMenuWidth;
            var duration = TimeSpan.FromTicks(
                Math.Max(
                    1,
                    (long)(FullMenuAnimationDuration.Ticks * (remainingDistance / fullDistance))));

            var animation = new DoubleAnimation(currentWidth, targetWidth, duration)
            {
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseInOut
                }
            };

            animation.Completed += (_, _) =>
            {
                if (animationVersion != _menuAnimationVersion)
                {
                    return;
                }

                gridMenu1.BeginAnimation(FrameworkElement.WidthProperty, null);
                CommitMenuWidth(targetWidth);
            };

            gridMenu1.BeginAnimation(
                FrameworkElement.WidthProperty,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private void CommitMenuWidth(double width)
        {
            gridMenu1.Width = width;
        }

        // I/O状态
        private void IOButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateWithPasswordGuard(_ioPage, sender as RadioButton);
        }

        // 参数设置
        private void ParameterButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateWithPasswordGuard(_parameterPage, sender as RadioButton);
        }

        // 系统状态
        private void SystemButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateWithPasswordGuard(_controlmPage, sender as RadioButton);
        }

        // 工艺配方
        private void ProcessButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateWithPasswordGuard(_processPage, sender as RadioButton);
        }

        // 历史记录
        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateWithPasswordGuard(_historyPage, sender as RadioButton);
        }

        // 用户权限
        private void UserButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateWithPasswordGuard(_userPage, sender as RadioButton);
        }

        private void NavigateWithPasswordGuard(Page targetPage, RadioButton? targetButton)
        {
            if (ReferenceEquals(PageNavigation.Content, targetPage)
                || _userManagementViewModel.TryResolvePendingPasswordChange())
            {
                PageNavigation.Navigate(targetPage);
                _selectedMenuButton = targetButton ?? _selectedMenuButton;
                return;
            }

            if (_selectedMenuButton is not null)
            {
                _selectedMenuButton.IsChecked = true;
            }
        }

        private void AccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (_userManagementViewModel.TryResolvePendingPasswordChange())
            {
                _accountShellViewModel.OpenAccountCommand.Execute(null);
            }
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_userManagementViewModel.TryResolvePendingPasswordChange())
            {
                _accountShellViewModel.LogoutCommand.Execute(null);
            }
        }

        private void MainView_Closing(object? sender, CancelEventArgs e)
        {
            if (!_userManagementViewModel.TryResolvePendingPasswordChange())
            {
                e.Cancel = true;
            }
        }
    }
}
