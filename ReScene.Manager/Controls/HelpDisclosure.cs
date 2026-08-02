using Avalonia.Automation.Peers;
using Avalonia.Controls;

namespace ReScene.Manager.Controls;

/// <summary>
/// The "Help" region every converted task view carries. Behaves as a plain
/// <see cref="Expander"/> in every respect the layout and the behavior care about — it IS one, so
/// <c>CompactHeightBehavior.SetHelpExpander</c>, the shared <c>helpDisclosure</c> template and the
/// compact-mode styling all continue to apply unchanged — and exists solely to correct what the
/// stock control reports to assistive technology.
/// <para>
/// The problem it solves: this region is a real disclosure ONLY in compact mode. At normal size the
/// app styles hide its header ToggleButton and force the body open, so the region is a flat
/// section with no disclosure affordance at all. A stock Expander nevertheless advertises the
/// ExpandCollapse pattern in both modes, and an AT can INVOKE it (non-focusable is
/// not non-actionable) — collapsing Help at normal size into a state the visual design says cannot
/// exist, with no affordance in any modality to undo it. See
/// <see cref="HelpDisclosureAutomationPeer"/> for how the pattern is withheld, and why it is
/// withheld in BOTH modes rather than per-mode.
/// </para>
/// </summary>
public class HelpDisclosure : Expander
{
    protected override AutomationPeer OnCreateAutomationPeer() => new HelpDisclosureAutomationPeer(this);
}
