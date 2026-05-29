using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Revit_Command_Centre.Services;
using Revit_Command_Centre.UI;

namespace Revit_Command_Centre.Modules.BulkParameters
{
    public partial class BulkParametersView : UserControl
    {
        private readonly UIApplication _uiApp;

        // Category list (populated by FetchCategories callback)
        private List<(ElementId Id, string Name, int Count)> _categories = new();
        private int _selectedCategoryIndex = -1;
        private Picker? _categoryPicker;

        // Parameter list (populated by FetchGrid callback)
        private List<ParameterInfo> _availableParams = new();
        private readonly List<string> _selectedParams = new();

        // Grid state
        private DataTable?  _currentData;     // bound to BulkGrid
        private DataTable?  _originalData;    // snapshot before any edits
        private List<ParameterRow> _currentRows = new();
        private readonly Dictionary<(int elementId, string col), string> _pendingEdits = new();
        private readonly HashSet<int> _modifiedElementIds = new();

        private static readonly SolidColorBrush AmberLight  = Frozen(0xFF, 0xFF, 0xF3, 0xCD);
        private static readonly SolidColorBrush TxtPrimary  = Frozen(0xFF, 0x1A, 0x1A, 0x1A);
        private static readonly SolidColorBrush TxtSecond   = Frozen(0xFF, 0x6B, 0x6B, 0x6B);
        private static readonly SolidColorBrush BorderC     = Frozen(0x1E, 0x00, 0x00, 0x00);
        private static readonly SolidColorBrush ReadOnlyBg  = Frozen(0xFF, 0xF5, 0xF5, 0xF5);
        private static readonly FontFamily      AppFont     = new("Segoe UI");

        private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
        {
            var b2 = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            b2.Freeze();
            return b2;
        }

        public BulkParametersView(UIApplication uiApp)
        {
            _uiApp = uiApp;
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            FillDownContainer.Children.Add(
                PickerHelper.MakeButton("Fill Down", FillDown_Click));
            DiscardContainer.Children.Add(
                PickerHelper.MakeButton("Discard changes", DiscardChanges_Click));
            CommitContainer.Children.Add(
                PickerHelper.MakeButton("Commit to model", CommitChanges_Click));

            FetchCategories();
        }

        // ── category loading ──────────────────────────────────────────────────────────────

        private void FetchCategories()
        {
            if (App.BulkParamHandler == null || App.BulkParamEvent == null) return;
            App.BulkParamHandler.Mode              = BulkParameterEventHandler.OperationMode.FetchCategories;
            App.BulkParamHandler.OnCategoriesLoaded = OnCategoriesLoaded;
            App.BulkParamEvent.Raise();
        }

        private void OnCategoriesLoaded(List<(ElementId Id, string Name, int Count)> categories)
        {
            _categories = categories;
            if (categories.Count == 0)
            {
                CmbCategory.Children.Clear();
                CmbCategory.Children.Add(new TextBlock
                {
                    Text = "No model categories found.", FontFamily = AppFont, FontSize = 11,
                    Foreground = TxtSecond
                });
                return;
            }

            var options = categories.Select(c => $"{c.Name} ({c.Count})").ToArray();
            _categoryPicker = new Picker(options);
            PickerHelper.Refresh(CmbCategory, _categoryPicker, OnCategoryPickerChanged);
        }

        private void OnCategoryPickerChanged()
        {
            if (_categoryPicker == null || _categories.Count == 0) return;
            _selectedCategoryIndex = _categoryPicker.Index;
            _selectedParams.Clear();
            ClearGrid();
            FetchGrid();
        }

        // ── grid loading ──────────────────────────────────────────────────────────────────

        private void FetchGrid()
        {
            if (App.BulkParamHandler == null || App.BulkParamEvent == null) return;
            if (_selectedCategoryIndex < 0 || _selectedCategoryIndex >= _categories.Count) return;

            var catId = _categories[_selectedCategoryIndex].Id;
            App.BulkParamHandler.Mode               = BulkParameterEventHandler.OperationMode.FetchGrid;
            App.BulkParamHandler.SelectedCategoryId = catId;
            App.BulkParamHandler.SelectedParameters = new List<string>(_selectedParams);
            App.BulkParamHandler.OnGridLoaded       = OnGridLoaded;

            if (App.BulkParamEvent.Raise() != ExternalEventRequest.Accepted)
                TxtEditCount.Text = "Loading…";
        }

        private void OnGridLoaded(List<ParameterInfo> parms, List<ParameterRow> rows)
        {
            _availableParams = parms;
            _currentRows     = rows;
            RebuildParamPicker();
            RebuildDataGrid(rows);
        }

