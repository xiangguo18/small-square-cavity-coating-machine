using Small_square_cavity_coating_machine.Views;
using Small_square_cavity_coating_machine.Services;
using System;
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
        private long _menuAnimationVersion;

        public MainView(ApplicationServices services)
        {
            _ioPage = new IO();
            _parameterPage = new Parameter();
            _controlmPage = new Controlview(services.ControlViewModel);
            _processPage = new Process(services.ProcessViewModel);
            _historyPage = new History(services.HistoryViewModel);
            _userPage = new User();

            InitializeComponent();

            // 程序启动时默认显示 I/O 页面
            //PageNavigation.Navigate(_controlmPage);
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
            PageNavigation.Navigate(_ioPage);
        }

        // 参数设置
        private void ParameterButton_Click(object sender, RoutedEventArgs e)
        {
            PageNavigation.Navigate(_parameterPage);
        }

        // 系统状态
        private void SystemButton_Click(object sender, RoutedEventArgs e)
        {
            PageNavigation.Navigate(_controlmPage);
        }

        // 工艺配方
        private void ProcessButton_Click(object sender, RoutedEventArgs e)
        {
            PageNavigation.Navigate(_processPage);
        }

        // 历史记录
        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            PageNavigation.Navigate(_historyPage);
        }

        // 用户权限
        private void UserButton_Click(object sender, RoutedEventArgs e)
        {
            PageNavigation.Navigate(_userPage);
        }
    }
}
