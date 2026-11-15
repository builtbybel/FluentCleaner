namespace FluentCleaner.Plugins.Tweaks;

//a tweak plus what this pc actually says about it. the difference between the two is the
//whole idea: show both what needs work and what the user may want to put back.
internal sealed class Finding
{
    public Tweak Tweak = null!;   // the catalogue entry this is about
    public bool Done;             // every recommended value is set, so picking it means reset
    public bool Wanted;           // where the tick in front of the row stands
}