        private void RebuildParamPicker()
        {
            SelectedParamsPanel.Children.Clear();
            AddParamContainer.Children.Clear();

            // Show selected param chips
            foreach (string param in _selectedParams)
            {
                string capture = param;
                var chip = new Border
                {
                    Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(8, 2, 8, 2),
                    CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                    BorderBrush = BorderC, BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Child = new TextBlock { Text = capture + " ✕", FontFamily = AppFont, FontSize = 11, Foreground = TxtPrimary }
                };
                chip.MouseLeftButtonUp += (s, e) =>
                {
                    _selectedParams.Remove(capture);
                    FetchGrid();
                };
                SelectedParamsPanel.Children.Add(chip);
            }

            // Add parameter picker — show unselected params
            var unselected = _availableParams
                .Where(p => !_selectedParams.Contains(p.Name))
                .Select(p => p.Name)
                .ToArray();

            if (unselected.Length == 0) return;

            var addPicker = new Picker(new[] { "+ Add parameter…" }.Concat(unselected).ToArray());
            PickerHelper.Refresh(AddParamContainer, addPicker, () =>
            {
                if (addPicker.Index == 0) return;
                string paramName = addPicker.Options[addPicker.Index];
                if (!_selectedParams.Contains(paramName))
                {
                    _selectedParams.Add(paramName);
                    FetchGrid();
                }
            });
        }

        private void RebuildDataGrid(List<ParameterRow> rows)
        {
            _pendingEdits.Clear();
            _modifiedElementIds.Clear();
            BulkGrid.Columns.Clear();

            if (_selectedParams.Count == 0 || rows.Count == 0)
            {
                ClearGrid();
                return;
            }

            // Build DataTable
            var dt = new DataTable();
            dt.Columns.Add("_ElementId", typeof(int));
            dt.Columns.Add("Element",    typeof(string));
            foreach (string p in _selectedParams)
                dt.Columns.Add(p, typeof(string));

            foreach (var row in rows)
            {
                var dr = dt.NewRow();
                dr["_ElementId"] = row.ElementId.IntegerValue;
                dr["Element"]    = row.DisplayName;
                foreach (string p in _selectedParams)
                    dr[p] = row.Values.GetValueOrDefault(p, "");
                dt.Rows.Add(dr);
            }

            _originalData = dt.Copy();
            _currentData  = dt;

            // Build columns (AutoGenerateColumns=False)
            BulkGrid.Columns.Add(new DataGridTextColumn
            {
                Header     = "_ElementId",
                Binding    = new Binding("_ElementId") { Mode = BindingMode.OneWay },
                Visibility = Visibility.Collapsed
            });
            BulkGrid.Columns.Add(new DataGridTextColumn
            {
                Header     = "Element",
                Binding    = new Binding("Element") { Mode = BindingMode.OneWay },
                IsReadOnly = true,
                Width      = new DataGridLength(160)
            });

            foreach (string paramName in _selectedParams)
            {
                bool isReadOnly = _availableParams
                    .FirstOrDefault(p => p.Name == paramName)?.IsReadOnly ?? true;

                BulkGrid.Columns.Add(new DataGridTextColumn
                {
                    Header     = paramName,
                    Binding    = new Binding(paramName),
                    IsReadOnly = isReadOnly,
                    Width      = new DataGridLength(1, DataGridLengthUnitType.Star)
                });
            }

            // Apply filter if active
            var view = dt.DefaultView;
            string filter = TxtFilter.Text.Trim();
            if (!string.IsNullOrEmpty(filter))
                view.RowFilter = $"Element LIKE '%{filter.Replace("'", "''")}%'";

            BulkGrid.ItemsSource = view;
            UpdateEditCount();
        }

        private void ClearGrid()
        {
            _currentData = null;
            _originalData = null;
            BulkGrid.Columns.Clear();
            BulkGrid.ItemsSource = null;
            _pendingEdits.Clear();
            _modifiedElementIds.Clear();
            UpdateEditCount();
        }

        // ── DataGrid events ───────────────────────────────────────────────────────────────

        private void BulkGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            var drv = e.Row.Item as DataRowView;
            if (drv == null) return;
            int elemId = (int)drv["_ElementId"];
            e.Row.Background = _modifiedElementIds.Contains(elemId) ? AmberLight : Brushes.Transparent;
        }

        private void BulkGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            if (e.EditingElement is not System.Windows.Controls.TextBox tb) return;

            string colName = e.Column.Header?.ToString() ?? "";
            if (string.IsNullOrEmpty(colName) || colName == "_ElementId" || colName == "Element") return;

            var drv = (DataRowView)e.Row.Item;
            int elemId = (int)drv["_ElementId"];
            string newValue = tb.Text;

            string origValue = "";
            if (_originalData != null)
            {
                var origRows = _originalData.Select($"_ElementId = {elemId}");
                if (origRows.Length > 0)
                    origValue = origRows[0][colName]?.ToString() ?? "";
            }

            var key = (elemId, colName);
            if (newValue != origValue)
            {
                _pendingEdits[key] = newValue;
                _modifiedElementIds.Add(elemId);
            }
            else
            {
                _pendingEdits.Remove(key);
                if (!_pendingEdits.Keys.Any(k => k.elementId == elemId))
                    _modifiedElementIds.Remove(elemId);
            }

