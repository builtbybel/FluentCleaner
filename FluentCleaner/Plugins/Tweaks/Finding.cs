namespace FluentCleaner.Plugins.Tweaks;

//a tweak plus its current state on this pc
internal sealed class Finding
{
    public Tweak Tweak = null!;   // the catalogue entry
    public bool Done;             // all values already set;ticking it means reset
    public bool Wanted;           // ticked in the list
}
