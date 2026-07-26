using System.Text;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.SRR;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Regression tests for two Inspector edit-lifecycle defects:
/// (3) a failed Add/Remove stored-file edit left the Hex/Text panes permanently blank (the file
/// handles were released but never re-opened), and (4) the tree 'Export…' item's enabled state was
/// off by one selection (and stale after Close) because its command was never re-notified when
/// <c>HexBlockLength</c> changed or when the file closed.
/// </summary>
public sealed class InspectorViewModelEditRecoveryTests : TempDirTestBase
{
    // Editing service whose mutating operations throw, simulating a failed edit. The load path never
    // touches it (LoadFileAsync parses via SRRFileData.Load), so the read members stay unimplemented.
    private sealed class ThrowingEditingService : ISRREditingService
    {
        public void AddStoredFiles(string srrFilePath, IReadOnlyList<(string StoredName, string FilePath)> files)
            => throw new InvalidOperationException("add failed");
        public void RemoveStoredFiles(string srrFilePath, IReadOnlyList<string> storedNames)
            => throw new InvalidOperationException("remove failed");
        public Task RenameStoredFileAsync(string srrPath, string oldName, string newName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task MoveStoredFileAsync(string srrPath, string storedName, int offset, CancellationToken ct = default) => throw new NotSupportedException();
        public IReadOnlyList<StoredFileInfo> GetStoredFiles(string srrFilePath) => throw new NotSupportedException();
        public Task<string?> ExtractStoredFileAsync(string srrFilePath, string outputDirectory, string storedName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]?> ReadStoredFileBytesAsync(string srrFilePath, string storedName, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class NoOpEditingService : ISRREditingService
    {
        public void AddStoredFiles(string srrFilePath, IReadOnlyList<(string StoredName, string FilePath)> files) { }
        public void RemoveStoredFiles(string srrFilePath, IReadOnlyList<string> storedNames) { }
        public Task RenameStoredFileAsync(string srrPath, string oldName, string newName, CancellationToken ct = default) => Task.CompletedTask;
        public Task MoveStoredFileAsync(string srrPath, string storedName, int offset, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<StoredFileInfo> GetStoredFiles(string srrFilePath) => [];
        public Task<string?> ExtractStoredFileAsync(string srrFilePath, string outputDirectory, string storedName, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<byte[]?> ReadStoredFileBytesAsync(string srrFilePath, string storedName, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    }

    private sealed class StubVerifyService : ISRRVerifyService
    {
        public Task<SRRVerifyResult> VerifyAsync(string srrFilePath, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubPropertyExportService : IPropertyExportService
    {
        public Task ExportSelectedAsync(string outputPath, TreeNodeViewModel node, IEnumerable<PropertyItem> properties, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ExportTreeAsync(string outputPath, IEnumerable<TreeNodeViewModel> roots, CancellationToken ct = default) => throw new NotSupportedException();
    }

    // Dialog whose OpenFileAsync returns a fixed path (the file to add).
    private sealed class OpenReturnsPathDialog(string path) : NoOpFileDialogService
    {
        public override Task<string?> OpenFileAsync(string title, IReadOnlyList<string> filters, string? initialPath = null)
            => Task.FromResult<string?>(path);
    }

    // Drive the async LoadFileAsync (and the async commands) synchronously without deadlocking on the
    // internal `await Task.Run` continuation. See InspectorViewModelImageTests for the same helper.
    private static void RunSync(Func<Task> action) => Task.Run(action).GetAwaiter().GetResult();

    private InspectorViewModel LoadSrrWithStored(ISRREditingService editing, IFileDialogService dialog, out string srrPath, string storedName = "keep.nfo")
    {
        srrPath = SRREditingServiceImageTests.WriteMinimalSRR(TempDir, "inspect.srr", storedName, Encoding.ASCII.GetBytes("DATA"));
        var vm = new InspectorViewModel(dialog, editing, new StubVerifyService(), new StubPropertyExportService(), new RecordingImagePreviewService());
        string local = srrPath;
        RunSync(() => vm.LoadFileAsync(local));
        return vm;
    }

    // ── (3) Add / Remove recover the Hex/Text view after a failed edit ──

    [Fact]
    public void AddStoredFile_WhenEditThrows_ReloadsSoHexRecovers_AndShowsError()
    {
        string toAdd = Path.Combine(TempDir, "extra.txt");
        File.WriteAllText(toAdd, "x");

        using InspectorViewModel vm = LoadSrrWithStored(new ThrowingEditingService(), new OpenReturnsPathDialog(toAdd), out _);
        vm.SelectedTreeNode = vm.TreeRoots.Flatten().First(n => n.Tag is SRRStoredFileBlock);
        Assert.NotNull(vm.HexDataSource); // baseline: hex renders before the failed edit

        RunSync(() => vm.AddStoredFileToSRRCommand.ExecuteAsync(null));

        // The failure is surfaced to the user...
        Assert.Contains("Error adding stored file", vm.StatusMessage, StringComparison.Ordinal);

        // ...and the file was re-opened, so selecting a node shows hex again. Before the fix,
        // ReleaseFileHandles disposed the data source and only the success path re-loaded it, so a
        // failed edit left Hex/Text blank until the user closed and re-opened the file.
        vm.SelectedTreeNode = null;
        vm.SelectedTreeNode = vm.TreeRoots.Flatten().First(n => n.Tag is SRRStoredFileBlock);
        Assert.NotNull(vm.HexDataSource);
    }

    [Fact]
    public void RemoveStoredFile_WhenEditThrows_ReloadsSoHexRecovers_AndShowsError()
    {
        using InspectorViewModel vm = LoadSrrWithStored(new ThrowingEditingService(), new NoOpFileDialogService(), out _);
        vm.SelectedTreeNode = vm.TreeRoots.Flatten().First(n => n.Tag is SRRStoredFileBlock);
        Assert.NotNull(vm.HexDataSource);

        RunSync(() => vm.RemoveStoredFileFromSRRCommand.ExecuteAsync(null));

        Assert.Contains("Error removing stored file", vm.StatusMessage, StringComparison.Ordinal);

        vm.SelectedTreeNode = null;
        vm.SelectedTreeNode = vm.TreeRoots.Flatten().First(n => n.Tag is SRRStoredFileBlock);
        Assert.NotNull(vm.HexDataSource);
    }

    // ── (4) Export CanExecute is correct on first selection and after Close ──

    [Fact]
    public void ExportBlock_EnabledOnFirstSelection_WithoutASecondSelection()
    {
        using InspectorViewModel vm = LoadSrrWithStored(new NoOpEditingService(), new NoOpFileDialogService(), out _);

        bool enabledAtLastEvent = false;
        vm.ExportBlockCommand.CanExecuteChanged += (_, _) => enabledAtLastEvent = vm.ExportBlockCommand.CanExecute(null);

        // Select a block with data. Before the fix, the command's CanExecuteChanged fired (in
        // OnSelectedTreeNodeChanged) BEFORE SetHexBlock set HexBlockLength, and HexBlockLength carried
        // no [NotifyCanExecuteChangedFor], so no event announced the now-enabled state — the tree's
        // 'Export…' item showed disabled for one selection after opening the file.
        vm.SelectedTreeNode = vm.TreeRoots.Flatten().First(n => n.Tag is SRRStoredFileBlock);

        Assert.True(vm.ExportBlockCommand.CanExecute(null));
        Assert.True(enabledAtLastEvent, "the last CanExecuteChanged must reflect the enabled state (no stale disable).");
    }

    [Fact]
    public void ExportBlock_Disabled_AndReNotified_AfterCloseFile()
    {
        using InspectorViewModel vm = LoadSrrWithStored(new NoOpEditingService(), new NoOpFileDialogService(), out _);
        vm.SelectedTreeNode = vm.TreeRoots.Flatten().First(n => n.Tag is SRRStoredFileBlock);
        Assert.True(vm.ExportBlockCommand.CanExecute(null));

        bool eventFired = false;
        bool enabledAtLastEvent = true;
        vm.ExportBlockCommand.CanExecuteChanged += (_, _) =>
        {
            eventFired = true;
            enabledAtLastEvent = vm.ExportBlockCommand.CanExecute(null);
        };

        vm.CloseFileCommand.Execute(null);

        Assert.False(vm.ExportBlockCommand.CanExecute(null));
        Assert.True(eventFired, "CloseFile must re-notify ExportBlockCommand so the UI clears the stale-enabled state.");
        Assert.False(enabledAtLastEvent);
    }
}
