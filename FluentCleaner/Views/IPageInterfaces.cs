using Microsoft.UI.Xaml.Controls;

namespace FluentCleaner.Views;

/// <summary>Pages that support the global TitleBar search box implement this.</summary>
public interface ISearchablePage
{
    void OnSearch(string text);
}

/// <summary>Pages that want to expose contextual actions in the TitleBar [...] button implement this.</summary>
public interface IPageActions
{
    void BuildActions(MenuFlyout flyout);
}
