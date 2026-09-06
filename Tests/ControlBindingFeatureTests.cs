using Small_square_cavity_coating_machine.Models.Equipment;
using Small_square_cavity_coating_machine.Models.Security;
using Small_square_cavity_coating_machine.Services;
using Small_square_cavity_coating_machine.Services.Alarms;
using Small_square_cavity_coating_machine.Services.Equipment;
using Small_square_cavity_coating_machine.Services.Security;
using Small_square_cavity_coating_machine.ViewModels;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Small_square_cavity_coating_machine.Tests;

public sealed class ControlBindingFeatureTests
{
    private sealed class ConfirmationStub : IConfirmationDialogService
    {
        public bool Result = true;
        public int Calls;
        public bool Confirm(string title, string message)
        {
            Calls++;
            return Result;
        }
    }

    private sealed class Authorization : IAuthorizationService
    {
        public bool Allowed = true;
        public bool IsBuiltInAdministrator { get; set; } = true;
        public string CurrentUserName => "slkj";
        public event EventHandler? AccessChanged { add { } remove { } }
        public event EventHandler<AuthorizationDeniedEventArgs>? AccessDenied { add { } remove { } }
        public bool CanOperate(PermissionKey permission) => Allowed;
        public bool TryAuthorize(PermissionKey permission) => Allowed;
    }

