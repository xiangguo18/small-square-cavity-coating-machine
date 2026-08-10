using Small_square_cavity_coating_machine.ViewModels;
using Xunit;

namespace SmallSquareCavityCoatingMachine.Tests;

public sealed class ControlViewModelPipeFlowTests
{
    [Fact]
    public void VacuumPipes_RequireTheirAdjacentDevices()
    {
        var viewModel = new ControlViewModel
        {
            ApcCurrentPosition = 0d
        };

        Assert.False(viewModel.ChamberToApcPipeIsFlowing);
        Assert.False(viewModel.ApcToTurboPipeIsFlowing);
        Assert.False(viewModel.TurboToRightForelinePipeIsFlowing);
        Assert.False(viewModel.RightForelineToDryPumpPipeIsFlowing);

        viewModel.ApcCurrentPosition = 25d;
        Assert.True(viewModel.ChamberToApcPipeIsFlowing);
        Assert.False(viewModel.ApcToTurboPipeIsFlowing);

        viewModel.TurboPumpIsRunning = true;
        Assert.True(viewModel.ApcToTurboPipeIsFlowing);
        Assert.False(viewModel.TurboToRightForelinePipeIsFlowing);

        viewModel.RightForelineValveIsOpen = true;
        Assert.True(viewModel.TurboToRightForelinePipeIsFlowing);
        Assert.False(viewModel.RightForelineToDryPumpPipeIsFlowing);

        viewModel.DryPumpIsRunning = true;
        Assert.True(viewModel.RightForelineToDryPumpPipeIsFlowing);

        viewModel.RightForelineValveIsOpen = false;
        Assert.False(viewModel.TurboToRightForelinePipeIsFlowing);
        Assert.False(viewModel.RightForelineToDryPumpPipeIsFlowing);
    }

    [Fact]
    public void ChamberBranches_TreatTheChamberAsAlwaysOpen()
    {
        var viewModel = new ControlViewModel();

        Assert.False(viewModel.ChamberToMixingPipeIsFlowing);
        Assert.False(viewModel.ChamberToBypassPipeIsFlowing);
        Assert.False(viewModel.BypassToDryPumpPipeIsFlowing);
        Assert.False(viewModel.ChamberToLowerForelinePipeIsFlowing);

        viewModel.BypassValveIsOpen = true;
        Assert.True(viewModel.ChamberToBypassPipeIsFlowing);
        Assert.False(viewModel.BypassToDryPumpPipeIsFlowing);

        viewModel.DryPumpIsRunning = true;
        Assert.True(viewModel.BypassToDryPumpPipeIsFlowing);

        viewModel.LowerForelineValveIsOpen = true;
        Assert.True(viewModel.ChamberToLowerForelinePipeIsFlowing);
    }

    [Fact]
    public void ChamberToMixingPipe_FlowsWhenAnyConnectedUpperValveIsOpen()
    {
        var viewModel = new ControlViewModel();

        Assert.False(viewModel.ChamberToMixingPipeIsFlowing);

        viewModel.ArgonUpperValveIsOpen = true;
        Assert.True(viewModel.ChamberToMixingPipeIsFlowing);

        viewModel.ArgonUpperValveIsOpen = false;
        Assert.False(viewModel.ChamberToMixingPipeIsFlowing);

        viewModel.NitrogenUpperValveIsOpen = true;
        Assert.True(viewModel.ChamberToMixingPipeIsFlowing);

        viewModel.NitrogenUpperValveIsOpen = false;
        Assert.False(viewModel.ChamberToMixingPipeIsFlowing);

        viewModel.OxygenUpperValveIsOpen = true;
        Assert.True(viewModel.ChamberToMixingPipeIsFlowing);

        viewModel.OxygenUpperValveIsOpen = false;
        Assert.False(viewModel.ChamberToMixingPipeIsFlowing);
    }

    [Fact]
    public void GasPipes_RequireTheAdjacentValvesAndTheirOwnMfcFlow()
    {
        var viewModel = new ControlViewModel
        {
            ArgonCurrentFlow = 0.6d,
            NitrogenCurrentFlow = 0d,
            OxygenCurrentFlow = 0.8d
        };

        viewModel.ArgonUpperValveIsOpen = true;
        viewModel.NitrogenUpperValveIsOpen = true;
        viewModel.OxygenUpperValveIsOpen = true;

        Assert.True(viewModel.ArgonMixingPipeIsFlowing);
        Assert.True(viewModel.NitrogenMixingPipeIsFlowing);
        Assert.True(viewModel.OxygenMixingPipeIsFlowing);
        Assert.False(viewModel.ArgonValvePairPipeIsFlowing);
        Assert.False(viewModel.NitrogenValvePairPipeIsFlowing);
        Assert.False(viewModel.OxygenValvePairPipeIsFlowing);

        viewModel.ArgonLowerValveIsOpen = true;
        viewModel.NitrogenLowerValveIsOpen = true;
        viewModel.OxygenLowerValveIsOpen = true;

        Assert.True(viewModel.ArgonValvePairPipeIsFlowing);
        Assert.True(viewModel.NitrogenValvePairPipeIsFlowing);
        Assert.True(viewModel.OxygenValvePairPipeIsFlowing);
        Assert.True(viewModel.ArgonMfcPipeIsFlowing);
        Assert.False(viewModel.NitrogenMfcPipeIsFlowing);
        Assert.True(viewModel.OxygenMfcPipeIsFlowing);

        viewModel.ArgonLowerValveIsOpen = false;
        Assert.False(viewModel.ArgonValvePairPipeIsFlowing);
        Assert.False(viewModel.ArgonMfcPipeIsFlowing);
        Assert.False(viewModel.NitrogenMfcPipeIsFlowing);
        Assert.True(viewModel.OxygenMfcPipeIsFlowing);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-0.1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void MfcPipe_DoesNotFlowForNonPositiveOrInvalidValues(double currentFlow)
    {
        var viewModel = new ControlViewModel
        {
            ArgonLowerValveIsOpen = true,
            ArgonCurrentFlow = currentFlow
        };

        Assert.False(viewModel.ArgonMfcPipeIsFlowing);
    }

    [Fact]
    public void SourceChanges_NotifyDependentPipeProperties()
    {
        var viewModel = new ControlViewModel();
        var changedProperties = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.TurboPumpIsRunning = true;

        Assert.Contains(nameof(ControlViewModel.ApcToTurboPipeIsFlowing), changedProperties);
        Assert.Contains(nameof(ControlViewModel.TurboToRightForelinePipeIsFlowing), changedProperties);

        changedProperties.Clear();
        viewModel.ArgonUpperValveIsOpen = true;

        Assert.Contains(nameof(ControlViewModel.ChamberToMixingPipeIsFlowing), changedProperties);
        Assert.Contains(nameof(ControlViewModel.ArgonMixingPipeIsFlowing), changedProperties);

        changedProperties.Clear();
        viewModel.NitrogenCurrentFlow = 0d;

        Assert.Contains(nameof(ControlViewModel.NitrogenMfcPipeIsFlowing), changedProperties);
    }
}
