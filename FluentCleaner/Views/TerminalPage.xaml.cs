using FluentCleaner.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FluentCleaner.Views;

public sealed partial class TerminalPage : Page
{
    public CliViewModel ViewModel { get; } = new();

    private bool _initialized;

    public TerminalPage()
    {
        InitializeComponent();
        ViewModel.Output.CollectionChanged += (_, _) =>
            OutputScroller.ChangeView(null, double.MaxValue, null);

        Loaded += async (_, _) =>
        {
            if (_initialized) return;
            _initialized = true;
            await ViewModel.InitAsync();
        };
    }

    private void Input_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = ViewModel.GetSuggestions(sender.Text);
    }

    private async void Input_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs e)
    {
        var text = e.ChosenSuggestion as string ?? e.QueryText;
        if (string.IsNullOrWhiteSpace(text)) return;
        sender.Text = "";
        sender.ItemsSource = null;  // force suggestion popup closed
        await ViewModel.ExecuteAsync(text);
    }
}
