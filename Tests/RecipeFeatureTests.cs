using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Services.Recipes;
using Small_square_cavity_coating_machine.ViewModels;
using Small_square_cavity_coating_machine.ViewModels.Recipes;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace SmallSquareCavityCoatingMachine.Tests;

public sealed class RecipeFeatureTests
{
    [Fact]
    public void ImportProvidedXls_MapsNineteenColumnsAndIgnoresTemplateTail()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "小方腔工艺配方示例.xls");
        Assert.True(File.Exists(path), $"测试文件不存在：{path}");

        var result = new ExcelRecipeImporter().Import(path);

        Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal(5, result.Layers.Count);
        Assert.Equal(230d, result.Layers[0].CathodeAPower);
        Assert.Equal(320d, result.Layers[0].CathodeBPower);
        Assert.Equal(10d, result.Layers[1].StageSpeedRpm);
        Assert.Equal(15d, result.Layers[2].StageSpeedRpm);
        Assert.Equal(30d, result.Layers[2].IgnitionApcPercent);
        Assert.Equal(40d, result.Layers[2].WorkingApcPercent);
    }

    [Fact]
    public void ImportXlsx_UsesColumnPositionAndPreservesNegativeStageSpeed()
    {
        var path = CreatePositionMappedWorkbook();
        try
        {
            var result = new ExcelRecipeImporter().Import(path);

            Assert.True(result.IsSuccessful, string.Join(Environment.NewLine, result.Errors));
            var layer = Assert.Single(result.Layers);
            Assert.Equal(1, layer.Sequence);
            Assert.Equal(230d, layer.CathodeAPower);
            Assert.Equal(-15d, layer.StageSpeedRpm);
            Assert.Equal(2d, layer.GasStabilizationSeconds);
            Assert.Equal(30d, layer.IgnitionApcPercent);
            Assert.Equal(40d, layer.WorkingApcPercent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("15", 15d)]
    [InlineData("-15", -15d)]
    [InlineData("0", 0d)]
    public void NewLayerDialogModel_AcceptsSignedStageSpeed(string input, double expected)
    {
        var viewModel = new NewRecipeLayerViewModel(1);
        var stageSpeed = viewModel.PowerAndTimeInputs.Single(item => item.Label == "样品台转速");
        stageSpeed.ValueText = input;

        var successful = viewModel.TryBuildLayer(out var layer, out var error);

        Assert.True(successful, error);
        Assert.NotNull(layer);
        Assert.Equal(expected, layer.StageSpeedRpm);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("不是数字")]
    public void NewLayerDialogModel_RejectsInvalidStageSpeed(string input)
    {
        var viewModel = new NewRecipeLayerViewModel(1);
        var stageSpeed = viewModel.PowerAndTimeInputs.Single(item => item.Label == "样品台转速");
        stageSpeed.ValueText = input;

        Assert.False(viewModel.TryBuildLayer(out _, out _));
    }

    [Fact]
    public void NewLayerDialogModel_PressureModeIgnoresApcInputs()
    {
        var viewModel = new NewRecipeLayerViewModel(1);
        viewModel.PressureInputs[0].ValueText = "1.2";
        viewModel.PressureInputs[1].ValueText = "2.3";
        viewModel.ApcInputs[0].ValueText = "不是数字";
        viewModel.ApcInputs[1].ValueText = "200";

        var successful = viewModel.TryBuildLayer(out var layer, out var error);

        Assert.True(successful, error);
        Assert.NotNull(layer);
        Assert.Equal(RecipePressureControlMode.Pressure, layer.PressureControlMode);
        Assert.Equal(1.2d, layer.IgnitionPressurePa);
        Assert.Equal(2.3d, layer.WorkingPressurePa);
        Assert.Equal(0d, layer.IgnitionApcPercent);
        Assert.Equal(0d, layer.WorkingApcPercent);
    }

    [Fact]
    public void NewLayerDialogModel_ApcModeIgnoresPressureInputs()
    {
        var viewModel = new NewRecipeLayerViewModel(1)
        {
            IsApcMode = true
        };
        viewModel.PressureInputs[0].ValueText = "不是数字";
        viewModel.PressureInputs[1].ValueText = "-1";
        viewModel.ApcInputs[0].ValueText = "35";
        viewModel.ApcInputs[1].ValueText = "45";

        var successful = viewModel.TryBuildLayer(out var layer, out var error);

        Assert.True(successful, error);
        Assert.NotNull(layer);
        Assert.Equal(RecipePressureControlMode.ApcPosition, layer.PressureControlMode);
        Assert.Equal(0d, layer.IgnitionPressurePa);
        Assert.Equal(0d, layer.WorkingPressurePa);
        Assert.Equal(35d, layer.IgnitionApcPercent);
        Assert.Equal(45d, layer.WorkingApcPercent);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("3", 3)]
    [InlineData("6", 6)]
    public void NewLayerDialogModel_AcceptsInsertionSequence(string input, int expected)
    {
        var viewModel = new NewRecipeLayerViewModel(6)
        {
            SequenceText = input
        };

        var successful = viewModel.TryBuildLayer(out var layer, out var error);

        Assert.True(successful, error);
        Assert.NotNull(layer);
        Assert.Equal(expected, layer.Sequence);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("1.5")]
    [InlineData("不是整数")]
    public void NewLayerDialogModel_RejectsInvalidInsertionSequence(string input)
    {
        var viewModel = new NewRecipeLayerViewModel(6)
        {
            SequenceText = input
        };

        Assert.False(viewModel.TryBuildLayer(out _, out var error));
        Assert.Contains("1-6", error);
    }

    [Fact]
    public void ProcessViewModel_InsertsLayerAndShiftsFollowingSequences()
    {
        var dialog = new RecordingRecipeDialogService
        {
            NewLayerResult = new RecipeLayer { Sequence = 3, CathodeAPower = 999d }
        };
        using var viewModel = CreateProcessViewModel(dialog);
        for (var sequence = 1; sequence <= 5; sequence++)
        {
            viewModel.Layers.Add(new RecipeLayer
            {
                Sequence = sequence,
                CathodeAPower = sequence * 100d
            });
        }

        viewModel.NewRecipeLayerCommand.Execute(null);

        Assert.Equal([1, 2, 3, 4, 5, 6], viewModel.Layers.Select(layer => layer.Sequence));
        Assert.Equal(999d, viewModel.Layers.Single(layer => layer.Sequence == 3).CathodeAPower);
        Assert.Equal(300d, viewModel.Layers.Single(layer => layer.Sequence == 4).CathodeAPower);
        Assert.Equal(500d, viewModel.Layers.Single(layer => layer.Sequence == 6).CathodeAPower);
    }

    [Fact]
    public void ProcessViewModel_DisablesRecipeMutationCommandsWhileRunning()
    {
        var dialog = new RecordingRecipeDialogService();
        using var viewModel = CreateProcessViewModel(dialog);
        viewModel.Layers.Add(new RecipeLayer { Sequence = 1 });

        viewModel.IsRunning = true;

        Assert.False(viewModel.ImportRecipeCommand.CanExecute(null));
        Assert.False(viewModel.ClearRecipeCommand.CanExecute(null));
        Assert.False(viewModel.NewRecipeLayerCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dispatch_SendsSignedSpeedUnchangedAndWaitsForProcessComplete()
    {
        var gateway = new RecordingRecipeGateway();
        var service = new RecipeDispatchService(
            gateway,
            new InMemoryOperationLogRepository(),
            FastOptions);
        var layer = new RecipeLayer { Sequence = 1, StageSpeedRpm = -18d };
        var request = new RecipeRunRequest(RecipeDispatchMode.All, [layer], "序号1");

        var result = await service.RunAsync(
            request,
            new InlineProgress<RecipeRunProgress>(),
            CancellationToken.None);

        Assert.True(result.IsCompleted);
        Assert.Equal(-18d, Assert.Single(gateway.SentLayers).StageSpeedRpm);
        Assert.True(gateway.ProcessCompletionWasObserved);
    }

    [Fact]
    public async Task Dispatch_StopsBeforeNextLayerWhenPlcFaults()
    {
        var gateway = new RecordingRecipeGateway { FailLayerCompletion = true };
        var service = new RecipeDispatchService(
            gateway,
            new InMemoryOperationLogRepository(),
            FastOptions);
        var request = new RecipeRunRequest(
            RecipeDispatchMode.All,
            [
                new RecipeLayer { Sequence = 1 },
                new RecipeLayer { Sequence = 2 }
            ],
            "序号1-2");

        var result = await service.RunAsync(
            request,
            new InlineProgress<RecipeRunProgress>(),
            CancellationToken.None);

        Assert.False(result.IsCompleted);
        Assert.Single(gateway.SentLayers);
        Assert.Contains("模拟PLC异常", result.FailureReason);
    }

    private static RecipeDispatchOptions FastOptions { get; } = new()
    {
        PreflightTimeout = TimeSpan.FromSeconds(1),
        LayerAcceptedTimeout = TimeSpan.FromSeconds(1),
        LayerCompleteTimeout = TimeSpan.FromSeconds(1),
        ProcessCompleteTimeout = TimeSpan.FromSeconds(1)
    };

    private static ProcessViewModel CreateProcessViewModel(IRecipeUserDialogService dialogService)
    {
        var gateway = new RecordingRecipeGateway();
        return new ProcessViewModel(
            new EmptyRecipeImporter(),
            new RecipeDispatchService(
                gateway,
                new InMemoryOperationLogRepository(),
                FastOptions),
            gateway,
            dialogService,
            new InMemoryOperationLogRepository(),
            ApplicationStatusViewModel.Instance);
    }

    private static string CreatePositionMappedWorkbook()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recipe-position-{Guid.NewGuid():N}.xlsx");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);
        WriteEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WriteEntry(
            archive,
            "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="任意名称" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
        WriteEntry(
            archive,
            "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);
        WriteEntry(
            archive,
            "xl/worksheets/sheet1.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <dimension ref="A1:T3"/>
              <sheetData>
                <row r="1">
                  <c r="A1" t="inlineStr"><is><t>这些表头可以任意修改</t></is></c>
                  <c r="S1" t="inlineStr"><is><t>只解释位置</t></is></c>
                  <c r="T1" t="inlineStr"><is><t>额外说明列</t></is></c>
                </row>
                <row r="2">
                  <c r="A2"><v>1</v></c>
                  <c r="B2"><v>230</v></c>
                  <c r="G2"><v>-15</v></c>
                  <c r="R2"><v>30</v></c>
                  <c r="S2"><v>40</v></c>
                  <c r="T2" t="inlineStr"><is><t>此列忽略</t></is></c>
                </row>
                <row r="3"><c r="A3"><v>2</v></c></row>
              </sheetData>
            </worksheet>
            """);
        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }

    private sealed class RecordingRecipeGateway : IRecipePlcGateway
    {
        public bool IsAvailable => true;

        public bool IsSimulated => true;

        public bool FailLayerCompletion { get; init; }

        public bool ProcessCompletionWasObserved { get; private set; }

        public List<RecipeLayer> SentLayers { get; } = [];

        public Task<RecipeGatewayPreflightResult> PreflightAsync(CancellationToken cancellationToken)
            => Task.FromResult(RecipeGatewayPreflightResult.Ready);

        public Task SendLayerAsync(RecipeLayer layer, CancellationToken cancellationToken)
        {
            SentLayers.Add(layer.Snapshot());
            return Task.CompletedTask;
        }

        public Task WaitForLayerAcceptedAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WaitForLayerCompletedAsync(CancellationToken cancellationToken)
            => FailLayerCompletion
                ? Task.FromException(new InvalidOperationException("模拟PLC异常"))
                : Task.CompletedTask;

        public Task WaitForProcessCompletedAsync(CancellationToken cancellationToken)
        {
            ProcessCompletionWasObserved = true;
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyRecipeImporter : IRecipeExcelImporter
    {
        public RecipeImportResult Import(string filePath)
            => new([], [new RecipeImportError(0, 0, "测试未配置导入文件")]);
    }

    private sealed class RecordingRecipeDialogService : IRecipeUserDialogService
    {
        public RecipeLayer? NewLayerResult { get; init; }

        public string? SelectRecipeFile() => null;

        public bool ConfirmReplaceExistingRecipe() => true;

        public bool ConfirmClearRecipe() => true;

        public RecipeLayer? ShowNewLayerDialog(int nextSequence) => NewLayerResult;

        public void ShowImportErrors(IReadOnlyList<RecipeImportError> errors)
        {
        }

        public void ShowInformation(string message, string title)
        {
        }

        public void ShowRunFinished(RecipeRunResult result)
        {
        }
    }
}
