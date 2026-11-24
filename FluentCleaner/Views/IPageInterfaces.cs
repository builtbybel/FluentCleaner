using Microsoft.UI.Xaml.Controls;

namespace FluentCleaner.Views;

/// <summary>Pages that support the global TitleBar search box implement this.</summary>
public interface ISearchablePage
{
    void OnSearch(string text);
}

/// <summary>
/// Pages with their own actions implement this; they appear at the top of the app menu
/// (the ∨ beside the plus) whenever the page is the one in front.
/// </summary>
public interface IPageActions
{
    void BuildActions(MenuFlyout flyout);
}

/// <summary>
/// Pages that can reload their content. Refresh in the app menu and F5 call this.
/// </summary>
public interface IRefreshablePage
{
    void Refresh();
}

public static class MenuFlyoutExtensions
{
    public static void AddItem(this MenuFlyout flyout, string label, Action action)
    {
        var item = new MenuFlyoutItem { Text = label };
        item.Click += (_, _) => action();
        flyout.Items.Add(item);
    }
}