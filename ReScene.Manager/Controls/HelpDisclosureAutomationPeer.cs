using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls.Automation.Peers;

namespace ReScene.Manager.Controls;

/// <summary>
/// Reports the <see cref="HelpDisclosure"/> region as a stable, NON-ACTIONABLE container: a group
/// that holds the Help content, never a control an assistive technology can expand or collapse.
/// The single actionable route is the header ToggleButton, which carries the Toggle pattern and is
/// the only keyboard-focusable peer in the region — and which exists only in compact mode, exactly
/// where a disclosure affordance is real.
/// <para>
/// WHY THE PATTERN IS WITHHELD IN BOTH MODES, not per mode: a peer whose advertised patterns change
/// underneath a client is worse than one that never offered them. An AT that has resolved and cached
/// this element's providers must not find the topology reshaped by a window resize it did not
/// initiate. Compact loses nothing by it — the toggle reports the same state through Toggle, which
/// is the pattern a header button should carry anyway.
/// </para>
/// <para>
/// MECHANISM, and why it is this one. <see cref="ExpanderAutomationPeer"/>'s own
/// <c>Collapse</c>, <c>Expand</c> and <c>ExpandCollapseState</c> are IL <c>virtual final</c> —
/// what an implicit interface implementation compiles to — so they cannot be overridden, however
/// much reflection's <c>IsVirtual</c> alone suggests otherwise (MEASURED: <c>virtual=True
/// final=True</c> on all three). The overridable seam is <see cref="GetProviderCore"/>, the
/// protected virtual behind the public <c>GetProvider&lt;T&gt;()</c> that the platform's UIA bridge
/// resolves patterns through: returning null there withholds the pattern at the boundary that
/// decides what a client can actually invoke. The peer type still IMPLEMENTS the interface — that
/// cannot be un-inherited — so a direct cast in-process still succeeds; a UIA client does not have
/// one, it asks through the provider route this closes.
/// </para>
/// </summary>
public class HelpDisclosureAutomationPeer : ExpanderAutomationPeer
{
    public HelpDisclosureAutomationPeer(HelpDisclosure owner)
        : base(owner)
    {
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override object? GetProviderCore(Type patternType) =>
        patternType == typeof(IExpandCollapseProvider) ? null : base.GetProviderCore(patternType);
}
