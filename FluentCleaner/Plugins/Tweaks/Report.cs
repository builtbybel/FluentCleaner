namespace FluentCleaner.Plugins.Tweaks;

//what came out of one run of the applier
internal sealed class Report
{
    public int Fixed;                         // settings written
    public int Reset;                         // settings put back
    public List<string> Failed = new();       // titles, the wording the user just read in the list
    public bool NeedsAdmin;                   // something needed rights this user has not got
    public List<string> Restart = new();      // what the ini wants restarted, each one once
}
