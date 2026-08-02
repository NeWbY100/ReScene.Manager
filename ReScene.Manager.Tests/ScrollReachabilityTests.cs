using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Pins that every tab page's ScrollViewer can reach the END of its content once it overflows.
/// Regression (reported on a small VM window): <c>ScrollViewer.Padding</c> shifts the content's
/// arranged position but is excluded from the reported scroll extent, so the trailing ~padding of
/// content sits permanently below the maximum offset — the RAR Reconstructor Options tab's last
/// checkboxes could not be scrolled into view. The inset therefore lives on the content panel
/// (<c>Margin</c>), never on the ScrollViewer; these tests fail against the Padding form.
/// </summary>
public class ScrollReachabilityTests
{
    /// <summary>
    /// Selects every sub-tab, scrolls its page ScrollViewer to the bottom edge, and returns
    /// (headers that actually overflowed, failures where the content's bottom stayed beyond the
    /// viewport). Pages whose content fits prove nothing; callers guard rig validity by asserting
    /// which pages overflowed.
    /// </summary>
    private static (List<string> Scrollable, List<string> Unreachable) ProbePages(TabControl tabs)
    {
        List<string> scrollable = [];
        List<string> unreachable = [];
        for (int i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            Dispatcher.UIThread.RunJobs();
            // Headers may be composite panels (e.g. Paths carries a warning icon) — only plain
            // string headers are used for naming; composites fall back to the index.
            string header = (tabs.Items[i] as TabItem)?.Header as string ?? $"#{i}";

            // The page's own ScrollViewer is unnamed; TextBox template internals are PART_ScrollViewer.
            ScrollViewer? sv = tabs.GetVisualDescendants().OfType<ScrollViewer>()
                .FirstOrDefault(s => s.Name is null && s.IsEffectivelyVisible);
            Assert.NotNull(sv);
            if (sv!.Extent.Height <= sv.Viewport.Height + 0.5)
            {
                continue;
            }

            scrollable.Add(header);
            sv.Offset = new Vector(sv.Offset.X, sv.Extent.Height - sv.Viewport.Height);
            Dispatcher.UIThread.RunJobs();

            var content = Assert.IsAssignableFrom<Control>(sv.Content);
            Point bottom = content.TranslatePoint(new Point(0, content.Bounds.Height), sv)!.Value;
            if (bottom.Y > sv.Viewport.Height + 0.5)
            {
                unreachable.Add($"{header}: content bottom {bottom.Y:F1} exceeds viewport {sv.Viewport.Height:F1} at max scroll");
            }
        }

        return (scrollable, unreachable);
    }

