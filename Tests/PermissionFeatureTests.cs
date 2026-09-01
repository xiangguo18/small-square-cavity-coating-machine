using Small_square_cavity_coating_machine.Models.Recipes;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Services.History;
using Small_square_cavity_coating_machine.Services.Recipes;
using Small_square_cavity_coating_machine.Services.Security;
using Small_square_cavity_coating_machine.ViewModels;
using Small_square_cavity_coating_machine.ViewModels.Recipes;
using Small_square_cavity_coating_machine.ViewModels.Security;
using Xunit;

namespace SmallSquareCavityCoatingMachine.Tests;

public sealed class PermissionFeatureTests : IDisposable
{
    private readonly string _temporaryDirectory;
    private readonly string _databasePath;
    private readonly SqliteUserRepository _repository;

    public PermissionFeatureTests()
    {
        _temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "small-cavity-permission-tests",
            Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_temporaryDirectory, "users.db");
        _repository = new SqliteUserRepository(_databasePath, new PasswordHasher());
        _repository.Initialize();
    }

    [Fact]
    public void Initialize_SeedsProtectedAdministratorWithHashedPasswordAndAllPermissions()
    {
        var administrator = _repository.GetByUserName("SLKJ");

        Assert.NotNull(administrator);
        Assert.True(administrator.IsBuiltInAdministrator);
        Assert.Equal(PermissionCatalog.All.Count, administrator.Permissions.Count);
        Assert.True(_repository.VerifyPassword("slkj", "slkj123456"));

        var databaseContents = File.ReadAllText(_databasePath);
        Assert.DoesNotContain("slkj123456", databaseContents, StringComparison.Ordinal);
    }

    [Fact]
    public void Administrator_CannotBeDeletedOrRenamedAndCanChangePassword()
    {
        var administrator = _repository.GetByUserName("slkj")!;

        Assert.Throws<InvalidOperationException>(() => _repository.Delete(administrator.Id));

        var updated = _repository.Update(
            administrator.Id,
            "renamed-admin",
            "new-password",
            []);

        Assert.Equal("slkj", updated.UserName);
        Assert.Equal(PermissionCatalog.All.Count, updated.Permissions.Count);
        Assert.True(_repository.VerifyPassword("slkj", "new-password"));
        Assert.False(_repository.VerifyPassword("slkj", "slkj123456"));
    }

    [Fact]
    public void OrdinaryUser_CrudPersistsPermissionsAndBlankPasswordKeepsExistingHash()
    {
        var created = _repository.Create(
            "  Operator  ",
            "operator-password",
            [PermissionKey.SystemStatus, PermissionKey.HistoryRecords]);

        Assert.Equal("Operator", created.UserName);
        Assert.True(created.HasPermission(PermissionKey.SystemStatus));
        Assert.Throws<InvalidOperationException>(() =>
            _repository.Create("operator", "another-password", []));

        var updated = _repository.Update(
            created.Id,
            "LineOperator",
            null,
            [PermissionKey.ProcessRecipe]);

        Assert.Equal("Operator", updated.UserName);
        Assert.True(updated.HasPermission(PermissionKey.ProcessRecipe));
        Assert.False(updated.HasPermission(PermissionKey.SystemStatus));
        Assert.True(_repository.VerifyPassword("Operator", "operator-password"));

        _repository.Delete(updated.Id);
        Assert.Null(_repository.GetById(updated.Id));
    }

    [Fact]
    public void SystemCommands_RemainVisuallyEnabledAndAreGuardedWhenPermissionIsMissing()
    {
        var user = _repository.Create("viewer", "viewer-password", []);
        var session = new UserSession(_repository, "slkj");
        session.SetCurrentUser(user);
        var authorization = new AuthorizationService(session);
        var operationLogs = new InMemoryOperationLogRepository();
        var viewModel = new ControlViewModel(operationLogs, authorization);
        string? deniedMessage = null;
        authorization.AccessDenied += (_, e) => deniedMessage = e.Message;

        Assert.True(viewModel.ToggleArgonUpperValveCommand.CanExecute(null));
        viewModel.ToggleArgonUpperValveCommand.Execute(null);
        Assert.False(viewModel.ArgonUpperValveIsOpen);
        Assert.Equal("当前账号无此操作权限", deniedMessage);
        Assert.Empty(operationLogs.Query(DateTimeOffset.MinValue, DateTimeOffset.MaxValue));

        var authorizedUser = _repository.Update(
            user.Id,
            user.UserName,
            null,
            [PermissionKey.SystemStatus]);
        session.SetCurrentUser(authorizedUser);

        Assert.True(viewModel.ToggleArgonUpperValveCommand.CanExecute(null));
        viewModel.ToggleArgonUpperValveCommand.Execute(null);
        Assert.True(viewModel.ArgonUpperValveIsOpen);
    }

    [Fact]
    public void RecipeCommands_RemainVisuallyEnabledAndAreGuardedWhenPermissionIsMissing()
    {
        var user = _repository.Create("recipe-viewer", "viewer-password", []);
        var session = new UserSession(_repository, "slkj");
        session.SetCurrentUser(user);
        var authorization = new AuthorizationService(session);
        var importer = new CountingImporter();
        using var viewModel = new ProcessViewModel(
            importer,
            new StubDispatchService(),
            new StubGateway(),
            new StubRecipeDialogService(),
            new InMemoryOperationLogRepository(),
            ApplicationStatusViewModel.Instance,
            authorization);

        Assert.True(viewModel.ImportRecipeCommand.CanExecute(null));
        viewModel.ImportRecipeCommand.Execute(null);
        Assert.Equal(0, importer.ImportCount);

        var authorizedUser = _repository.Update(
            user.Id,
            user.UserName,
            null,
            [PermissionKey.ProcessRecipe]);
        session.SetCurrentUser(authorizedUser);

        Assert.True(viewModel.ImportRecipeCommand.CanExecute(null));
    }

    [Fact]
    public void UserManagementCommands_RemainVisuallyEnabledAndAreGuardedWhenPermissionIsMissing()
    {
        var viewer = _repository.Create("user-viewer", "viewer-password", []);
        var session = new UserSession(_repository);
        session.SetCurrentUser(viewer);
        var authorization = new AuthorizationService(session);
        var authentication = new AuthenticationService(
            _repository,
            session,
            new InMemoryOperationLogRepository());
        var viewModel = new UserManagementViewModel(
            _repository,
            session,
            authorization,
            authentication,
            new StubUserManagementDialogService());
        var selectedUser = viewModel.SelectedUser;
        var deniedCount = 0;
        authorization.AccessDenied += (_, _) => deniedCount++;

        Assert.True(viewModel.NewUserCommand.CanExecute(null));
        Assert.True(viewModel.SaveCommand.CanExecute(null));
        Assert.True(viewModel.DeleteCommand.CanExecute(null));

        viewModel.NewUserCommand.Execute(null);

        Assert.Equal(1, deniedCount);
        Assert.Same(selectedUser, viewModel.SelectedUser);
        Assert.False(viewModel.IsCreating);
    }

    [Fact]
    public void Guest_CannotCreateSaveDeleteOrModifyUsers()
    {
        var session = new UserSession(_repository);
        var authorization = new AuthorizationService(session);
        var authentication = new AuthenticationService(
            _repository,
            session,
            new InMemoryOperationLogRepository());
        var viewModel = new UserManagementViewModel(
            _repository,
            session,
            authorization,
            authentication,
            new StubUserManagementDialogService());
        var originalUsers = _repository.GetAll().Select(user => user.Id).ToArray();

        Assert.True(viewModel.IsGuest);
        Assert.True(viewModel.IsUserNameReadOnly);

        viewModel.NewUserCommand.Execute(null);
        Assert.False(viewModel.IsCreating);

        viewModel.UserName = "forbidden-user";
        viewModel.PendingPassword = "forbidden-password";
        viewModel.SaveCommand.Execute(null);
        viewModel.DeleteCommand.Execute(null);

        Assert.Null(_repository.GetByUserName("forbidden-user"));
        Assert.Equal(originalUsers, _repository.GetAll().Select(user => user.Id));
    }

    [Fact]
    public void OrdinaryUser_CanChangeOnlyOwnPassword()
    {
        var ordinary = _repository.Create("ordinary", "old-password", []);
        var another = _repository.Create("another", "another-password", []);
        var session = new UserSession(_repository);
        session.SetCurrentUser(ordinary);
        var authorization = new AuthorizationService(session);
        var authentication = new AuthenticationService(
            _repository,
            session,
            new InMemoryOperationLogRepository());
        var viewModel = new UserManagementViewModel(
            _repository,
            session,
            authorization,
            authentication,
            new StubUserManagementDialogService());

        viewModel.SelectedUser = viewModel.Users.Single(user => user.Id == ordinary.Id);
        Assert.True(viewModel.CanEditPassword);
        Assert.False(viewModel.CanEditUserName);
        Assert.False(viewModel.CanEditPermissions);

        viewModel.UserName = "attempted-rename";
        foreach (var option in viewModel.PermissionOptions)
        {
            option.IsAllowed = true;
        }

        viewModel.PendingPassword = "new-password";
        viewModel.SaveCommand.Execute(null);

        var unchangedAccount = _repository.GetById(ordinary.Id)!;
        Assert.Equal("ordinary", unchangedAccount.UserName);
        Assert.Empty(unchangedAccount.Permissions);
        Assert.True(_repository.VerifyPassword("ordinary", "new-password"));

        viewModel.SelectedUser = viewModel.Users.Single(user => user.Id == another.Id);
        Assert.False(viewModel.CanEditPassword);
        viewModel.PendingPassword = "forbidden-change";
        viewModel.SaveCommand.Execute(null);
        Assert.True(_repository.VerifyPassword("another", "another-password"));
        Assert.False(_repository.VerifyPassword("another", "forbidden-change"));
    }

    [Fact]
    public void DelegatedUserManager_CanCreateAndDeleteOthersButNotCurrentAccount()
    {
        var manager = _repository.Create(
            "manager",
            "manager-password",
            [PermissionKey.UserManagement]);
        var session = new UserSession(_repository);
        session.SetCurrentUser(manager);
        var authorization = new AuthorizationService(session);
        var authentication = new AuthenticationService(
            _repository,
            session,
            new InMemoryOperationLogRepository());
        var viewModel = new UserManagementViewModel(
            _repository,
            session,
            authorization,
            authentication,
            new StubUserManagementDialogService());

        Assert.True(viewModel.CanManageUsers);
        viewModel.NewUserCommand.Execute(null);
        viewModel.UserName = "managed-user";
        viewModel.PendingPassword = "managed-password";
        viewModel.PermissionOptions.Single(option => option.Key == PermissionKey.SystemStatus).IsAllowed = true;
        viewModel.SaveCommand.Execute(null);

        var managed = _repository.GetByUserName("managed-user")!;
        Assert.True(managed.HasPermission(PermissionKey.SystemStatus));
        viewModel.SelectedUser = viewModel.Users.Single(user => user.Id == managed.Id);
        viewModel.DeleteCommand.Execute(null);
        Assert.Null(_repository.GetById(managed.Id));

        viewModel.SelectedUser = viewModel.Users.Single(user => user.Id == manager.Id);
        Assert.False(viewModel.CanDelete);
        viewModel.DeleteCommand.Execute(null);
        Assert.NotNull(_repository.GetById(manager.Id));
    }

    [Fact]
    public void PendingPassword_CancelDiscardAndSaveControlLeavingCurrentUser()
    {
        var manager = _repository.Create(
            "guard-manager",
            "manager-password",
            [PermissionKey.UserManagement]);
        var other = _repository.Create("guard-other", "old-password", []);
        var session = new UserSession(_repository);
        session.SetCurrentUser(manager);
        var authorization = new AuthorizationService(session);
        var authentication = new AuthenticationService(
            _repository,
            session,
            new InMemoryOperationLogRepository());
        var dialogs = new StubUserManagementDialogService();
        var viewModel = new UserManagementViewModel(
            _repository,
            session,
            authorization,
            authentication,
            dialogs);
        viewModel.SelectedUser = viewModel.Users.Single(user => user.Id == other.Id);
        var managerItem = viewModel.Users.Single(user => user.Id == manager.Id);

        viewModel.PendingPassword = "cancelled-password";
        dialogs.PendingPasswordDecision = PendingPasswordDecision.Cancel;
        Assert.False(viewModel.TrySelectUser(managerItem));
        Assert.Equal(other.Id, viewModel.SelectedUser!.Id);
        Assert.Equal("cancelled-password", viewModel.PendingPassword);

        dialogs.PendingPasswordDecision = PendingPasswordDecision.Discard;
        Assert.True(viewModel.TrySelectUser(managerItem));
        Assert.Equal(manager.Id, viewModel.SelectedUser!.Id);
        Assert.Empty(viewModel.PendingPassword);
        Assert.True(_repository.VerifyPassword("guard-other", "old-password"));

        viewModel.SelectedUser = viewModel.Users.Single(user => user.Id == other.Id);
        viewModel.PendingPassword = "saved-password";
        dialogs.PendingPasswordDecision = PendingPasswordDecision.Save;
        Assert.True(viewModel.TrySelectUser(managerItem));
        Assert.True(_repository.VerifyPassword("guard-other", "saved-password"));
        Assert.Empty(viewModel.PendingPassword);
    }

    [Fact]
    public void PendingPassword_SaveFailurePreventsLeavingAndKeepsPassword()
    {
        var manager = _repository.Create(
            "new-user-manager",
            "manager-password",
            [PermissionKey.UserManagement]);
        var session = new UserSession(_repository);
        session.SetCurrentUser(manager);
        var authorization = new AuthorizationService(session);
        var authentication = new AuthenticationService(
            _repository,
            session,
            new InMemoryOperationLogRepository());
        var dialogs = new StubUserManagementDialogService
        {
            PendingPasswordDecision = PendingPasswordDecision.Save
        };
        var viewModel = new UserManagementViewModel(
            _repository,
            session,
            authorization,
            authentication,
            dialogs);

        viewModel.NewUserCommand.Execute(null);
        viewModel.PendingPassword = "password-without-name";

        Assert.False(viewModel.TryResolvePendingPasswordChange());
        Assert.True(viewModel.IsCreating);
        Assert.Equal("password-without-name", viewModel.PendingPassword);
    }

    [Fact]
    public void LoginAndLogout_ChangeSessionAndImmediatelyRevokeWritePermission()
    {
        var operationLogs = new InMemoryOperationLogRepository();
        var session = new UserSession(_repository);
        var authorization = new AuthorizationService(session);
        var authentication = new AuthenticationService(
            _repository,
            session,
            operationLogs);
        var controlViewModel = new ControlViewModel(operationLogs, authorization);

        Assert.False(session.IsAuthenticated);
        Assert.True(controlViewModel.StartSystemCommand.CanExecute(null));
        Assert.False(authentication.Login("slkj", "wrong-password").IsSuccessful);

        Assert.True(authentication.Login("slkj", "slkj123456").IsSuccessful);
        Assert.True(session.IsAuthenticated);
        Assert.True(controlViewModel.StartSystemCommand.CanExecute(null));

        authentication.Logout();

        Assert.False(session.IsAuthenticated);
        Assert.Equal("未登录", authorization.CurrentUserName);
        Assert.True(controlViewModel.StartSystemCommand.CanExecute(null));
        controlViewModel.StartSystemCommand.Execute(null);
        Assert.False(controlViewModel.SystemIsRunning);
    }

    [Fact]
    public void AccountShell_ShowsGuestStateAndAuthorizationNoticeAcrossLoginChanges()
    {
        var operationLogs = new InMemoryOperationLogRepository();
        var session = new UserSession(_repository);
        var authorization = new AuthorizationService(session);
        var authentication = new AuthenticationService(_repository, session, operationLogs);
        var shell = new AccountShellViewModel(
            authentication,
            new StubAccountDialogService(),
            authorization);

        Assert.True(shell.IsGuest);
        Assert.Equal("当前用户：未登录｜仅可查看", shell.GuestStatusText);

        Assert.False(authorization.TryAuthorize(PermissionKey.SystemStatus));
        Assert.True(shell.IsAuthorizationNoticeVisible);
        Assert.Equal("请先登录，当前为只读查看", shell.AuthorizationNoticeText);

        Assert.True(authentication.Login("slkj", "slkj123456").IsSuccessful);
        Assert.False(shell.IsGuest);

        authentication.Logout();
        Assert.True(shell.IsGuest);
    }

    [Fact]
    public void AccountButtons_OpenLoginOrProfileAndLogoutRequiresConfirmation()
    {
        var session = new UserSession(_repository);
        var authorization = new AuthorizationService(session);
        var authentication = new AuthenticationService(
            _repository,
            session,
            new InMemoryOperationLogRepository());
        var dialogs = new StubAccountDialogService();
        var shell = new AccountShellViewModel(authentication, dialogs, authorization);

        shell.OpenAccountCommand.Execute(null);
        Assert.Equal(1, dialogs.LoginCount);
        Assert.Equal(0, dialogs.ProfileCount);

        Assert.True(authentication.Login("slkj", "slkj123456").IsSuccessful);
        shell.OpenAccountCommand.Execute(null);
        Assert.Equal(1, dialogs.LoginCount);
        Assert.Equal(1, dialogs.ProfileCount);

        dialogs.ConfirmLogoutResult = false;
        shell.LogoutCommand.Execute(null);
        Assert.True(session.IsAuthenticated);
        Assert.Equal(1, dialogs.ConfirmLogoutCount);

        dialogs.ConfirmLogoutResult = true;
        shell.LogoutCommand.Execute(null);
        Assert.False(session.IsAuthenticated);
        Assert.Equal(2, dialogs.ConfirmLogoutCount);

        shell.LogoutCommand.Execute(null);
        Assert.Equal(2, dialogs.ConfirmLogoutCount);
        Assert.True(shell.IsAuthorizationNoticeVisible);
        Assert.Equal("当前尚未登录，无需退出", shell.AuthorizationNoticeText);
    }

    [Fact]
    public void CurrentUser_CanPersistAvatarAndChangePasswordAfterOldPasswordVerification()
    {
        var session = new UserSession(_repository);
        var authentication = new AuthenticationService(
            _repository,
            session,
            new InMemoryOperationLogRepository());
        Assert.True(authentication.Login("slkj", "slkj123456").IsSuccessful);

        byte[] avatarData = [1, 2, 3, 4, 5];
        Assert.True(authentication.UpdateCurrentAvatar(avatarData).IsSuccessful);
        Assert.Equal(avatarData, session.CurrentUser!.AvatarData);

        var wrongPasswordResult = authentication.ChangeCurrentPassword(
            "wrong-password",
            "updated-password");
        Assert.False(wrongPasswordResult.IsSuccessful);

        var changed = authentication.ChangeCurrentPassword(
            "slkj123456",
            "updated-password");
        Assert.True(changed.IsSuccessful);
        Assert.False(_repository.VerifyPassword("slkj", "slkj123456"));
        Assert.True(_repository.VerifyPassword("slkj", "updated-password"));

        var reopenedRepository = new SqliteUserRepository(_databasePath, new PasswordHasher());
        reopenedRepository.Initialize();
        Assert.Equal(avatarData, reopenedRepository.GetByUserName("slkj")!.AvatarData);

        Assert.True(authentication.UpdateCurrentAvatar(null).IsSuccessful);
        Assert.Null(session.CurrentUser!.AvatarData);
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, true);
        }
    }

    private sealed class CountingImporter : IRecipeExcelImporter
    {
        public int ImportCount { get; private set; }

        public RecipeImportResult Import(string filePath)
        {
            ImportCount++;
            return new RecipeImportResult([], []);
        }
    }

    private sealed class StubGateway : IRecipePlcGateway
    {
        public bool IsAvailable => true;

        public bool IsSimulated => true;

        public Task<RecipeGatewayPreflightResult> PreflightAsync(CancellationToken cancellationToken) =>
            Task.FromResult(RecipeGatewayPreflightResult.Ready);

        public Task SendLayerAsync(RecipeLayer layer, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task BeginRunAsync(RecipeRunRequest request, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task WaitForLayerCompletedAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CompleteRunAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubDispatchService : IRecipeDispatchService
    {
        public Task<RecipeRunResult> RunAsync(
            RecipeRunRequest request,
            IProgress<RecipeRunProgress> progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(RecipeRunResult.Completed(request.Layers.Count));
    }

    private sealed class StubRecipeDialogService : IRecipeUserDialogService
    {
        public string? SelectRecipeFile() => null;

        public bool ConfirmReplaceExistingRecipe() => true;

        public bool ConfirmClearRecipe() => true;

        public RecipeLayer? ShowNewLayerDialog(int nextSequence) => null;

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

    private sealed class StubAccountDialogService : IAccountDialogService
    {
        public int LoginCount { get; private set; }

        public int ProfileCount { get; private set; }

        public int ConfirmLogoutCount { get; private set; }

        public bool ConfirmLogoutResult { get; set; } = true;

        public void ShowLogin()
        {
            LoginCount++;
        }

        public void ShowProfile()
        {
            ProfileCount++;
        }

        public bool ConfirmLogout(string userName)
        {
            ConfirmLogoutCount++;
            return ConfirmLogoutResult;
        }
    }

    private sealed class StubUserManagementDialogService : IUserManagementDialogService
    {
        public PendingPasswordDecision PendingPasswordDecision { get; set; } =
            PendingPasswordDecision.Cancel;

        public bool ConfirmDelete(string userName) => true;

        public PendingPasswordDecision ConfirmPendingPassword(string userName, bool isCreating) =>
            PendingPasswordDecision;
    }
}
