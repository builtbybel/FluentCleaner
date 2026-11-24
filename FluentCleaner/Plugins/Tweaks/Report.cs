namespace FluentCleaner.Plugins.Tweaks;

//result of one applier run
internal sealed class Report
{
    public int Fixed;                         // settings written
    public int Reset;                         // settings reset
    public List<string> Failed = new();       // titles as shown in the list
    public bool NeedsAdmin;                   // something needed admin rights
    public List<string> Restart = new();      // restarts the ini asks for, no dupes
}