    [AvaloniaFact]
    public void ReconstructorView_TabPages_ScrollToEnd_ReachesLastContent()
    {
        ReconstructorViewModel vm = BeginnerShellTestFactory.Create().Reconstructor;
        var view = new ReconstructorView { DataContext = vm };
        // 1012x640 sits ABOVE the view's fixed-minimums floor (~565px: header rows + tab strip +
        // TabControl MinHeight 220 + splitter + log MinHeight 140), so every page ScrollViewer is
        // fully inside the window and this fact isolates extent-reachability. Heights BELOW the
        // floor clip the grid itself — a separate structural defect tracked for its own fix.
        var window = new Window { Width = 1012, Height = 640, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            TabControl subTabs = view.GetVisualDescendants().OfType<TabControl>().First();

            // Executable form of the rig premise: the page ScrollViewer itself must sit fully
            // inside the window here — below the fixed-minimums floor the grid clips instead of
            // scrolling (the tracked structural defect) and within-viewer reachability would no
            // longer describe what the user can see.
            ScrollViewer firstPage = subTabs.GetVisualDescendants().OfType<ScrollViewer>()
                .First(s => s.Name is null && s.IsEffectivelyVisible);
            Point svBottom = firstPage.TranslatePoint(new Point(0, firstPage.Bounds.Height), window)!.Value;
            Assert.True(svBottom.Y <= window.Bounds.Height + 0.5,
                $"rig premise broken: page ScrollViewer bottom {svBottom.Y:F1} spills past window height {window.Bounds.Height:F1}");

            (List<string> scrollable, List<string> unreachable) = ProbePages(subTabs);

            // Rig validity: the window is sized so the reported page (Options) genuinely
            // overflows — a page that fits passes trivially and would mask the regression.
            Assert.Contains("Options", scrollable);
            Assert.True(scrollable.Count >= 3,
                $"rig validity: only [{string.Join(", ", scrollable)}] overflowed at 1012x640");

            Assert.True(unreachable.Count == 0, string.Join("; ", unreachable));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsWindow_TabPages_ScrollToEnd_ReachesLastContent()
    {
        var vm = new SettingsViewModel(new InertAppSettingsService(), new AvaloniaFileDialogService(static () => null));
        // Shipping minimum geometry (MinWidth=560, MinHeight=360): the longest page overflows
        // right at the window's own floor, so the probe needs no minimum overrides.
        var window = new SettingsWindow(vm) { Width = 560, Height = 360 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            (List<string> scrollable, List<string> unreachable) = ProbePages(tabs);

            // The RAR Reconstruction page is the longest and must overflow at the shipping
            // minimum — pinning it by name keeps the rig honest if pages are reordered.
            Assert.Contains("RAR Reconstruction", scrollable);

            Assert.True(unreachable.Count == 0, string.Join("; ", unreachable));

            // Small-window-layout Task 7 audit: SettingsWindow owns its own MinWidth 560 /
            // MinHeight 360 and its pages already scroll (the fact above proves the LAST control
            // is reachable by scroll). Criterion C, applied to Settings, is the remaining half of
            // the audit — a genuine keyboard Tab walk from each page's first control must never
            // leave the window at that same minimum. No compact machinery (CompactHeightBehavior)
            // is added here — this proves none is needed; a failure here means the audit's
            // premise is wrong and the spec needs a change, not a silent workaround.
            //
            // Whole-branch review (MAJOR): the original retrofit called AssertTabWalkStaysVisible
            // with no expected-stop sets (completeness unchecked — a stable-but-early trap could
            // pass) and no reverse anchor (the reverse pass re-used the forward sentinel, which is
            // first-in-scope on every one of these four tabs, so it was a trivial no-op — see each
            // helper below). Retrofitted per-tab: every stop is resolved INDEPENDENTLY (bound
            // command reference for Buttons; Content text for Cancel/Save/RadioButtons/links; a
            // unique marker value written into the bound VM property, then matched back off the
            // realized TextBox's own Text, for the two path fields with neither an x:Name nor a
            // distinguishing XAML attribute) — never derived from the walk's own output, matching
            // e.g. SRSCreatorCompactTests.ResolveIndependentExpectedOrder's technique. Each tab's
            // reverse pass is anchored at the forward walk's own PROVEN last stop (a throwaway
            // diagnostic probe against this exact geometry established which control that is,
            // once, per the rig's "PROVEN first, not presumed" convention — see CompactViewRig's
            // own remarks on RunTabPass/reverseSentinel) rather than the sentinel, so each reverse
            // pass genuinely retraces the page instead of immediately re-landing on itself.
            AssertInterfaceTabWalk(window, tabs);
            AssertGeneralTabWalk(window, tabs, vm);
            AssertInspectorAndCompareTabWalk(window, tabs, vm);
            AssertRarReconstructionTabWalk(window, tabs, vm);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// PROVEN (throwaway diagnostic probe against this exact 560×360 geometry, not committed):
    /// forward from Beginner visits [Beginner, Advanced, Cancel, Save], then a stable loop closes
    /// back on Advanced — re-entering the RadioButton group lands on the CHECKED item, not the
    /// group's first member, so Beginner itself never recurs going forward. Reverse anchored at
    /// Save (the forward walk's own last NEW stop) retraces [Save, Cancel, Advanced, Beginner]
    /// and closes on Beginner itself: it is first in its own keyboard-navigation scope, so
    /// Shift+Tab from it cannot move further — the identical phenomenon
    /// <see cref="CompactViewRig"/>'s own <c>reverseSentinel</c> remarks document.
    /// </summary>
    private static void AssertInterfaceTabWalk(Window window, TabControl tabs)
    {
        tabs.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();

        Button cancel = ResolveCancelButton(window);
        Button save = ResolveSaveButton(window);
        RadioButton beginner = window.GetVisualDescendants().OfType<RadioButton>().Single(r => r.Content as string == "Beginner");
        RadioButton advanced = window.GetVisualDescendants().OfType<RadioButton>().Single(r => r.Content as string == "Advanced");

        List<Control> stops = [beginner, advanced, cancel, save];
        CompactViewRig.AssertTabWalkStaysVisible(window, beginner,
            expectedForwardStops: stops, expectedReverseStops: stops, reverseSentinel: save);
    }

    /// <summary>
    /// PROVEN (throwaway diagnostic probe, not committed): forward from DefaultAppName's TextBox
    /// (document order places it before the Browse+DefaultOutputDirectory DockPanel) visits
    /// [AppName, Browse, OutputDir, RecentFilesLimit's own PART_TextBox, Cancel, Save], then a
    /// stable loop closes back on RecentFilesLimit's PART_TextBox — its NumericUpDown's editable
    /// part is a separate, later keyboard-navigation scope from the rest of the tab, the same
    /// "closes on a middle stop, not the sentinel" phenomenon the Interface tab's RadioButton
    /// group shows. Reverse anchored at Save retraces the same six stops backwards and closes
    /// back on AppName's own TextBox (first in its own scope).
    /// </summary>
    private static void AssertGeneralTabWalk(Window window, TabControl tabs, SettingsViewModel vm)
    {
        tabs.SelectedIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Button cancel = ResolveCancelButton(window);
        Button save = ResolveSaveButton(window);
        Button browseOutputDir = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseOutputDirCommand));

        // Neither path TextBox carries an x:Name or a distinguishing XAML attribute (the
        // review's own citation) — resolved instead via a unique marker value written into each
        // bound VM property, then matched back off the realized TextBox's own Text: a
        // reference-independent identity exactly like the command references above, never
        // derived from the walk.
        vm.DefaultAppName = "TabWalkMarker_AppName";
        vm.DefaultOutputDirectory = "TabWalkMarker_OutputDir";
        Dispatcher.UIThread.RunJobs();
        TextBox appNameTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "TabWalkMarker_AppName");
        TextBox outputDirTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "TabWalkMarker_OutputDir");

        // RecentFilesLimit's own editable part likewise has no x:Name; it is the only realized
        // PART_TextBox in this tab's scope.
        TextBox recentFilesLimitPart = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_TextBox");

        List<Control> stops = [appNameTextBox, browseOutputDir, outputDirTextBox, recentFilesLimitPart, cancel, save];
        CompactViewRig.AssertTabWalkStaysVisible(window, appNameTextBox,
            expectedForwardStops: stops, expectedReverseStops: stops, reverseSentinel: save);
    }

