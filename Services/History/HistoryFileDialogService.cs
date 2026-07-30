using Microsoft.Win32;

namespace Small_square_cavity_coating_machine.Services.History;

public sealed class HistoryFileDialogService : IHistoryFileDialogService
{
    private const string CsvFilter = "曲线数据文件 (*.csv)|*.csv|所有文件 (*.*)|*.*";

    public string? SelectSavePath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".csv",
            FileName = suggestedFileName,
            Filter = CsvFilter,
            OverwritePrompt = true,
            Title = "保存本次启动以来的曲线数据"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? SelectOpenPath()
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            DefaultExt = ".csv",
            Filter = CsvFilter,
            Multiselect = false,
            Title = "查询工艺或手动保存的曲线数据"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
