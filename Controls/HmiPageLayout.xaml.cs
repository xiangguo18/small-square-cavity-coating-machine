using System.Windows;
using System.Windows.Controls;

namespace Small_square_cavity_coating_machine.Controls;

public partial class HmiPageLayout : UserControl
{
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
    }

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