            e.Row.Background = _modifiedElementIds.Contains(elemId) ? AmberLight : Brushes.Transparent;
            UpdateEditCount();
        }

        private void TxtFilter_Changed(object sender, TextChangedEventArgs e)
        {
            if (_currentData == null) return;
            string filter = TxtFilter.Text.Trim();
            _currentData.DefaultView.RowFilter = string.IsNullOrEmpty(filter)
                ? ""
                : $"Element LIKE '%{filter.Replace("'", "''")}%'";
        }

        // ── fill down ─────────────────────────────────────────────────────────────────────

        private void FillDown_Click(object sender, MouseButtonEventArgs e)
        {
            var selected = BulkGrid.SelectedCells.ToList();
            if (selected.Count < 2) return;

            var firstCell = selected[0];
            string colName = firstCell.Column.Header?.ToString() ?? "";
            if (colName == "_ElementId" || colName == "Element") return;

            if (_currentData == null) return;
            var firstDrv = firstCell.Item as DataRowView;
            if (firstDrv == null) return;
            string fillValue = firstDrv[colName]?.ToString() ?? "";

            foreach (var cell in selected.Skip(1))
            {
                if (cell.Column.Header?.ToString() != colName) continue;
                var drv = cell.Item as DataRowView;
                if (drv == null) continue;
                int elemId = (int)drv["_ElementId"];

                drv.BeginEdit();
                drv[colName] = fillValue;
                drv.EndEdit();

                string origValue = "";
                if (_originalData != null)
                {
                    var origRows = _originalData.Select($"_ElementId = {elemId}");
                    if (origRows.Length > 0)
                        origValue = origRows[0][colName]?.ToString() ?? "";
                }

                var key = (elemId, colName);
                if (fillValue != origValue)
                {
                    _pendingEdits[key] = fillValue;
                    _modifiedElementIds.Add(elemId);
                    var rowContainer = BulkGrid.ItemContainerGenerator.ContainerFromItem(drv) as DataGridRow;
                    if (rowContainer != null) rowContainer.Background = AmberLight;
                }
                else
                {
                    _pendingEdits.Remove(key);
                    if (!_pendingEdits.Keys.Any(k => k.elementId == elemId))
                    {
                        _modifiedElementIds.Remove(elemId);
                        var rowContainer = BulkGrid.ItemContainerGenerator.ContainerFromItem(drv) as DataGridRow;
                        if (rowContainer != null) rowContainer.Background = Brushes.Transparent;
                    }
                }
            }
            UpdateEditCount();
        }

        // ── discard / commit ──────────────────────────────────────────────────────────────

        private void DiscardChanges_Click(object sender, MouseButtonEventArgs e)
        {
            if (_currentRows.Count > 0)
                RebuildDataGrid(_currentRows);
        }

        private void CommitChanges_Click(object sender, MouseButtonEventArgs e)
        {
            if (_pendingEdits.Count == 0) return;
            if (App.BulkParamHandler == null || App.BulkParamEvent == null) return;
            if (_currentData == null) return;

            // Warn if any edits affect type parameters
            var typeParamEdits = _pendingEdits.Keys
                .Where(k =>
                {
                    var paramRow = _currentRows.FirstOrDefault(r => r.ElementId.IntegerValue == k.elementId);
                    return paramRow?.IsTypeParam.GetValueOrDefault(k.col, false) ?? false;
                }).ToList();

            if (typeParamEdits.Count > 0)
            {
                var td = new Autodesk.Revit.UI.TaskDialog("Bulk Parameter Edit")
                {
                    MainInstruction = $"{typeParamEdits.Count} edit(s) target Type parameters.",
                    MainContent     = "Type parameter edits will affect ALL instances of that type in the model.",
                    CommonButtons   = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No
                };
                if (td.Show() != TaskDialogResult.Yes) return;
            }

            // Build CellEdit list
            var edits = new List<CellEdit>();
            foreach (var kvp in _pendingEdits)
            {
                var paramRow = _currentRows.FirstOrDefault(r => r.ElementId.IntegerValue == kvp.Key.elementId);
                if (paramRow == null) continue;
                bool isType = paramRow.IsTypeParam.GetValueOrDefault(kvp.Key.col, false);
                edits.Add(new CellEdit(paramRow.ElementId, kvp.Key.col, kvp.Value, isType));
            }

            App.BulkParamHandler.Mode           = BulkParameterEventHandler.OperationMode.Commit;
            App.BulkParamHandler.PendingEdits   = edits;
            App.BulkParamHandler.OnCommitComplete = OnCommitComplete;

            if (App.BulkParamEvent.Raise() != ExternalEventRequest.Accepted)
                TxtEditCount.Text = "Another operation is in progress. Please try again.";
        }

        private void OnCommitComplete(CommitResult result)
        {
            string msg = $"Committed: {result.Succeeded}  Failed: {result.Failed}";
            if (result.Errors.Count > 0)
                msg += $"\n{string.Join("\n", result.Errors.Take(10))}";
            Autodesk.Revit.UI.TaskDialog.Show("Bulk Parameter Edit", msg);

            // Refresh grid from model to confirm written values
            FetchGrid();
        }

        private void UpdateEditCount()
        {
            int n = _pendingEdits.Count;
            TxtEditCount.Text = n == 0 ? "0 cells edited"
                : $"{n} cell{(n == 1 ? "" : "s")} edited";
        }
    }
}
