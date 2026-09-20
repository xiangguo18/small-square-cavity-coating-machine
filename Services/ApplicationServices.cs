using Small_square_cavity_coating_machine.Models;
using Small_square_cavity_coating_machine.Models.Alarms;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Services.Recipes;
using Small_square_cavity_coating_machine.Services.Security;
using Small_square_cavity_coating_machine.ViewModels;
using Small_square_cavity_coating_machine.ViewModels.History;
using Small_square_cavity_coating_machine.ViewModels.Recipes;
using Small_square_cavity_coating_machine.ViewModels.Security;
using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.ViewModels.Equipment;
using System.IO;
using System.ComponentModel;
using System.Windows.Threading;

namespace Small_square_cavity_coating_machine.Services;

/// <summary>
/// 应用组合根：唯一设备会话，独立只读定义库与运行记录库。
/// </summary>
public sealed class ApplicationServices : IDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly ITelemetrySource _telemetrySource;
    private readonly IUiDispatcher _uiDispatcher;
    private bool _isStarted;

    public ApplicationServices(Dispatcher dispatcher)
    {
        _uiDispatcher = new WpfUiDispatcher(dispatcher);
        var userDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmallSquareCavityCoatingMachine");
        PasswordHasher = new PasswordHasher();
        UserRepository = new SqliteUserRepository(
            Path.Combine(userDataDirectory, "users.db"),
            PasswordHasher);
        UserRepository.Initialize();

        SessionTrendStore = new SessionTrendStore();
        var simulateAlarms = Environment.GetCommandLineArgs().Contains("--simulate-alarms", StringComparer.OrdinalIgnoreCase);
        var runtime = new EquipmentRuntimeRepository(Path.Combine(userDataDirectory,
            simulateAlarms ? "equipment-runtime.simulation.db" : "equipment-runtime.db"));
        OperationLogRepository = runtime;
        UserSession = new UserSession(UserRepository);
        AuthorizationService = new AuthorizationService(UserSession);
        AlarmLogRepository = new SqliteAlarmLogRepository(Path.Combine(userDataDirectory,
            simulateAlarms ? "alarm-history.simulation.db" : "alarm-history.db"));
        var definitionErrors = new Dictionary<string, string>();
        string? definitionPath = null;
        try { definitionPath = SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(Path.Combine(userDataDirectory, "Database")); }
        catch (Exception ex) { foreach (var group in EquipmentGroups.Monitored) definitionErrors[group] = ex.Message; }
        AlarmDefinitions = LoadDefinitions(EquipmentGroups.Alarm, () => new SqliteAlarmDefinitionRepository(definitionPath!).Load());
        var ioDefinitions = LoadDefinitions(EquipmentGroups.Io, () => new SqliteEquipmentDefinitionRepository(definitionPath!).LoadIo());
        var parameterDefinitions = LoadDefinitions(EquipmentGroups.Parameter, () => new SqliteEquipmentDefinitionRepository(definitionPath!).LoadParameters());
        var controlDefinitions = EquipmentControlDefinitions.Empty;
        IReadOnlyList<T> LoadDefinitions<T>(string group, Func<IReadOnlyList<T>> load)
        {
            if (definitionPath is null) return [];
            try { return load(); }
            catch (Exception ex) { definitionErrors[group] = ex.Message; return []; }
        }
        var recipeDefinitions = LoadDefinitions(EquipmentGroups.Recipe, () => new SqliteRecipeDefinitionRepository(definitionPath!).Load());
        var processDefinitions = LoadDefinitions(EquipmentGroups.Process, () => new SqliteProcessDefinitionRepository(definitionPath!).Load());
        if (definitionPath is not null)
        {
            try { controlDefinitions = new SqliteEquipmentDefinitionRepository(definitionPath).LoadControls(); }
            catch (Exception ex)
            {
                foreach (var group in EquipmentGroups.ControlArrays.Concat(EquipmentGroups.SystemControlPoints))
                    definitionErrors[group] = ex.Message;
            }
        }
        var simulator = simulateAlarms ? new SimulatedEquipmentSessionFactory(parameterDefinitions, processDefinitions) : null;
        EquipmentClient = new OpcUaEquipmentClient(
            simulateAlarms ? new SimulationConnectionSettings()
                : new AlarmConnectionSettingsStore(Path.Combine(userDataDirectory, "alarms.opcua.json")),
            simulator is not null ? simulator : new OpcUaEquipmentSessionFactory(Path.Combine(userDataDirectory, "pki")),
            AlarmDefinitions, ioDefinitions, parameterDefinitions, AuthorizationService, runtime, simulateAlarms, definitionErrors,
            enableRecipes: true, processDefinitions: processDefinitions, controlDefinitions: controlDefinitions);
        ControlService = new EquipmentControlService(EquipmentClient, controlDefinitions);
        AlarmSignalSource = new EquipmentAlarmSignalSource(EquipmentClient);
        IoStatus = new IoStatusViewModel(ioDefinitions, EquipmentClient, _uiDispatcher);
        ParameterSettings = new ParameterSettingsViewModel(parameterDefinitions, EquipmentClient, runtime, AuthorizationService, _uiDispatcher, ControlService);
        AlarmNavigation = new AlarmNavigationService();
        AlarmMonitor = new AlarmMonitorService(AlarmDefinitions, AlarmSignalSource, AlarmLogRepository);
        AlarmStatus = new AlarmStatusViewModel(AlarmMonitor, AlarmLogRepository, _uiDispatcher, AlarmNavigation);
        ApplicationStatusViewModel.Instance.AlarmStatus = AlarmStatus;
        ApplicationStatusViewModel.Instance.PlcConnectionState = PlcConnectionState.Disconnected;
        ApplicationStatusViewModel.Instance.PlcConnectionText = "PLC未连接";
        PlcConnection = new PlcConnectionStatusViewModel(EquipmentClient, _uiDispatcher);
        ApplicationStatusViewModel.Instance.PlcConnection = PlcConnection;
        PlcConnection.PropertyChanged += PlcConnectionPropertyChanged;
        SyncPlcConnectionStatus();
        AuthenticationService = new AuthenticationService(
            UserRepository,
            UserSession,
            OperationLogRepository);
        AvatarImageService = new AvatarImageService();
        var accountDialogService = new WpfAccountDialogService(
            AuthenticationService,
            AvatarImageService);
        AccountShellViewModel = new AccountShellViewModel(
            AuthenticationService,
            accountDialogService,
            AuthorizationService);
        UserManagementViewModel = new UserManagementViewModel(
            UserRepository,
            UserSession,
            AuthorizationService,
            AuthenticationService,
            new WpfUserManagementDialogService(), ControlService, _uiDispatcher);
        var trendFileService = new CsvTrendFileService();
        var fileDialogService = new HistoryFileDialogService();
        ProcessTrendRecorder = new ProcessTrendRecorder(
            new SqliteProcessTrendRepository(Path.Combine(userDataDirectory,
                simulateAlarms ? "trend-history.simulation.db" : "trend-history.db")),
            trendFileService, Path.Combine(userDataDirectory, simulateAlarms ? "ProcessTrends.Simulation" : "ProcessTrends"),
            processDefinitions, simulateAlarms);
        ProcessTrendRecorder.Changed += OnTrendRecordingChanged;

        if (simulateAlarms) SeedSimulationHistory();

        ControlViewModel = new ControlViewModel(
            OperationLogRepository,
            AuthorizationService,
            ControlService,
            _uiDispatcher,
            new WpfConfirmationDialogService());
        RecipePlcGateway = new OpcUaRecipePlcGateway(EquipmentClient, runtime, recipeDefinitions,
            definitionErrors.GetValueOrDefault(EquipmentGroups.Recipe, ""));
        var recipeImporter = new ExcelRecipeImporter(recipeDefinitions);
        var recipeCsvService = new RecipeCsvService(recipeDefinitions);
        var recipeDialogService = new WpfRecipeUserDialogService(recipeDefinitions);
        var recipeDispatchService = new RecipeDispatchService(
            RecipePlcGateway,
            OperationLogRepository, trendRecorder: ProcessTrendRecorder);
        ProcessViewModel = new ProcessViewModel(
            recipeImporter,
            recipeDispatchService,
            RecipePlcGateway,
            recipeDialogService,
            OperationLogRepository,
            ApplicationStatusViewModel.Instance,
            AuthorizationService, _uiDispatcher, recipeCsvService);

        var liveTrend = new LiveTrendViewModel(
            dispatcher,
            SessionTrendStore,
            trendFileService,
            fileDialogService, simulateAlarms, ProcessTrendRecorder);
        var processTrend = new ProcessTrendViewModel(
            dispatcher,
            trendFileService,
            fileDialogService, ProcessTrendRecorder);
        var operationHistory = new OperationHistoryViewModel(OperationLogRepository, _uiDispatcher);
        var alarmHistory = new AlarmHistoryViewModel(AlarmLogRepository, _uiDispatcher,
            simulator is not null ? new AlarmSimulationViewModel(simulator, AlarmDefinitions) : null,
            ControlService);
        HistoryViewModel = new HistoryViewModel(
            liveTrend,
            processTrend,
            operationHistory,
            alarmHistory);

        _telemetrySource = new OpcUaTelemetrySource(EquipmentClient, processDefinitions,
            definitionErrors.GetValueOrDefault(EquipmentGroups.Process, ""));
        _telemetrySource.SampleReceived += OnTelemetrySample;
    }

    public ISessionTrendStore SessionTrendStore { get; }
    public IProcessTrendRecorder ProcessTrendRecorder { get; }

    private void OnTrendRecordingChanged(object? sender, EventArgs e) => _uiDispatcher.Post(() =>
    {
        if (!_shutdown.IsCancellationRequested)
            ApplicationStatusViewModel.Instance.HistoryRecordingError = ProcessTrendRecorder.RecordingError;
    });

    private void OnTelemetrySample(object? sender, TelemetrySample sample) =>
        SessionTrendStore.Append(ProcessTrendRecorder.RecordSample(sample));

    public IPasswordHasher PasswordHasher { get; }

    public IUserRepository UserRepository { get; }

    public IUserSession UserSession { get; }

    public IAuthorizationService AuthorizationService { get; }

    public IAuthenticationService AuthenticationService { get; }

    public IAvatarImageService AvatarImageService { get; }

    public AccountShellViewModel AccountShellViewModel { get; }

    public UserManagementViewModel UserManagementViewModel { get; }

    public IOperationLogRepository OperationLogRepository { get; }

    public IOpcUaEquipmentClient EquipmentClient { get; }
    public IEquipmentControlService ControlService { get; }
    public IoStatusViewModel IoStatus { get; }
    public ParameterSettingsViewModel ParameterSettings { get; }
    public IAlarmLogRepository AlarmLogRepository { get; }
    public IReadOnlyList<AlarmDefinition> AlarmDefinitions { get; }
    public IAlarmSignalSource AlarmSignalSource { get; }
    public IAlarmMonitorService AlarmMonitor { get; }
    public AlarmStatusViewModel AlarmStatus { get; }
    public AlarmNavigationService AlarmNavigation { get; }
    public PlcConnectionStatusViewModel PlcConnection { get; }

    public ControlViewModel ControlViewModel { get; }

    public IRecipePlcGateway RecipePlcGateway { get; }

    public ProcessViewModel ProcessViewModel { get; }

    public HistoryViewModel HistoryViewModel { get; }

    public Task StartAsync()
    {
        if (_isStarted)
        {
            return Task.CompletedTask;
        }

        _isStarted = true;
        return Task.WhenAll(_telemetrySource.StartAsync(_shutdown.Token),
            AlarmSignalSource.StartAsync(AlarmDefinitions, _shutdown.Token));
    }

    private void PlcConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e) => SyncPlcConnectionStatus();

    private void SyncPlcConnectionStatus()
    {
        if (_shutdown.IsCancellationRequested) return;
        ApplicationStatusViewModel.Instance.PlcConnectionState = PlcConnection.ConnectionState;
        ApplicationStatusViewModel.Instance.PlcConnectionText = PlcConnection.StatusText;
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        ProcessViewModel.Dispose();
        HistoryViewModel.LiveTrend.Dispose();
        HistoryViewModel.ProcessTrend.Dispose();
        _telemetrySource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _telemetrySource.SampleReceived -= OnTelemetrySample;
        AlarmSignalSource.DisposeAsync().AsTask().GetAwaiter().GetResult();
        EquipmentClient.DisposeAsync().AsTask().GetAwaiter().GetResult();
        IoStatus.Dispose();
        ParameterSettings.Dispose();
        ControlViewModel.Dispose();
        UserManagementViewModel.Dispose();
        ((EquipmentControlService)ControlService).Dispose();
        HistoryViewModel.OperationHistory.Dispose();
        PlcConnection.PropertyChanged -= PlcConnectionPropertyChanged;
        PlcConnection.Dispose();
        ApplicationStatusViewModel.Instance.PlcConnection = null;
        AlarmMonitor.Dispose();
        AlarmStatus.Dispose();
        HistoryViewModel.AlarmHistory.Dispose();
        ((SqliteAlarmLogRepository)AlarmLogRepository).DisposeAsync().AsTask().GetAwaiter().GetResult();
        ApplicationStatusViewModel.Instance.AlarmStatus = null;
        ProcessTrendRecorder.Changed -= OnTrendRecordingChanged;
        ProcessTrendRecorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
        ApplicationStatusViewModel.Instance.HistoryRecordingError = "";
        _shutdown.Dispose();
    }

    private void SeedSimulationHistory()
    {
        var now = DateTimeOffset.Now;
        OperationLogRepository.Add(new OperationLogRecord(
            now.AddMinutes(-12),
            "模拟操作员",
            "加热",
            "设置目标温度",
            true,
            false,
            string.Empty));
        OperationLogRepository.Add(new OperationLogRecord(
            now.AddHours(-5),
            "模拟操作员",
            "电源1",
            "启动电源",
            true,
            false,
            string.Empty));
        OperationLogRepository.Add(new OperationLogRecord(
            now.AddDays(-2),
            "模拟操作员",
            "前级阀",
            "打开阀门",
            false,
            true,
            "模拟互锁条件未满足"));

    }
}
