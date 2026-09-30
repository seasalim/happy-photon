using Avalonia.Controls;

namespace HappyPhoton.Views;

public sealed class DevelopGroup : Expander
{
    protected override Type StyleKeyOverride => typeof(Expander);

    public DevelopGroup()
    {
        Classes.Add("develop-group");
        Classes.Add("compact-chevron");
        IsExpanded = true;
        // WP1 keeps the disclosure inert, including automation collapse requests.
        Collapsing += (_, e) => e.Cancel = true;
    }
}
