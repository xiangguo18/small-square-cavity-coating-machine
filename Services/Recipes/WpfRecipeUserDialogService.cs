using Microsoft.Win32;
using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.ViewModels.Recipes;
using Small_square_cavity_coating_machine.Views;
using System.Windows;

namespace Small_square_cavity_coating_machine.Services.Recipes;

public sealed class WpfRecipeUserDialogService(IReadOnlyList<RecipeDefinition>? definitions = null) : IRecipeUserDialogService
{
    public string? SelectRecipeFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入工艺配方",
            Filter = "Excel 配方文件 (*.xls;*.xlsx)|*.xls;*.xlsx|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(Application.Current.MainWindow) == true
            ? dialog.FileName
            : null;
    }

    public bool ConfirmReplaceExistingRecipe()
    {
        return MessageBox.Show(
                   Application.Current.MainWindow,
                   "导入新配方将替换当前表格中的全部配方层，是否继续？",
                   "替换当前配方",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning,
                   MessageBoxResult.No)
               == MessageBoxResult.Yes;
    }

    public bool ConfirmClearRecipe()
    {
        return MessageBox.Show(
                   Application.Current.MainWindow,
                   "清除后当前表格中的全部配方层都将被移除，是否继续？",
                   "清除当前配方",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning,
                   MessageBoxResult.No)
               == MessageBoxResult.Yes;
    }

    public RecipeLayer? ShowNewLayerDialog(int nextSequence)
    {
        var viewModel = new NewRecipeLayerViewModel(nextSequence, definitions);
        var window = new NewRecipeLayerWindow(viewModel)
        {
            Owner = Application.Current.MainWindow
        };
        return window.ShowDialog() == true ? window.CreatedLayer : null;
    }

    public void ShowImportErrors(IReadOnlyList<RecipeImportError> errors)
    {
        const int maximumDisplayedErrors = 12;
        var lines = errors.Take(maximumDisplayedErrors).Select(error => $"• {error}");
        var message = string.Join(Environment.NewLine, lines);
        if (errors.Count > maximumDisplayedErrors)
        {
            message += $"{Environment.NewLine}……另有 {errors.Count - maximumDisplayedErrors} 个错误";
        }

        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            "配方导入失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public void ShowInformation(string message, string title)
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public void ShowRunFinished(RecipeRunResult result)
    {
        var message = result.IsCompleted
            ? $"本次【{result.RecipeName}】镀膜完成，共完成{result.CompletedLayers}层。"
            : $"停止自动下发，不表示设备已停机。{Environment.NewLine}原因：{result.FailureReason}";
        MessageBox.Show(
            Application.Current.MainWindow,
            message + (result.Notice.Length > 0 ? Environment.NewLine + result.Notice : ""),
            result.IsCompleted ? "镀膜完成" : "配方下发中断",
            MessageBoxButton.OK,
            result.IsCompleted ? MessageBoxImage.Information : MessageBoxImage.Error);
    }
}
