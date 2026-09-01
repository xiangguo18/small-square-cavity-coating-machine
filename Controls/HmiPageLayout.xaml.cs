using System.Windows;
using System.Windows.Controls;
using Small_square_cavity_coating_machine.ViewModels;

namespace Small_square_cavity_coating_machine.Controls;

public partial class HmiPageLayout : UserControl
{
    private PlcConnectionStatusViewModel? _connectionPresentation;
    public static readonly DependencyProperty PageTitleProperty =
        DependencyProperty.Register(
            nameof(PageTitle),
            typeof(string),
            typeof(HmiPageLayout),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderRightContentProperty =
        DependencyProperty.Register(
            nameof(HeaderRightContent),
            typeof(object),
            typeof(HmiPageLayout),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PageContentProperty =
        DependencyProperty.Register(
            nameof(PageContent),
            typeof(object),
            typeof(HmiPageLayout),
            new PropertyMetadata(null));

    public HmiPageLayout()
    {
        InitializeComponent();
        // Presentation-only lifecycle: never keep an old page's dropdown open after navigation.
        Loaded += (_, _) =>
        {
            _connectionPresentation = ApplicationStatusViewModel.Instance.PlcConnection;
            if (_connectionPresentation is not null) _connectionPresentation.CloseRequested += CloseConnectionPopup;
        };
        Unloaded += (_, _) =>
        {
            AlarmDetailsToggle.IsChecked = false;
            PlcSettingsToggle.IsChecked = false;
            if (_connectionPresentation is not null) _connectionPresentation.CloseRequested -= CloseConnectionPopup;
            _connectionPresentation = null;
        };
    }

    private void CloseConnectionPopup(object? sender, EventArgs e) => PlcSettingsToggle.IsChecked = false;

    public string PageTitle
    {
        get => (string)GetValue(PageTitleProperty);
        set => SetValue(PageTitleProperty, value);
    }

    public object? HeaderRightContent
    {
        get => GetValue(HeaderRightContentProperty);
        set => SetValue(HeaderRightContentProperty, value);
    }

    public object? PageContent
    {
        get => GetValue(PageContentProperty);
        set => SetValue(PageContentProperty, value);
    }
}
