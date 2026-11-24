namespace FluentCleaner.Plugins;

//progress for the command bar;used by the tweak applier and the app uninstaller
internal readonly struct WorkProgress
{
    public int Completed { get; }      // steps done
    public int Total { get; }          // steps in total
    public string Text { get; }        // text under the bar
    public bool Indeterminate { get; } // nothing to count yet, bar just runs

    public WorkProgress(int completed, int total, string text, bool indeterminate = false)
    {
        Completed = completed;
        Total = total;
        Text = text;
        Indeterminate = indeterminate;
    }
}
