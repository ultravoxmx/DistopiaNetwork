using DistopiaNetwork.WindowsClient.Domain.Models;
using DistopiaNetwork.WindowsClient.Domain.Services;

namespace DistopiaNetwork.WindowsClient.UI.Forms;

public class MainExplorerForm : Form
{
    private readonly CatalogService _catalog;
    private readonly MetadataEditService _editService;
    private readonly DeleteService _deleteService;

    private readonly TreeView _tree = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false };
    private readonly PropertyGrid _details = new() { Dock = DockStyle.Fill };
    private readonly Button _btnRefresh = new() { Text = "Refresh" };
    private readonly Button _btnEdit = new() { Text = "Edit Metadata" };
    private readonly Button _btnDelete = new() { Text = "Delete" };

    private List<PodcastItemView> _currentItems = [];

    public MainExplorerForm(CatalogService catalog, MetadataEditService editService, DeleteService deleteService)
    {
        _catalog = catalog;
        _editService = editService;
        _deleteService = deleteService;

        Text = "Distopia Network Explorer";
        Width = 1400;
        Height = 820;

        BuildUi();

        Load += async (_, _) => await RefreshCatalogAsync();
    }

    private void BuildUi()
    {
        var main = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 260 };
        var right = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 800 };

        _tree.Nodes.Add("Catalog", "Catalog");
        _tree.Nodes[0]!.Nodes.Add("All", "All Podcasts");
        _tree.Nodes[0]!.Nodes.Add("Mine", "My Podcasts");
        _tree.ExpandAll();
        _tree.AfterSelect += (_, _) => ApplyFilter();

        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PodcastItemView.Title), HeaderText = "Title", Width = 270 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PodcastItemView.PublisherServer), HeaderText = "Server", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PodcastItemView.PublisherPubKeyShort), HeaderText = "Creator", Width = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PodcastItemView.PublishTimestamp), HeaderText = "Timestamp", Width = 140 });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(PodcastItemView.IsOwner), HeaderText = "Owner", Width = 70 });
        _grid.SelectionChanged += (_, _) => UpdateDetails();

        var toolPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        _btnRefresh.Click += async (_, _) => await RefreshCatalogAsync();
        _btnEdit.Click += async (_, _) => await EditSelectedAsync();
        _btnDelete.Click += async (_, _) => await DeleteSelectedAsync();
        toolPanel.Controls.AddRange([_btnRefresh, _btnEdit, _btnDelete]);

        var gridPanel = new Panel { Dock = DockStyle.Fill };
        gridPanel.Controls.Add(_grid);
        gridPanel.Controls.Add(toolPanel);

        main.Panel1.Controls.Add(_tree);
        main.Panel2.Controls.Add(right);
        right.Panel1.Controls.Add(gridPanel);
        right.Panel2.Controls.Add(_details);

        Controls.Add(main);
    }

    private async Task RefreshCatalogAsync()
    {
        try
        {
            _currentItems = await _catalog.LoadAsync();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load catalog: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyFilter()
    {
        var mode = _tree.SelectedNode?.Name;
        IEnumerable<PodcastItemView> filtered = _currentItems;

        if (mode == "Mine")
            filtered = _currentItems.Where(x => x.IsOwner);

        _grid.DataSource = filtered.ToList();
        UpdateDetails();
    }

    private PodcastItemView? Selected()
        => _grid.CurrentRow?.DataBoundItem as PodcastItemView;

    private void UpdateDetails()
    {
        var selected = Selected();
        _details.SelectedObject = selected;

        var owner = selected?.IsOwner == true;
        _btnEdit.Enabled = owner;
        _btnDelete.Enabled = owner;
    }

    private async Task EditSelectedAsync()
    {
        var selected = Selected();
        if (selected is null) return;

        var newTitle = Prompt("Nuovo titolo", selected.Title);
        if (newTitle is null) return;

        var newDesc = Prompt("Nuova descrizione", string.Empty) ?? string.Empty;
        var newImage = Prompt("Nuovo URL immagine", string.Empty);

        var result = await _editService.UpdateAsync(selected.PodcastId, newTitle, newDesc, string.IsNullOrWhiteSpace(newImage) ? null : newImage);
        if (!result.Success)
        {
            MessageBox.Show(result.Error ?? "Update failed.", "Update metadata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await RefreshCatalogAsync();
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = Selected();
        if (selected is null) return;

        var confirm = MessageBox.Show(
            $"Eliminare '{selected.Title}'? Questa azione verrà propagata nella rete.",
            "Conferma eliminazione",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes) return;

        var result = await _deleteService.DeleteAsync(selected.PodcastId, "Deleted from WindowsClient");
        if (!result.Success)
        {
            MessageBox.Show(result.Error ?? "Delete failed.", "Delete podcast", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await RefreshCatalogAsync();
    }

    private static string? Prompt(string title, string initialValue)
    {
        using var form = new Form { Width = 560, Height = 160, Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent };
        var text = new TextBox { Left = 16, Top = 16, Width = 510, Text = initialValue };
        var ok = new Button { Text = "OK", Left = 370, Width = 75, Top = 60, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 451, Width = 75, Top = 60, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange([text, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        return form.ShowDialog() == DialogResult.OK ? text.Text.Trim() : null;
    }
}
