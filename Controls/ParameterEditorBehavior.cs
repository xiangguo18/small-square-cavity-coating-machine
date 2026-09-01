using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Small_square_cavity_coating_machine.Controls;

/// <summary>Keyboard-to-command adapter only. No parameter values, addresses or PLC logic.</summary>
public static class ParameterEditorBehavior
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ParameterEditorBehavior), new PropertyMetadata(false, OnEnabled));
    public static readonly DependencyProperty SubmitCommandProperty = DependencyProperty.RegisterAttached("SubmitCommand", typeof(ICommand), typeof(ParameterEditorBehavior));
    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.RegisterAttached("CancelCommand", typeof(ICommand), typeof(ParameterEditorBehavior));
    public static readonly DependencyProperty BeginCommandProperty = DependencyProperty.RegisterAttached("BeginCommand", typeof(ICommand), typeof(ParameterEditorBehavior));
    public static readonly DependencyProperty EndCommandProperty = DependencyProperty.RegisterAttached("EndCommand", typeof(ICommand), typeof(ParameterEditorBehavior));
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);
    public static ICommand? GetSubmitCommand(DependencyObject o) => (ICommand?)o.GetValue(SubmitCommandProperty);
    public static void SetSubmitCommand(DependencyObject o, ICommand? value) => o.SetValue(SubmitCommandProperty, value);
    public static ICommand? GetCancelCommand(DependencyObject o) => (ICommand?)o.GetValue(CancelCommandProperty);
    public static void SetCancelCommand(DependencyObject o, ICommand? value) => o.SetValue(CancelCommandProperty, value);
    public static ICommand? GetBeginCommand(DependencyObject o) => (ICommand?)o.GetValue(BeginCommandProperty);
    public static void SetBeginCommand(DependencyObject o, ICommand? value) => o.SetValue(BeginCommandProperty, value);
    public static ICommand? GetEndCommand(DependencyObject o) => (ICommand?)o.GetValue(EndCommandProperty);
    public static void SetEndCommand(DependencyObject o, ICommand? value) => o.SetValue(EndCommandProperty, value);

    private static void OnEnabled(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not TextBox box) return;
        if ((bool)e.NewValue) { box.PreviewKeyDown += OnKey; box.GotKeyboardFocus += OnFocus; box.LostKeyboardFocus += OnBlur; }
        else { box.PreviewKeyDown -= OnKey; box.GotKeyboardFocus -= OnFocus; box.LostKeyboardFocus -= OnBlur; }
    }
    private static void Execute(ICommand? command) { if (command?.CanExecute(null) == true) command.Execute(null); }
    private static void OnFocus(object sender, KeyboardFocusChangedEventArgs e) => Execute(GetBeginCommand((TextBox)sender));
    private static void OnBlur(object sender, KeyboardFocusChangedEventArgs e) => Execute(GetEndCommand((TextBox)sender));
    private static void OnKey(object sender, KeyEventArgs e)
    {
        if (HandleKey((TextBox)sender, e.Key, e.IsRepeat)) e.Handled = true;
    }
    public static bool HandleKey(TextBox box, Key key, bool isRepeat)
    {
        if (key is not (Key.Enter or Key.Escape)) return false;
        if (isRepeat) return true;
        if (key == Key.Escape) Execute(GetCancelCommand(box));
        else
        {
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Execute(GetSubmitCommand(box));
        }
        return true;
    }
}