    [Fact]
    public void Embedded_control_definitions_match_confirmed_indices_without_duplicates()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var path = SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path);
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var controls = new SqliteEquipmentDefinitionRepository(path).LoadControls();

        Assert.Equal("Part_Command[982]", controls.Commands[982].Address);
        Assert.DoesNotContain(983, controls.Commands.Keys);
        Assert.Equal("Part_Command[21]", controls.Commands[21].Address);
        Assert.Equal("Part_Command[24]", controls.Commands[24].Address);
        Assert.Equal("Part_Data1[21]", controls.Data[21].Address);
        Assert.Equal("Part_Data_Set2[0]", controls.Data[21].SetAddress);
        Assert.Equal("Part_Data1[26]", controls.Data[26].Address);
        Assert.Equal("Part_Data_Set1[1]", controls.Data[1].SetAddress);
        Assert.Equal("Part_Data_Set1[4]", controls.Data[7].SetAddress);
        Assert.Equal("Part_Data_Set1[5]", controls.Data[8].SetAddress);
        Assert.Equal("Part_Data_Set1[6]", controls.Data[9].SetAddress);
        Assert.Equal("EQ_Interlock[10]", controls.Commands[27].InterlockAddress);
        Assert.Equal("EQ_Interlock[15]", controls.Commands[32].InterlockAddress);
        Assert.Equal("EQ_Interlock[6]", controls.Commands[6].InterlockAddress);
        Assert.Equal("EQ_Interlock[7]", controls.Commands[7].InterlockAddress);
        Assert.Equal("EQ_PassInterlock", controls.SystemCommands["PassInterlock"].CommandAddress);
        Assert.True(controls.SystemCommands["PassInterlock"].RequiresBuiltInAdministrator);
        var expectedStates = new Dictionary<int, int>
        {
            [0]=0, [1]=1, [2]=2, [3]=3, [4]=4, [5]=5, [6]=6, [7]=7,
            [11]=11, [12]=12, [13]=13, [14]=14, [15]=15, [16]=16,
            [17]=17, [18]=18, [19]=19, [20]=20, [21]=21, [22]=22, [23]=23, [24]=24, [25]=25
        };
        Assert.All(expectedStates, pair => Assert.Equal($"Part_State[{pair.Value}]", controls.Parts[pair.Key].StateAddress));
        Assert.DoesNotContain(controls.Parts.Keys, id => id is >= 100 and <= 110);
        Assert.Equal(5, controls.Commands[13].PartId);
        Assert.Equal(24, controls.Commands[15].PartId);
        Assert.Equal(25, controls.Commands[17].PartId);
        Assert.Equal(6, controls.Commands[21].PartId);
        Assert.Equal(7, controls.Commands[23].PartId);
        Assert.Equal([11,12,15,14,13,16], new[] {27,29,31,33,35,37}.Select(id => controls.Commands[id].PartId));
        var partStateAddresses = controls.Parts.Values.Select(p => p.StateAddress).Where(a => a.Length > 0).ToArray();
        Assert.Equal(partStateAddresses.Length, partStateAddresses.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(controls.Addresses.Count, controls.Addresses.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(controls.Addresses, a => a.StartsWith(EquipmentGroups.PartCommandEnable, StringComparison.Ordinal));
        Assert.DoesNotContain(controls.Addresses, a => a.EndsWith("_En", StringComparison.Ordinal));
        Assert.All(controls.SystemCommands.Values, s => Assert.Equal("", s.EnableAddress));
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Duplicate_part_state_address_is_rejected_instead_of_guessed()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var path = SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Part SET Node='Part_State[0]' WHERE Id=1";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidOperationException>(() => new SqliteEquipmentDefinitionRepository(path).LoadControls());
    }

    [Fact]
    public async Task Execute_part_command_honors_interlock_and_confirms_observed_state()
    {
        await using var harness = await ControlHarness.CreateAsync();
        var first = await harness.Service.ExecutePartCommandAsync(13, "样品挡板", "开启");
        var second = await harness.Service.ExecutePartCommandAsync(14, "样品挡板", "关闭");

        Assert.Equal(ControlWriteOutcome.Confirmed, first.Outcome);
        Assert.Equal(ControlWriteOutcome.Confirmed, second.Outcome);
        Assert.Collection(harness.Session.ControlWrites.Take(2),
            write => { Assert.Equal((EquipmentGroups.PartCommand, 13), (write.Group, write.Index)); Assert.Equal(true, write.Value); },
            write => { Assert.Equal((EquipmentGroups.PartCommand, 14), (write.Group, write.Index)); Assert.Equal(true, write.Value); });
        // 脉冲命令不再回读命令位，程序依据 Part_State 反馈确认（样例挡板关闭后 state[5]=bit0）。
        Assert.Equal((ushort)1, ((ushort[])harness.Session.Values[EquipmentGroups.PartState])[5]);
        Assert.DoesNotContain(harness.Session.ControlWrites, write => write.Value is false);

        ((bool[])harness.Session.Values[EquipmentGroups.Interlock])[10] = false;
        harness.Session.Send(EquipmentGroups.Interlock);
        var blocked = await harness.Service.ExecutePartCommandAsync(27, "Ar气阀", "开启");
        Assert.Equal(ControlWriteOutcome.Rejected, blocked.Outcome);

        Assert.Equal(ControlWriteOutcome.Confirmed, (await harness.Service.SetPassInterlockAsync(true)).Outcome);
        Assert.Equal(ControlWriteOutcome.Confirmed,
            (await harness.Service.ExecutePartCommandAsync(27, "Ar气阀", "开启")).Outcome);
        Assert.Contains(harness.Session.ControlWrites,
            write => write.Group == EquipmentGroups.PassInterlock && write.Index is null && Equals(write.Value, true));
        Assert.Contains(harness.Session.ControlWrites,
            write => write.Group == EquipmentGroups.PartCommand && write.Index == 27 && Equals(write.Value, true));
    }

    [Fact]
    public async Task Setpoints_are_database_driven_and_all_confirmed_state_words_drive_feedback()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        Assert.Equal(ControlWriteOutcome.Confirmed,
            (await harness.Service.WriteSetpointAsync(1, 0, PermissionKey.SystemStatus)).Outcome);
        Assert.Equal(ControlWriteOutcome.Confirmed,
            (await harness.Service.WriteSetpointAsync(1, 42.5, PermissionKey.SystemStatus)).Outcome);
        Assert.Equal([0f, 42.5f], harness.Session.ControlWrites
            .Where(w => w.Group == EquipmentGroups.PartDataSet1 && w.Index == 1)
            .Select(w => Assert.IsType<float>(w.Value)).ToArray());

        var states = (ushort[])harness.Session.Values[EquipmentGroups.PartState];
        states[0] = 1 << 1;
        states[1] = (1 << 1) | (1 << 7);
        states[2] = 1 << 1;
        states[4] = 1 << 1;
        states[5] = 1 << 1;
        states[6] = (1 << 1) | (1 << 7);
        states[7] = 1 << 0;
        states[11] = 1 << 1;
        states[12] = 1 << 0;
        states[13] = 1 << 1;
        states[14] = 1 << 7;
        states[15] = 1 << 1;
        states[16] = 1 << 0;
        states[17] = 1 << 1;
        states[18] = 1 << 7;
        states[19] = 1 << 1;
        states[20] = 1 << 1;
        states[21] = 1 << 0;
        states[24] = 1 << 1;
        states[25] = 1 << 7;
        harness.Session.Send(EquipmentGroups.PartState);
        Assert.True(viewModel.DryPumpIsRunning);
        Assert.True(viewModel.TurboPumpIsRunning);
        Assert.True(viewModel.TurboPumpIsFaulted);
        Assert.True(viewModel.ApcIsOpen);
        Assert.True(viewModel.SampleStageIsRunning);
        Assert.True(viewModel.SampleShutterIsOn);
        Assert.True(viewModel.Power1IsRunning);
        Assert.True(viewModel.Power1IsFaulted);
        Assert.False(viewModel.Power2IsRunning);
        Assert.True(viewModel.ArgonLowerValveIsOpen);
        Assert.False(viewModel.ArgonUpperValveIsOpen);
        Assert.True(viewModel.NitrogenLowerValveIsOpen);
        Assert.True(viewModel.NitrogenUpperValveIsFaulted);
        Assert.True(viewModel.OxygenLowerValveIsOpen);
        Assert.False(viewModel.OxygenUpperValveIsOpen);
        Assert.True(viewModel.ForelineGaugeIsReadingEnabled);
        Assert.True(viewModel.HighVacuumGaugeIsFaulted);
        Assert.True(viewModel.FilmGaugeIsReadingEnabled);
        Assert.True(viewModel.FilmGaugeValveIsOpen);
        Assert.False(viewModel.BypassValveIsOpen);
        Assert.True(viewModel.Target1ShutterIsOn);
        Assert.True(viewModel.Target2ShutterIsFaulted);

        states[11] = (1 << 0) | (1 << 1); // contradictory position bits are unknown, never green
        harness.Session.Send(EquipmentGroups.PartState);
        Assert.False(viewModel.ArgonLowerValveIsOpen);
        harness.Session.Send(EquipmentGroups.PartState, Opc.Ua.StatusCodes.BadCommunicationError);
        Assert.False(viewModel.SampleStageIsRunning);
        Assert.False(viewModel.Power1IsRunning);
        Assert.False(viewModel.ForelineGaugeIsReadingEnabled);
        Assert.False(viewModel.Target1ShutterIsOn);

        harness.Session.Values[EquipmentGroups.PartState] = new ushort[10];
        harness.Session.Send(EquipmentGroups.PartState);
        Assert.False(viewModel.Target2ShutterIsOn);
        Assert.False(viewModel.Target2ShutterIsFaulted);
    }

    [Fact]
    public async Task Mfc_commands_write_confirmed_values_to_the_three_configured_setpoint_elements()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        viewModel.SetArgonFlowCommand.Execute(12.3d);
        await AlarmArrayConnectionTests.Until(() => harness.Session.ControlWrites.Count >= 1);
        await AlarmArrayConnectionTests.Until(() => !harness.Client.Snapshot().IsWriting);
        viewModel.SetNitrogenFlowCommand.Execute(23.4d);
        await AlarmArrayConnectionTests.Until(() => harness.Session.ControlWrites.Count >= 2);
        await AlarmArrayConnectionTests.Until(() => !harness.Client.Snapshot().IsWriting);
        viewModel.SetOxygenFlowCommand.Execute(34.5d);
        await AlarmArrayConnectionTests.Until(() => harness.Session.ControlWrites.Count >= 3);
        await AlarmArrayConnectionTests.Until(() => !harness.Client.Snapshot().IsWriting);

        Assert.Equal(
            [
                (EquipmentGroups.PartDataSet1, (int?)4, 12.3f),
                (EquipmentGroups.PartDataSet1, (int?)5, 23.4f),
                (EquipmentGroups.PartDataSet1, (int?)6, 34.5f)
            ],
            harness.Session.ControlWrites.Select(write =>
                (write.Group, write.Index, Assert.IsType<float>(write.Value))).ToArray());
        await AlarmArrayConnectionTests.Until(() => Math.Abs(viewModel.OxygenSetpointFlow - 34.5d) < 0.001d);
        Assert.Equal(12.3d, viewModel.ArgonSetpointFlow, 3);
        Assert.Equal(23.4d, viewModel.NitrogenSetpointFlow, 3);

        var writeCount = harness.Session.ControlWrites.Count;
        Assert.Equal(ControlWriteOutcome.Rejected,
            (await harness.Service.WriteSetpointAsync(7, 500.1d, PermissionKey.SystemStatus)).Outcome);
        Assert.Equal(writeCount, harness.Session.ControlWrites.Count);

        harness.Authorization.Allowed = false;
        viewModel.SetArgonFlowCommand.Execute(40d);
        await Task.Delay(100);
        Assert.Equal(writeCount, harness.Session.ControlWrites.Count);
    }

    [Fact]
    public async Task Workflow_buttons_light_on_confirm_with_mutually_exclusive_selection()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        viewModel.StartVacuumCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.VacuumingIsSelected);
        Assert.False(viewModel.VentingIsSelected);
        Assert.False(viewModel.PressureHoldingIsSelected);

        viewModel.BreakVacuumCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.VentingIsSelected);
        Assert.False(viewModel.VacuumingIsSelected);
        Assert.False(viewModel.PressureHoldingIsSelected);

        // 流程命令无独立状态反馈，只报告“命令已发送”，并保持同组互斥（目标写 1、其余写 0）。
        var workflow = harness.Session.ArrayWrites
            .Where(a => a.Group == EquipmentGroups.PartCommand)
            .SelectMany(a => a.Mutations).Where(m => m.Index is 980 or 981 or 982).ToArray();
        Assert.Contains(workflow, m => m.Index == 980 && Equals(m.Value, true));
        Assert.Contains(workflow, m => m.Index == 980 && Equals(m.Value, false));
        Assert.Contains(workflow, m => m.Index == 981 && Equals(m.Value, true));
        Assert.Contains(workflow, m => m.Index == 981 && Equals(m.Value, false));
        Assert.Contains(workflow, m => m.Index == 982 && Equals(m.Value, false));
    }

    [Fact]
    public async Task System_control_buttons_light_on_confirm_with_mutual_exclusion()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        viewModel.StartSystemCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.SystemIsRunning);
        Assert.False(viewModel.SystemIsStopped);
        Assert.False(viewModel.SystemResetIsActive);
        Assert.Contains(harness.Session.ControlWrites, w => w.Group == "EQ_Start" && Equals(w.Value, true));
        Assert.Contains(harness.Session.ControlWrites, w => w.Group == "EQ_Stop" && Equals(w.Value, false));

        viewModel.StopSystemCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.SystemIsStopped);
        Assert.False(viewModel.SystemIsRunning);
        Assert.False(viewModel.SystemResetIsActive);

        // 复位：点亮复位灯，并清空运行模式/真空控制/样品台全部按钮灯。
        viewModel.SelectManualModeCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.ManualModeIsSelected);
        viewModel.ResetSystemCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.SystemResetIsActive);
        Assert.False(viewModel.SystemIsRunning);
        Assert.False(viewModel.SystemIsStopped);
        Assert.False(viewModel.AutomaticModeIsSelected);
        Assert.False(viewModel.SemiAutomaticModeIsSelected);
        Assert.False(viewModel.ManualModeIsSelected);
        Assert.False(viewModel.VacuumingIsSelected);
        Assert.False(viewModel.VentingIsSelected);
        Assert.False(viewModel.PressureHoldingIsSelected);
        Assert.False(viewModel.SampleStageIsForwardRunning);
        Assert.False(viewModel.SampleStageIsReverseRunning);
        Assert.False(viewModel.SampleStageIsStopped);
    }

    [Fact]
    public async Task Mode_buttons_light_on_confirm_with_mutual_exclusion()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        viewModel.SelectManualModeCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.ManualModeIsSelected);
        Assert.False(viewModel.AutomaticModeIsSelected);
        Assert.False(viewModel.SemiAutomaticModeIsSelected);
        Assert.Contains(harness.Session.ControlWrites, w => w.Group == "EQ_Manual" && Equals(w.Value, true));
        Assert.Contains(harness.Session.ControlWrites, w => w.Group == "EQ_Auto" && Equals(w.Value, false));

        viewModel.SelectSemiAutomaticModeCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.SemiAutomaticModeIsSelected);
        Assert.False(viewModel.ManualModeIsSelected);
        Assert.False(viewModel.AutomaticModeIsSelected);
    }

    [Fact]
    public async Task Sample_stage_direction_buttons_light_on_confirm_with_mutual_exclusion()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        viewModel.StartSampleStageForwardCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.SampleStageIsForwardRunning);
        Assert.False(viewModel.SampleStageIsReverseRunning);
        Assert.False(viewModel.SampleStageIsStopped);
        Assert.False(viewModel.SampleStageIsRunning); // 物理运行灯仍由 Part_State[4] 反馈驱动，本轮未推送反馈

        viewModel.StopSampleStageCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.SampleStageIsStopped);
        Assert.False(viewModel.SampleStageIsForwardRunning);
        Assert.False(viewModel.SampleStageIsReverseRunning);

        var stage = harness.Session.ArrayWrites
            .Where(a => a.Group == EquipmentGroups.PartCommand)
            .SelectMany(a => a.Mutations).Where(m => m.Index is 10 or 11 or 12).ToArray();
        Assert.Contains(stage, m => m.Index == 10 && Equals(m.Value, true));
        Assert.Contains(stage, m => m.Index == 10 && Equals(m.Value, false));
        Assert.Contains(stage, m => m.Index == 12 && Equals(m.Value, true));
        Assert.Contains(stage, m => m.Index == 11 && Equals(m.Value, false));
    }

    [Fact]
    public async Task Disconnect_clears_command_selection_lights_fail_closed()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        viewModel.StartSystemCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.SystemIsRunning);
        viewModel.SelectManualModeCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.ManualModeIsSelected);
        viewModel.StartSampleStageForwardCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => viewModel.SampleStageIsForwardRunning);

        harness.Session.Failure.TrySetException(new InvalidOperationException("connection lost"));
        await AlarmArrayConnectionTests.Until(() => !harness.Client.Snapshot().IsConnected);
        await AlarmArrayConnectionTests.Until(() => !viewModel.SystemIsRunning);
        Assert.False(viewModel.SystemIsRunning);
        Assert.False(viewModel.SystemIsStopped);
        Assert.False(viewModel.SystemResetIsActive);
        Assert.False(viewModel.AutomaticModeIsSelected);
        Assert.False(viewModel.ManualModeIsSelected);
        Assert.False(viewModel.SampleStageIsForwardRunning);
    }

    [Fact]
    public async Task Component_toggle_chooses_open_or_close_from_real_feedback_without_optimistic_color()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());
        var states = (ushort[])harness.Session.Values[EquipmentGroups.PartState];

        states[5] = 1 << 1;
        harness.Session.Send(EquipmentGroups.PartState);
        Assert.True(viewModel.SampleShutterIsOn);
        viewModel.ToggleSampleShutterCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() => harness.Session.ArrayWrites.Any(a => a.Mutations.Any(m => m.Index == 14)));
        await AlarmArrayConnectionTests.Until(() => !harness.Client.Snapshot().IsWriting);
        Assert.True(viewModel.SampleShutterIsOn); // waits for Part_State, never follows command success optimistically

        states[5] = 1 << 0;
        harness.Session.Send(EquipmentGroups.PartState);
        viewModel.ToggleSampleShutterCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() =>
            harness.Session.ArrayWrites.Any(a => a.Mutations.Any(m => m.Index == 13 && Equals(m.Value, true))));
        Assert.False(viewModel.SampleShutterIsOn);
        // 互斥：样品挡板开/关地址对互斥（开启时对侧写 0、目标写 1）。
        var shutter = harness.Session.ArrayWrites.SelectMany(a => a.Mutations).Where(m => m.Index is 13 or 14).ToArray();
        Assert.Contains(shutter, m => m.Index == 13 && Equals(m.Value, false));
        Assert.Contains(shutter, m => m.Index == 14 && Equals(m.Value, false));
        Assert.Contains(shutter, m => m.Index == 13 && Equals(m.Value, true));
        Assert.Contains(shutter, m => m.Index == 14 && Equals(m.Value, true));
    }

    [Fact]
    public async Task Simulated_apc_position_updates_the_same_confirmed_state_used_by_the_view_model()
    {
        using var temp = new AlarmFeatureTests.TemporaryDirectory();
        var path = SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path);
        var parameters = new SqliteEquipmentDefinitionRepository(path).LoadParameters();
        var factory = new SimulatedEquipmentSessionFactory(parameters);
        await using var session = await factory.ConnectAsync(new OpcUaAlarmOptions(), CancellationToken.None);

        await session.WriteElementAsync(EquipmentGroups.PartDataSet1, 1, 0f, CancellationToken.None);
        var closed = Assert.IsType<ushort[]>((await session.ReadGroupAsync(
            EquipmentGroups.PartState, null, CancellationToken.None)).Value);
        Assert.Equal((ushort)1, closed[2]);

        await session.WriteElementAsync(EquipmentGroups.PartDataSet1, 1, 37.5f, CancellationToken.None);
        var open = Assert.IsType<ushort[]>((await session.ReadGroupAsync(
            EquipmentGroups.PartState, null, CancellationToken.None)).Value);
        Assert.Equal((ushort)2, open[2]);
    }

    [Fact]
    public async Task System_control_buttons_deassert_sibling_command_addresses()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        viewModel.StartSystemCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() =>
            harness.Session.ControlWrites.Any(w => w.Group == "EQ_Start" && Equals(w.Value, true))
            && !harness.Client.Snapshot().IsWriting);
        Assert.Contains(harness.Session.ControlWrites, w => w.Group == "EQ_Stop" && Equals(w.Value, false));
        Assert.Contains(harness.Session.ControlWrites, w => w.Group == "EQ_Reset" && Equals(w.Value, false));

        viewModel.SelectSemiAutomaticModeCommand.Execute(null);
        await AlarmArrayConnectionTests.Until(() =>
            harness.Session.ControlWrites.Any(w => w.Group == "EQ_Semi" && Equals(w.Value, true))
            && !harness.Client.Snapshot().IsWriting);
        Assert.Contains(harness.Session.ControlWrites, w => w.Group == "EQ_Auto" && Equals(w.Value, false));
        Assert.Contains(harness.Session.ControlWrites, w => w.Group == "EQ_Manual" && Equals(w.Value, false));
    }

    [Fact]
    public async Task Apc_position_setpoint_links_to_open_close_mutual_exclusion()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        viewModel.SetApcPositionCommand.Execute(0d);
        await AlarmArrayConnectionTests.Until(() =>
            harness.Session.ControlWrites.Any(w => w.Group == EquipmentGroups.PartDataSet1 && w.Index == 1
                && w.Value is float f && f == 0f)
            && !harness.Client.Snapshot().IsWriting);
        var apc1 = harness.Session.ArrayWrites.SelectMany(a => a.Mutations).Where(m => m.Index is 6 or 7).ToArray();
        Assert.Contains(apc1, m => m.Index == 6 && Equals(m.Value, false));
        Assert.Contains(apc1, m => m.Index == 7 && Equals(m.Value, true));

        viewModel.SetApcPositionCommand.Execute(50d);
        await AlarmArrayConnectionTests.Until(() =>
            harness.Session.ControlWrites.Any(w => w.Group == EquipmentGroups.PartDataSet1 && w.Index == 1
                && w.Value is float g && g == 50f)
            && !harness.Client.Snapshot().IsWriting);
        var apc2 = harness.Session.ArrayWrites.SelectMany(a => a.Mutations).Where(m => m.Index is 6 or 7).ToArray();
        Assert.Contains(apc2, m => m.Index == 7 && Equals(m.Value, false));
        Assert.Contains(apc2, m => m.Index == 6 && Equals(m.Value, true));
    }

    [Fact]
    public async Task Apc_position_rejects_out_of_range_without_writes()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        var before = harness.Session.ControlWrites.Count;
        viewModel.SetApcPositionCommand.Execute(-5d);
        Assert.Equal("APC开度范围：0～100 %", viewModel.ControlStatusText);
        viewModel.SetApcPositionCommand.Execute(150d);
        Assert.Equal("APC开度范围：0～100 %", viewModel.ControlStatusText);
        Assert.Equal(before, harness.Session.ControlWrites.Count);
    }

    [Fact]
    public async Task Apc_position_open_close_respect_interlock_fail_closed()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());
        var interlocks = (bool[])harness.Session.Values[EquipmentGroups.Interlock];

        interlocks[6] = false;
        harness.Session.Send(EquipmentGroups.Interlock);
        viewModel.SetApcPositionCommand.Execute(50d);
        await AlarmArrayConnectionTests.Until(() => viewModel.ControlStatusText.Contains("互锁"));
        Assert.DoesNotContain(harness.Session.ArrayWrites.SelectMany(a => a.Mutations),
            m => m.Index == 6 && Equals(m.Value, true));
        Assert.DoesNotContain(harness.Session.ControlWrites, w => w.Group == EquipmentGroups.PartDataSet1 && w.Index == 1);

        interlocks[6] = true;
        interlocks[7] = false;
        harness.Session.Send(EquipmentGroups.Interlock);
        viewModel.SetApcPositionCommand.Execute(0d);
        await AlarmArrayConnectionTests.Until(() => viewModel.ControlStatusText.Contains("互锁"));
        Assert.DoesNotContain(harness.Session.ArrayWrites.SelectMany(a => a.Mutations),
            m => m.Index == 7 && Equals(m.Value, true));
        Assert.DoesNotContain(harness.Session.ControlWrites, w => w.Group == EquipmentGroups.PartDataSet1 && w.Index == 1);
    }

    [Fact]
    public async Task Confirmation_aborts_apc_position_setpoint_when_declined()
    {
        await using var harness = await ControlHarness.CreateAsync();
        var confirmation = new ConfirmationStub { Result = false };
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher(), confirmation);

        var positionBefore = viewModel.ApcPositionSetpoint;
        viewModel.SetApcPositionCommand.Execute(50d);
        Assert.Equal(1, confirmation.Calls);
        Assert.Equal(positionBefore, viewModel.ApcPositionSetpoint);
        Assert.Empty(harness.Session.ControlWrites);
    }

    [Fact]
    public async Task Confirmation_aborts_auto_mode_when_declined()
    {
        await using var harness = await ControlHarness.CreateAsync();
        var confirmation = new ConfirmationStub { Result = false };
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher(), confirmation);

        var writesBefore = harness.Session.ControlWrites.Count;
        viewModel.SelectAutomaticModeCommand.Execute(null);
        Assert.Equal(1, confirmation.Calls);
        Assert.False(viewModel.AutomaticModeIsSelected);
        Assert.Equal(writesBefore, harness.Session.ControlWrites.Count);
    }

    [Fact]
    public async Task Confirmation_aborts_vacuum_workflow_when_declined()
    {
        await using var harness = await ControlHarness.CreateAsync();
        var confirmation = new ConfirmationStub { Result = false };
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher(), confirmation);

        var writesBefore = harness.Session.ControlWrites.Count;
        viewModel.StartVacuumCommand.Execute(null);
        viewModel.BreakVacuumCommand.Execute(null);
        viewModel.HoldPressureCommand.Execute(null);
        Assert.Equal(3, confirmation.Calls);
        Assert.Equal(writesBefore, harness.Session.ControlWrites.Count);
    }

    [Fact]
    public async Task Confirmation_only_asks_when_opening_vent_or_turbo_pump()
    {
        await using var harness = await ControlHarness.CreateAsync();
        var confirmation = new ConfirmationStub { Result = false };
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, confirmation);

        // 放气阀：当前关闭 → 打开时弹窗（取消则不动）
        viewModel.LowerForelineValveIsOpen = false;
        viewModel.ToggleLowerForelineValveCommand.Execute(null);
        Assert.Equal(1, confirmation.Calls);
        Assert.False(viewModel.LowerForelineValveIsOpen);

        // 放气阀：当前打开 → 关闭时不弹窗
        confirmation.Calls = 0;
        viewModel.LowerForelineValveIsOpen = true;
        viewModel.ToggleLowerForelineValveCommand.Execute(null);
        Assert.Equal(0, confirmation.Calls);
        Assert.False(viewModel.LowerForelineValveIsOpen);

        // 分子泵：当前停止 → 启动时弹窗（取消则不动）
        confirmation.Calls = 0;
        viewModel.TurboPumpIsRunning = false;
        viewModel.ToggleTurboPumpCommand.Execute(null);
        Assert.Equal(1, confirmation.Calls);
        Assert.False(viewModel.TurboPumpIsRunning);

        // 分子泵：当前运行 → 停止时不弹窗
        confirmation.Calls = 0;
        viewModel.TurboPumpIsRunning = true;
        viewModel.ToggleTurboPumpCommand.Execute(null);
        Assert.Equal(0, confirmation.Calls);
        Assert.False(viewModel.TurboPumpIsRunning);
    }

    [Fact]
    public async Task Control_points_initialize_when_server_omits_part_command_enable_and_engine_enable_nodes()
    {
        await using var harness = await ControlHarness.CreateAsync();
        Assert.True(harness.Client.Snapshot().IsConnected);
        Assert.DoesNotContain(harness.Session.Subscribed, g => g == EquipmentGroups.PartCommandEnable);
        Assert.DoesNotContain(harness.Session.Subscribed, g => g.EndsWith("_En", StringComparison.Ordinal));
        Assert.True(harness.Client.Snapshot().Groups[EquipmentGroups.PartCommand].IsReady);
        Assert.True(harness.Client.Snapshot().Groups[EquipmentGroups.Interlock].IsReady);
    }

    [Fact]
    public async Task Sixteen_device_interlocks_gate_open_close_individually_without_swapping()
    {
        await using var harness = await ControlHarness.CreateAsync();
        var pairs = new (int OpenCmd, int OpenInt, int CloseCmd, int CloseInt)[]
        {
            (41, 0, 42, 1),   // 粗抽阀
            (43, 2, 44, 3),   // 前级阀
            (39, 4, 40, 5),   // 薄膜规前级阀
            (6, 6, 7, 7),     // 插板阀
            (45, 8, 46, 9),   // 破空阀
            (27, 10, 28, 11), // MFC Ar
            (35, 12, 36, 13), // MFC N₂
            (31, 14, 32, 15), // MFC O₂
        };
        foreach (var (openCmd, openInt, closeCmd, closeInt) in pairs)
        {
            var interlocks = (bool[])harness.Session.Values[EquipmentGroups.Interlock];
            Array.Fill(interlocks, true);
            interlocks[openInt] = false;
            harness.Session.Send(EquipmentGroups.Interlock);
            Assert.Equal(ControlWriteOutcome.Rejected,
                (await harness.Service.ExecutePartCommandAsync(openCmd, "设备", "开启")).Outcome);

            Array.Fill(interlocks, true);
            interlocks[closeInt] = false;
            harness.Session.Send(EquipmentGroups.Interlock);
            Assert.Equal(ControlWriteOutcome.Confirmed,
                (await harness.Service.ExecutePartCommandAsync(openCmd, "设备", "开启")).Outcome); // 打开不受关闭互锁影响

            Array.Fill(interlocks, true);
            interlocks[closeInt] = false;
            harness.Session.Send(EquipmentGroups.Interlock);
            Assert.Equal(ControlWriteOutcome.Rejected,
                (await harness.Service.ExecutePartCommandAsync(closeCmd, "设备", "关闭")).Outcome);

            Array.Fill(interlocks, true);
            interlocks[openInt] = false;
            harness.Session.Send(EquipmentGroups.Interlock);
            Assert.Equal(ControlWriteOutcome.Confirmed,
                (await harness.Service.ExecutePartCommandAsync(closeCmd, "设备", "关闭")).Outcome); // 关闭不受打开互锁影响
        }
    }

    [Fact]
    public async Task Part_command_state_confirmation_reports_success_fault_and_timeout()
    {
        await using var harness = await ControlHarness.CreateAsync();
        // 成功：命令写入后 Part_State 到位 → Confirmed，且不再回读命令位（无 5 秒级等待）。
        Assert.Equal(ControlWriteOutcome.Confirmed,
            (await harness.Service.ExecutePartCommandAsync(13, "样品挡板", "开启")).Outcome);

        // 故障：Part_State bit7 → Rejected（故障提示）。
        harness.Session.ApplyFeedback = false;
        var states = (ushort[])harness.Session.Values[EquipmentGroups.PartState];
        states[5] = (ushort)(1 << 7);
        harness.Session.Send(EquipmentGroups.PartState);
        var snapshot = harness.Client.Snapshot();
        var fault = await harness.Client.WriteControlAsync(new ControlWriteRequest(
            "Part_Command[13]", true, snapshot.Epoch, "样品挡板", "开启", PermissionKey.SystemStatus,
            Confirm: ControlConfirm.State, StateAddress: "Part_State[5]", ExpectOpen: true, ConfirmTimeoutMs: 200));
        Assert.Equal(ControlWriteOutcome.Rejected, fault.Outcome);
        Assert.Contains("状态故障", fault.Message);

        // 超时：状态未变化 → Unknown（已发送但未确认）。
        states[5] = (ushort)(1 << 0);
        harness.Session.Send(EquipmentGroups.PartState);
        snapshot = harness.Client.Snapshot();
        var timeout = await harness.Client.WriteControlAsync(new ControlWriteRequest(
            "Part_Command[13]", true, snapshot.Epoch, "样品挡板", "开启", PermissionKey.SystemStatus,
            Confirm: ControlConfirm.State, StateAddress: "Part_State[5]", ExpectOpen: true, ConfirmTimeoutMs: 200));
        Assert.Equal(ControlWriteOutcome.Unknown, timeout.Outcome);
        Assert.Contains("状态未确认", timeout.Message);
    }

    [Fact]
    public async Task Ui_does_not_flip_before_state_feedback_arrives()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());
        var states = (ushort[])harness.Session.Values[EquipmentGroups.PartState];
        states[5] = (ushort)(1 << 0); // 关闭
        harness.Session.Send(EquipmentGroups.PartState);
        Assert.False(viewModel.SampleShutterIsOn);

        viewModel.ToggleSampleShutterCommand.Execute(null); // 发送打开命令
        await AlarmArrayConnectionTests.Until(() => !harness.Client.Snapshot().IsWriting);
        // 命令已确认但状态事件尚未推送，界面不得提前变绿。
        Assert.False(viewModel.SampleShutterIsOn);

        states[5] = (ushort)(1 << 1); // PLC 反馈到位
        harness.Session.Send(EquipmentGroups.PartState);
        Assert.True(viewModel.SampleShutterIsOn);
    }

    [Fact]
    public async Task Toggle_emits_single_array_batch_write_and_one_audit()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());

        viewModel.ToggleSampleShutterCommand.Execute(null); // 开启：断言13、互斥14
        await AlarmArrayConnectionTests.Until(() =>
            harness.Session.ArrayWrites.Any(a => a.Group == EquipmentGroups.PartCommand)
            && !harness.Client.Snapshot().IsWriting);

        var toggle = Assert.Single(harness.Session.ArrayWrites, a => a.Group == EquipmentGroups.PartCommand);
        Assert.Equal(false, toggle.Mutations.Single(m => m.Index == 14).Value);
        Assert.Equal(true, toggle.Mutations.Single(m => m.Index == 13).Value);
        // 一次点击 = 一次批量数组写入 + 一条审计记录（互斥+断言合并）。
        Assert.Single(harness.Runtime.All);
    }

    [Fact]
    public async Task Interlocked_toggle_with_interlock_false_writes_nothing()
    {
        await using var harness = await ControlHarness.CreateAsync();
        using var viewModel = new ControlViewModel(harness.Runtime, harness.Authorization, harness.Service,
            new AlarmFeatureTests.InlineDispatcher());
        var interlocks = (bool[])harness.Session.Values[EquipmentGroups.Interlock];
        interlocks[8] = false; // 放气阀打开互锁 EQ_Interlock[8]
        harness.Session.Send(EquipmentGroups.Interlock);

        viewModel.ToggleLowerForelineValveCommand.Execute(null); // 打开放气阀
        await AlarmArrayConnectionTests.Until(() => viewModel.ControlStatusText.Contains("互锁"));

        Assert.Empty(harness.Session.ArrayWrites);
        Assert.Empty(harness.Session.ControlWrites);
        Assert.Empty(harness.Runtime.All);
    }

    private sealed class ControlHarness : IAsyncDisposable
    {
        private readonly AlarmFeatureTests.TemporaryDirectory _temp;
        public EquipmentFeatureTests.Session Session { get; }
        public EquipmentFeatureTests.Runtime Runtime { get; }
        public Authorization Authorization { get; }
        public OpcUaEquipmentClient Client { get; }
        public EquipmentControlService Service { get; }

        private ControlHarness(AlarmFeatureTests.TemporaryDirectory temp, EquipmentFeatureTests.Session session,
            EquipmentFeatureTests.Runtime runtime, Authorization authorization, OpcUaEquipmentClient client,
            EquipmentControlService service)
        {
            _temp = temp; Session = session; Runtime = runtime; Authorization = authorization;
            Client = client; Service = service;
        }

        public static async Task<ControlHarness> CreateAsync()
        {
            var temp = new AlarmFeatureTests.TemporaryDirectory();
            var path = SqliteAlarmDefinitionRepository.ExtractEmbeddedDatabase(temp.Path);
            var repository = new SqliteEquipmentDefinitionRepository(path);
            var alarms = new SqliteAlarmDefinitionRepository(path).Load();
            var controls = repository.LoadControls();
            var session = new EquipmentFeatureTests.Session();
            var runtime = new EquipmentFeatureTests.Runtime(Path.Combine(temp.Path, "control-runtime.db"));
            var authorization = new Authorization();
            var settings = new AlarmArrayConnectionTests.Settings
            {
                Options = new() { EndpointUrl = "opc.tcp://127.0.0.1:4841", ReconnectDelayMs = 1000 }
            };
            var client = new OpcUaEquipmentClient(settings, new EquipmentFeatureTests.Factory(session), alarms,
                repository.LoadIo(), repository.LoadParameters(), authorization, runtime,
                controlDefinitions: controls);
            var service = new EquipmentControlService(client, controls);
            try
            {
                await client.StartAsync(CancellationToken.None);
                await AlarmArrayConnectionTests.Until(() => client.Snapshot().IsConnected);
                return new(temp, session, runtime, authorization, client, service);
            }
            catch
            {
                service.Dispose();
                await client.DisposeAsync();
                temp.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Service.Dispose();
            await Client.DisposeAsync();
            _temp.Dispose();
        }
    }
}
