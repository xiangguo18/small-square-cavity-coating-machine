using System.Windows;

namespace Small_square_cavity_coating_machine.Services;

/// <summary>
/// 关键操作执行前的人工确认弹窗服务。
/// </summary>
public interface IConfirmationDialogService
{
    /// <summary>
    /// 弹出确认框。返回 true 表示用户确认继续，false 表示取消本次操作。
    /// </summary>
    bool Confirm(string title, string message);
}

/// <summary>
/// 基于 WPF MessageBox 的确认弹窗实现；默认结果为“否”（安全默认）。
/// </summary>
public sealed class WpfConfirmationDialogService : IConfirmationDialogService
{
    public bool Confirm(string title, string message)
    {
        return MessageBox.Show(
                   Application.Current.MainWindow,
                   message,
                   title,
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Question,
                   MessageBoxResult.No)
               == MessageBoxResult.Yes;
    }
}