    /// <summary>
    /// PROVEN (throwaway diagnostic probe, not committed): forward from the NumericUpDown visits
    /// [NumericUpDown, Cancel, Save, its OWN PART_TextBox] — the editable part is again a
    /// separate, later scope from the outer control — then a stable loop closes back on Cancel.
    /// The reverse walk anchored at that same PART_TextBox is genuinely, honestly DEGENERATE: a
    /// single-entry self-loop (Shift+Tab from it cannot move at all — first in its own scope,
    /// identical to every other tab's own reverse boundary here), so its expected-stop set is
    /// correctly just itself rather than padded out to match the forward set's size — the same
    /// "deliberately single-entry... an honest reflection of this VERIFIED reality, not an
    /// oversight" disposition <see cref="CompactViewRig"/>'s own remarks record elsewhere.
    /// </summary>
    private static void AssertInspectorAndCompareTabWalk(Window window, TabControl tabs, SettingsViewModel vm)
    {
        tabs.SelectedIndex = 2;
        Dispatcher.UIThread.RunJobs();

        Button cancel = ResolveCancelButton(window);
        Button save = ResolveSaveButton(window);

        vm.MKVMaxElements = 54321; // unique marker; not otherwise load-bearing here
        Dispatcher.UIThread.RunJobs();
        NumericUpDown mkvMaxElements = window.GetVisualDescendants().OfType<NumericUpDown>().Single();
        TextBox mkvMaxElementsPart = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_TextBox");

        List<Control> forwardStops = [mkvMaxElements, cancel, save, mkvMaxElementsPart];
        List<Control> reverseStops = [mkvMaxElementsPart];
        CompactViewRig.AssertTabWalkStaysVisible(window, mkvMaxElements,
            expectedForwardStops: forwardStops, expectedReverseStops: reverseStops, reverseSentinel: mkvMaxElementsPart);
    }

    /// <summary>
    /// PROVEN (throwaway diagnostic probe, not committed): forward from the WinRAR Browse button
    /// (document order places the <c>DockPanel.Dock="Right"</c> Button before its TextBox, so it
    /// — not the path field — is first in tab order) visits all ten controls in document order,
    /// then a stable loop closes back on the CheckBox. Reverse anchored at Save retraces the same
    /// ten stops backwards and closes back on the WinRAR Browse button itself (first in its own
    /// scope, the same phenomenon every other tab above shows).
    /// </summary>
    private static void AssertRarReconstructionTabWalk(Window window, TabControl tabs, SettingsViewModel vm)
    {
        tabs.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();

        Button cancel = ResolveCancelButton(window);
        Button save = ResolveSaveButton(window);
        Button browseWinRAR = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseReconstructWinRARCommand));
        Button browseOutput = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseReconstructOutputCommand));
        Button windowsLink = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Extracted files for Windows (ready to use)");
        Button linuxLink = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Extracted files for Linux (ready to use)");
        Button ftpLink = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Original files from RAR FTP (Windows)");
        CheckBox cleanupCheckBox = window.GetVisualDescendants().OfType<CheckBox>().Single();

        vm.ReconstructWinRARPath = "TabWalkMarker_WinRARPath";
        vm.ReconstructOutputPath = "TabWalkMarker_OutputPath";
        Dispatcher.UIThread.RunJobs();
        TextBox winRarPathTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "TabWalkMarker_WinRARPath");
        TextBox outputPathTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Text == "TabWalkMarker_OutputPath");

        List<Control> stops =
        [
            browseWinRAR, winRarPathTextBox, windowsLink, linuxLink, ftpLink,
            browseOutput, outputPathTextBox, cleanupCheckBox, cancel, save,
        ];
        CompactViewRig.AssertTabWalkStaysVisible(window, browseWinRAR,
            expectedForwardStops: stops, expectedReverseStops: stops, reverseSentinel: save);
    }

    // Cancel/Save use Click handlers, not bound Commands, so they are resolved by their own
    // (unique, window-wide) Content text — shared across all four tabs' helpers above.
    private static Button ResolveCancelButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Cancel");

    private static Button ResolveSaveButton(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Save");

    private sealed class InertAppSettingsService : IAppSettingsService
    {
        public event EventHandler? Changed { add { } remove { } }
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
}
