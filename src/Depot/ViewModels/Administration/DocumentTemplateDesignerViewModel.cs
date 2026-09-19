// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

using Depot.Commands;
using Depot.DocumentRendering;
using Depot.Services;

namespace Depot.ViewModels.Administration;

public sealed record DocumentTemplateVersionItem(DocumentTemplate Template)
{
	public int Version => Template.Version;
	public bool IsActive => Template.IsActive;
	public string DisplayName => $"v{Version} · {(IsActive ? "Active" : "Draft")} · {Template.Id}";
}

public sealed class DocumentDesignerElementViewModel : BaseViewModel
{
	private string _id;
	private double _x;
	private double _y;
	private double _width;
	private double _height;
	private string? _binding;
	private string? _text;
	private string _font;
	private double _fontSize;
	private DocumentTemplateFontWeight _fontWeight;
	private DocumentTemplateAlignment _alignment;
	private string? _format;
	private string? _visibility;
	private bool _border;
	private DocumentTemplatePlacement _placement;
	private bool _repeatOnEveryPage;
	private double _rowHeight;

	public DocumentDesignerElementViewModel(DocumentTemplateElement element)
	{
		ModelType = element.Type;
		_id = element.Id;
		_x = element.X;
		_y = element.Y;
		_width = element.Width;
		_height = element.Height;
		_binding = element.Binding;
		_text = element.Text;
		_font = element.Font;
		_fontSize = element.FontSize;
		_fontWeight = element.FontWeight;
		_alignment = element.Alignment;
		_format = element.Format;
		_visibility = element.Visibility;
		_border = element.Border;
		_placement = element.Placement;
		_repeatOnEveryPage = element.RepeatOnEveryPage;
		_rowHeight = element.RowHeight;
		Columns = element.Columns.Select(column => column with { }).ToArray();
	}

	public DocumentTemplateElementType ModelType { get; }
	public string ElementType => ModelType.ToString();
	public IReadOnlyList<DocumentTemplateColumn> Columns { get; }
	public string DisplayText => ModelType switch
	{
		DocumentTemplateElementType.Text => string.IsNullOrWhiteSpace(Text) ? "Text" : Text!,
		DocumentTemplateElementType.BoundText => string.IsNullOrWhiteSpace(Binding) ? "Bound Text" : $"{{{Binding}}}",
		DocumentTemplateElementType.Image => "Image / Logo",
		DocumentTemplateElementType.LineTable => "Line Table",
		DocumentTemplateElementType.TotalsBlock => "Totals Block",
		DocumentTemplateElementType.PageNumber => "Page Number",
		_ => ModelType.ToString()
	};

	public string Id { get => _id; set { if (_id == value) return; _id = value; Changed(); } }
	public double X { get => _x; set { if (Math.Abs(_x - value) < 0.001) return; _x = value; Changed(); } }
	public double Y { get => _y; set { if (Math.Abs(_y - value) < 0.001) return; _y = value; Changed(); } }
	public double Width { get => _width; set { if (Math.Abs(_width - value) < 0.001) return; _width = value; Changed(); } }
	public double Height { get => _height; set { if (Math.Abs(_height - value) < 0.001) return; _height = value; Changed(); } }
	public string? Binding { get => _binding; set { if (_binding == value) return; _binding = value; Changed(); OnPropertyChanged(nameof(DisplayText)); } }
	public string? Text { get => _text; set { if (_text == value) return; _text = value; Changed(); OnPropertyChanged(nameof(DisplayText)); } }
	public string Font { get => _font; set { if (_font == value) return; _font = value; Changed(); } }
	public double FontSize { get => _fontSize; set { if (Math.Abs(_fontSize - value) < 0.001) return; _fontSize = value; Changed(); } }
	public DocumentTemplateFontWeight FontWeight { get => _fontWeight; set { if (_fontWeight == value) return; _fontWeight = value; Changed(); } }
	public DocumentTemplateAlignment Alignment { get => _alignment; set { if (_alignment == value) return; _alignment = value; Changed(); } }
	public string? Format { get => _format; set { if (_format == value) return; _format = value; Changed(); } }
	public string? Visibility { get => _visibility; set { if (_visibility == value) return; _visibility = value; Changed(); } }
	public bool Border { get => _border; set { if (_border == value) return; _border = value; Changed(); } }
	public DocumentTemplatePlacement Placement { get => _placement; set { if (_placement == value) return; _placement = value; Changed(); } }
	public bool RepeatOnEveryPage { get => _repeatOnEveryPage; set { if (_repeatOnEveryPage == value) return; _repeatOnEveryPage = value; Changed(); } }
	public double RowHeight { get => _rowHeight; set { if (Math.Abs(_rowHeight - value) < 0.001) return; _rowHeight = value; Changed(); } }

	public event EventHandler? ModelChanged;

	public void Translate(double deltaX, double deltaY)
	{
		X += deltaX;
		Y += deltaY;
	}

	public void SetBounds(double x, double y, double width, double height)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
	}

	public DocumentTemplateElement ToModel() => new()
	{
		Id = Id.Trim(),
		Type = ModelType,
		X = X,
		Y = Y,
		Width = Width,
		Height = Height,
		Binding = NullIfWhiteSpace(Binding),
		Text = Text,
		Font = Font.Trim(),
		FontSize = FontSize,
		FontWeight = FontWeight,
		Alignment = Alignment,
		Format = NullIfWhiteSpace(Format),
		Visibility = NullIfWhiteSpace(Visibility),
		Border = Border,
		Placement = Placement,
		RepeatOnEveryPage = RepeatOnEveryPage,
		RowHeight = RowHeight,
		Columns = Columns.Select(column => column with { }).ToArray()
	};

	private void Changed([CallerMemberName] string? propertyName = null)
	{
		OnPropertyChanged(propertyName);
		ModelChanged?.Invoke(this, EventArgs.Empty);
	}

	private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DocumentTemplateDesignerViewModel : BaseViewModel
{
	private const int HistoryLimit = 100;
	private const double MinimumElementSize = 4d;
	private const double PdfPointToWpfDipScale = 96d / 72d;

	private readonly DocumentTemplateDesignerService _service;
	private readonly HashSet<DocumentDesignerElementViewModel> _selection = [];
	private readonly List<DesignerSnapshot> _undoHistory = [];
	private readonly List<DesignerSnapshot> _redoHistory = [];
	private DocumentTemplateType _selectedType;
	private DocumentTemplateVersionItem? _selectedVersion;
	private DocumentDesignerElementViewModel? _selectedElement;
	private string _templateName = string.Empty;
	private bool _isEditing;
	private bool _isDirty;
	private bool _showGrid = true;
	private bool _snapToGrid = true;
	private double _gridSize = 10;
	private double _zoomPercent = 90;
	private string _previewStatus = string.Empty;
	private bool _applyingSnapshot;
	private int _transactionDepth;
	private DesignerSnapshot? _transactionStart;
	private DesignerSnapshot? _lastSnapshot;
	private DesignerSnapshot? _cleanSnapshot;

	public DocumentTemplateDesignerViewModel(DocumentTemplateDesignerService service)
	{
		_service = service ?? throw new ArgumentNullException(nameof(service));
		DocumentTypes = Enum.GetValues<DocumentTemplateType>();
		Bindings = ["", .. service.Bindings];
		FontWeights = Enum.GetValues<DocumentTemplateFontWeight>();
		Alignments = Enum.GetValues<DocumentTemplateAlignment>();
		Placements = Enum.GetValues<DocumentTemplatePlacement>();

		NewFromDefaultCommand = new RelayCommand(NewFromDefault, () => CanManage && !IsDirty);
		DuplicateCommand = new RelayCommand(Duplicate, () => CanManage && IsEditing && Elements.Count > 0);
		CopySelectedVersionAsDraftCommand = new RelayCommand(CopySelectedVersionAsDraft, () => CanManage && !IsDirty && SelectedVersion is not null);
		DiscardChangesCommand = new RelayCommand(DiscardUnsavedChanges, () => IsDirty);
		SaveDraftCommand = new RelayCommand(SaveDraft, () => CanManage && IsEditing && IsDirty && ValidationErrors.Count == 0 && Elements.Count > 0);
		ActivateCommand = new RelayCommand(Activate, () => CanManage && !IsDirty && SelectedVersion is not null && !SelectedVersion.IsActive);
		ResetDefaultCommand = new RelayCommand(ResetDefault, () => CanManage && !IsDirty);
		PreviewCommand = new RelayCommand(Preview, () => Elements.Count > 0 && ValidationErrors.Count == 0);
		DuplicateElementsCommand = new RelayCommand(DuplicateSelectedElements, () => CanManage && IsEditing && _selection.Count > 0);
		DeleteElementsCommand = new RelayCommand(DeleteSelectedElements, () => CanManage && IsEditing && _selection.Count > 0);
		UndoCommand = new RelayCommand(Undo, () => IsEditing && _undoHistory.Count > 0);
		RedoCommand = new RelayCommand(Redo, () => IsEditing && _redoHistory.Count > 0);
		AlignLeftCommand = new RelayCommand(() => AlignSelection(AlignmentOperation.Left), CanAlignSelection);
		AlignCenterCommand = new RelayCommand(() => AlignSelection(AlignmentOperation.Center), CanAlignSelection);
		AlignRightCommand = new RelayCommand(() => AlignSelection(AlignmentOperation.Right), CanAlignSelection);
		AlignTopCommand = new RelayCommand(() => AlignSelection(AlignmentOperation.Top), CanAlignSelection);
		AlignMiddleCommand = new RelayCommand(() => AlignSelection(AlignmentOperation.Middle), CanAlignSelection);
		AlignBottomCommand = new RelayCommand(() => AlignSelection(AlignmentOperation.Bottom), CanAlignSelection);
		DistributeHorizontallyCommand = new RelayCommand(() => DistributeSelection(horizontal: true), CanDistributeSelection);
		DistributeVerticallyCommand = new RelayCommand(() => DistributeSelection(horizontal: false), CanDistributeSelection);
		SameWidthCommand = new RelayCommand(() => MatchSelectionSize(width: true), CanAlignSelection);
		SameHeightCommand = new RelayCommand(() => MatchSelectionSize(width: false), CanAlignSelection);

		_selectedType = DocumentTypes.First();
		ReloadVersions(selectActive: true);
	}

	public IReadOnlyList<DocumentTemplateType> DocumentTypes { get; }
	public IReadOnlyList<string> Bindings { get; }
	public IReadOnlyList<DocumentTemplateFontWeight> FontWeights { get; }
	public IReadOnlyList<DocumentTemplateAlignment> Alignments { get; }
	public IReadOnlyList<DocumentTemplatePlacement> Placements { get; }
	public ObservableCollection<DocumentTemplateVersionItem> Versions { get; } = [];
	public ObservableCollection<DocumentDesignerElementViewModel> Elements { get; } = [];
	public ObservableCollection<string> ValidationErrors { get; } = [];

	public RelayCommand NewFromDefaultCommand { get; }
	public RelayCommand DuplicateCommand { get; }
	public RelayCommand CopySelectedVersionAsDraftCommand { get; }
	public RelayCommand DiscardChangesCommand { get; }
	public RelayCommand SaveDraftCommand { get; }
	public RelayCommand ActivateCommand { get; }
	public RelayCommand ResetDefaultCommand { get; }
	public RelayCommand PreviewCommand { get; }
	public RelayCommand DuplicateElementsCommand { get; }
	public RelayCommand DeleteElementsCommand { get; }
	public RelayCommand UndoCommand { get; }
	public RelayCommand RedoCommand { get; }
	public RelayCommand AlignLeftCommand { get; }
	public RelayCommand AlignCenterCommand { get; }
	public RelayCommand AlignRightCommand { get; }
	public RelayCommand AlignTopCommand { get; }
	public RelayCommand AlignMiddleCommand { get; }
	public RelayCommand AlignBottomCommand { get; }
	public RelayCommand DistributeHorizontallyCommand { get; }
	public RelayCommand DistributeVerticallyCommand { get; }
	public RelayCommand SameWidthCommand { get; }
	public RelayCommand SameHeightCommand { get; }

	public bool CanManage => _service.CanManage;
	public string Breadcrumb => "Administration / Documents / Document Designer";
	public string Title => "Document Designer";
	public string Subtitle => "Design PDF output templates for Depot business documents.";
	public string ModeText => IsEditing
		? IsDirty ? "Editing draft · unsaved changes" : "Editing draft"
		: SelectedVersion is null ? "No template selected" : $"Read-only version {SelectedVersion.Version}";
	public double PageWidth => 595;
	public double PageHeight => 842;
	public bool CanChangeTemplateSelection => !IsDirty;

	public DocumentTemplateType SelectedType
	{
		get => _selectedType;
		set
		{
			if (_selectedType == value || IsDirty) return;
			_selectedType = value;
			OnPropertyChanged();
			ReloadVersions(selectActive: true);
		}
	}

	public DocumentTemplateVersionItem? SelectedVersion
	{
		get => _selectedVersion;
		set
		{
			if (ReferenceEquals(_selectedVersion, value) || IsDirty) return;
			_selectedVersion = value;
			OnPropertyChanged();
			if (value is not null) LoadTemplate(value.Template, editing: false, markDirty: false);
			RaiseCommandStates();
		}
	}

	public DocumentDesignerElementViewModel? SelectedElement
	{
		get => _selectedElement;
		private set
		{
			if (ReferenceEquals(_selectedElement, value)) return;
			_selectedElement = value;
			OnPropertyChanged();
			RaiseInspectorProperties();
		}
	}

	public string TemplateName
	{
		get => _templateName;
		set
		{
			if (_templateName == value || _applyingSnapshot) return;
			var ownsTransaction = _transactionDepth == 0;
			if (ownsTransaction) BeginEditTransaction();
			_templateName = value;
			OnPropertyChanged();
			UpdateValidation();
			UpdateDirtyState();
			if (ownsTransaction) EndEditTransaction();
		}
	}

	public bool IsEditing
	{
		get => _isEditing;
		private set
		{
			if (_isEditing == value) return;
			_isEditing = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(IsReadOnly));
			OnPropertyChanged(nameof(ModeText));
			RaiseCommandStates();
		}
	}

	public bool IsDirty
	{
		get => _isDirty;
		private set
		{
			if (_isDirty == value) return;
			_isDirty = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(CanChangeTemplateSelection));
			OnPropertyChanged(nameof(ModeText));
			RaiseCommandStates();
		}
	}

	public bool IsReadOnly => !IsEditing;
	public bool ShowGrid { get => _showGrid; set { if (_showGrid == value) return; _showGrid = value; OnPropertyChanged(); } }
	public bool SnapToGrid { get => _snapToGrid; set { if (_snapToGrid == value) return; _snapToGrid = value; OnPropertyChanged(); } }
	public double GridSize { get => _gridSize; set { var normalized = Math.Clamp(value, 1, 100); if (Math.Abs(_gridSize - normalized) < 0.001) return; _gridSize = normalized; OnPropertyChanged(); } }
	public double ZoomPercent { get => _zoomPercent; set { var normalized = Math.Clamp(value, 25, 200); if (Math.Abs(_zoomPercent - normalized) < 0.001) return; _zoomPercent = normalized; OnPropertyChanged(); OnPropertyChanged(nameof(ZoomScale)); } }
	public double ZoomScale => (ZoomPercent / 100d) * PdfPointToWpfDipScale;
	public string PreviewStatus { get => _previewStatus; private set { if (_previewStatus == value) return; _previewStatus = value; OnPropertyChanged(); } }
	public string ValidationSummary => ValidationErrors.Count == 0 ? "Template is valid." : $"{ValidationErrors.Count} validation issue(s)";
	public bool HasSelectedElement => SelectedElement is not null;
	public bool ShowBindingProperty => SelectedElement?.ModelType is DocumentTemplateElementType.BoundText or DocumentTemplateElementType.Image;
	public bool ShowTextProperty => SelectedElement?.ModelType == DocumentTemplateElementType.Text;
	public bool ShowTypographyProperties => SelectedElement?.ModelType is DocumentTemplateElementType.Text or DocumentTemplateElementType.BoundText or DocumentTemplateElementType.PageNumber;
	public bool ShowFormatProperty => SelectedElement?.ModelType is DocumentTemplateElementType.BoundText or DocumentTemplateElementType.PageNumber;
	public bool ShowVisibilityProperty => SelectedElement?.ModelType is DocumentTemplateElementType.Text or DocumentTemplateElementType.BoundText or DocumentTemplateElementType.Image or DocumentTemplateElementType.Rectangle or DocumentTemplateElementType.TotalsBlock or DocumentTemplateElementType.PageNumber;
	public bool ShowBorderProperty => SelectedElement?.ModelType is DocumentTemplateElementType.Text or DocumentTemplateElementType.BoundText or DocumentTemplateElementType.Rectangle;
	public bool ShowPlacementProperty => SelectedElement is { ModelType: not DocumentTemplateElementType.Line and not DocumentTemplateElementType.PageNumber };
	public bool ShowRepeatProperty => SelectedElement?.ModelType is DocumentTemplateElementType.Text or DocumentTemplateElementType.BoundText or DocumentTemplateElementType.Image or DocumentTemplateElementType.LineTable or DocumentTemplateElementType.PageNumber;
	public bool ShowRowHeightProperty => SelectedElement?.ModelType == DocumentTemplateElementType.LineTable;
	public bool ShowLineTableHint => SelectedElement?.ModelType == DocumentTemplateElementType.LineTable;

	public Task LoadAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!IsDirty) ReloadVersions(selectActive: SelectedVersion is null);
		return Task.CompletedTask;
	}

	public void AddElement(DocumentTemplateElementType type)
	{
		if (!CanManage || !IsEditing) return;
		RunEdit(() =>
		{
			var editor = new DocumentDesignerElementViewModel(CreateElement(type, Elements.Count));
			AddEditor(editor);
			SetSelectedElements([editor]);
		});
	}

	public void SetSelectedElements(IEnumerable<DocumentDesignerElementViewModel> elements)
	{
		_selection.Clear();
		foreach (var element in elements.Where(Elements.Contains)) _selection.Add(element);
		SelectedElement = _selection.LastOrDefault();
		OnPropertyChanged(nameof(HasSelectedElement));
		RaiseCommandStates();
	}

	public void BeginEditTransaction()
	{
		if (!IsEditing || _applyingSnapshot) return;
		if (_transactionDepth == 0) _transactionStart = CaptureSnapshot();
		_transactionDepth++;
	}

	public void EndEditTransaction()
	{
		if (_transactionDepth <= 0) return;
		_transactionDepth--;
		if (_transactionDepth > 0) return;

		var before = _transactionStart;
		_transactionStart = null;
		var after = CaptureSnapshot();
		if (before is not null && !SnapshotsEqual(before, after))
		{
			PushUndo(before);
			_redoHistory.Clear();
		}
		_lastSnapshot = after;
		UpdateValidation();
		UpdateDirtyState();
		RaiseCommandStates();
	}

	public void MoveSelection(double deltaX, double deltaY)
	{
		if (!IsEditing || _selection.Count == 0) return;
		var ownsTransaction = _transactionDepth == 0;
		if (ownsTransaction) BeginEditTransaction();
		try
		{
			var selected = _selection.ToArray();
			if (SnapToGrid)
			{
				var anchor = SelectedElement ?? selected[0];
				deltaX = Snap(anchor.X + deltaX) - anchor.X;
				deltaY = Snap(anchor.Y + deltaY) - anchor.Y;
			}

			var minX = selected.Min(element => element.X);
			var minY = selected.Min(element => element.Y);
			var maxRight = selected.Max(element => element.X + Math.Max(0, element.Width));
			var maxBottom = selected.Max(element => element.Y + Math.Max(0, element.Height));
			deltaX = Math.Clamp(deltaX, -minX, PageWidth - maxRight);
			deltaY = Math.Clamp(deltaY, -minY, PageHeight - maxBottom);

			foreach (var element in selected) element.Translate(deltaX, deltaY);
		}
		finally
		{
			if (ownsTransaction) EndEditTransaction();
		}
	}

	public void ResizeElement(DocumentDesignerElementViewModel element, string handle, double deltaX, double deltaY)
	{
		if (!IsEditing || !Elements.Contains(element) || string.IsNullOrWhiteSpace(handle)) return;
		var ownsTransaction = _transactionDepth == 0;
		if (ownsTransaction) BeginEditTransaction();
		try
		{
			var direction = handle.Trim().ToUpperInvariant();
			var left = element.X;
			var top = element.Y;
			var right = element.X + Math.Max(MinimumElementSize, element.Width);
			var bottom = element.Y + Math.Max(element.ModelType == DocumentTemplateElementType.Line ? 0 : MinimumElementSize, element.Height);

			if (direction.Contains('W')) left = SnapIfEnabled(left + deltaX);
			if (direction.Contains('E')) right = SnapIfEnabled(right + deltaX);
			if (direction.Contains('N')) top = SnapIfEnabled(top + deltaY);
			if (direction.Contains('S')) bottom = SnapIfEnabled(bottom + deltaY);

			left = Math.Clamp(left, 0, PageWidth);
			right = Math.Clamp(right, 0, PageWidth);
			top = Math.Clamp(top, 0, PageHeight);
			bottom = Math.Clamp(bottom, 0, PageHeight);

			var minimumHeight = element.ModelType == DocumentTemplateElementType.Line ? 0d : MinimumElementSize;
			if (right - left < MinimumElementSize)
			{
				if (direction.Contains('W')) left = Math.Max(0, right - MinimumElementSize);
				else right = Math.Min(PageWidth, left + MinimumElementSize);
			}
			if (bottom - top < minimumHeight)
			{
				if (direction.Contains('N')) top = Math.Max(0, bottom - minimumHeight);
				else bottom = Math.Min(PageHeight, top + minimumHeight);
			}

			element.SetBounds(left, top, Math.Max(MinimumElementSize, right - left), Math.Max(minimumHeight, bottom - top));
			SetSelectedElements([element]);
		}
		finally
		{
			if (ownsTransaction) EndEditTransaction();
		}
	}

	public void DiscardUnsavedChanges()
	{
		if (!IsDirty) return;
		if (SelectedVersion is { } selected)
		{
			LoadTemplate(selected.Template, editing: false, markDirty: false);
			return;
		}
		ReloadVersions(selectActive: true);
	}

	private void NewFromDefault()
	{
		var source = _service.GetDefault(SelectedType);
		LoadTemplate(source with { Id = $"{SelectedType.ToString().ToLowerInvariant()}-custom", Version = 0, IsActive = false }, editing: true, markDirty: true);
	}

	private void Duplicate()
	{
		if (!IsEditing || Elements.Count == 0) return;
		var source = BuildTemplate(1, false);
		LoadTemplate(source with { Id = UniqueTemplateName($"{source.Id}-copy"), Version = 0, IsActive = false }, editing: true, markDirty: true);
	}

	private void CopySelectedVersionAsDraft()
	{
		if (SelectedVersion is null) return;
		var source = SelectedVersion.Template;
		LoadTemplate(source with { Id = UniqueTemplateName($"{source.Id}-draft-copy"), Version = 0, IsActive = false }, editing: true, markDirty: true);
	}

	private void SaveDraft()
	{
		try
		{
			var saved = _service.SaveDraft(BuildTemplate(1, false));
			ReloadVersions(selectActive: false, forceSelection: saved.Version);
			CompleteOperation(statusText: $"Draft v{saved.Version} saved.");
		}
		catch (Exception exception) { FailOperation(exception, "Template draft could not be saved"); }
	}

	private void Activate()
	{
		if (SelectedVersion is null) return;
		try
		{
			var active = _service.Activate(SelectedType, SelectedVersion.Version);
			ReloadVersions(selectActive: false, forceSelection: active.Version);
			CompleteOperation(statusText: $"Version {active.Version} activated.");
		}
		catch (Exception exception) { FailOperation(exception, "Template version could not be activated"); }
	}

	private void ResetDefault()
	{
		try
		{
			var active = _service.ResetToDefault(SelectedType);
			ReloadVersions(selectActive: false, forceSelection: active.Version);
			CompleteOperation(statusText: "Default template activated.");
		}
		catch (Exception exception) { FailOperation(exception, "Default template could not be restored"); }
	}

	private void Preview()
	{
		try
		{
			var bytes = _service.Preview(BuildTemplate(1, false));
			var directory = Path.Combine(Path.GetTempPath(), "Depot", "DocumentDesigner");
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, $"{SelectedType}-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.pdf");
			File.WriteAllBytes(path, bytes);
			PreviewStatus = $"Preview: {path}";
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
			CompleteOperation(statusText: "Preview generated from safe sample data.");
		}
		catch (Exception exception) { FailOperation(exception, "Preview could not be generated"); }
	}

	private void DuplicateSelectedElements()
	{
		if (!IsEditing) return;
		RunEdit(() =>
		{
			var copies = _selection.Select((element, index) =>
			{
				var model = element.ToModel();
				return new DocumentDesignerElementViewModel(model with
				{
					Id = UniqueElementId($"{model.Id}-copy"),
					X = Math.Min(PageWidth - Math.Max(0, model.Width), model.X + 10 + index * 2),
					Y = Math.Min(PageHeight - Math.Max(0, model.Height), model.Y + 10 + index * 2)
				});
			}).ToArray();
			foreach (var copy in copies) AddEditor(copy);
			SetSelectedElements(copies);
		});
	}

	private void DeleteSelectedElements()
	{
		if (!IsEditing) return;
		RunEdit(() =>
		{
			foreach (var element in _selection.ToArray())
			{
				element.ModelChanged -= OnElementChanged;
				Elements.Remove(element);
			}
			SetSelectedElements([]);
		});
	}

	private void Undo()
	{
		if (!IsEditing || _undoHistory.Count == 0 || _transactionDepth > 0) return;
		var current = CaptureSnapshot();
		var target = _undoHistory[^1];
		_undoHistory.RemoveAt(_undoHistory.Count - 1);
		_redoHistory.Add(current);
		ApplySnapshot(target);
		_lastSnapshot = CaptureSnapshot();
		UpdateValidation();
		UpdateDirtyState();
		RaiseCommandStates();
	}

	private void Redo()
	{
		if (!IsEditing || _redoHistory.Count == 0 || _transactionDepth > 0) return;
		var current = CaptureSnapshot();
		var target = _redoHistory[^1];
		_redoHistory.RemoveAt(_redoHistory.Count - 1);
		PushUndo(current);
		ApplySnapshot(target);
		_lastSnapshot = CaptureSnapshot();
		UpdateValidation();
		UpdateDirtyState();
		RaiseCommandStates();
	}

	private void AlignSelection(AlignmentOperation operation)
	{
		if (!CanAlignSelection()) return;
		RunEdit(() =>
		{
			var selected = _selection.ToArray();
			var left = selected.Min(element => element.X);
			var top = selected.Min(element => element.Y);
			var right = selected.Max(element => element.X + element.Width);
			var bottom = selected.Max(element => element.Y + element.Height);
			var center = (left + right) / 2d;
			var middle = (top + bottom) / 2d;

			foreach (var element in selected)
			{
				var x = operation switch
				{
					AlignmentOperation.Left => left,
					AlignmentOperation.Center => center - element.Width / 2d,
					AlignmentOperation.Right => right - element.Width,
					_ => element.X
				};
				var y = operation switch
				{
					AlignmentOperation.Top => top,
					AlignmentOperation.Middle => middle - element.Height / 2d,
					AlignmentOperation.Bottom => bottom - element.Height,
					_ => element.Y
				};
				element.SetBounds(
					Math.Clamp(SnapIfEnabled(x), 0, Math.Max(0, PageWidth - element.Width)),
					Math.Clamp(SnapIfEnabled(y), 0, Math.Max(0, PageHeight - element.Height)),
					element.Width,
					element.Height);
			}
		});
	}

	private void DistributeSelection(bool horizontal)
	{
		if (!CanDistributeSelection()) return;
		RunEdit(() =>
		{
			var selected = horizontal
				? _selection.OrderBy(element => element.X).ToArray()
				: _selection.OrderBy(element => element.Y).ToArray();

			if (horizontal)
			{
				var left = selected.First().X;
				var right = selected.Last().X + selected.Last().Width;
				var available = right - left - selected.Sum(element => element.Width);
				var gap = available / (selected.Length - 1);
				var cursor = left;
				foreach (var element in selected)
				{
					element.X = Math.Clamp(SnapIfEnabled(cursor), 0, Math.Max(0, PageWidth - element.Width));
					cursor += element.Width + gap;
				}
			}
			else
			{
				var top = selected.First().Y;
				var bottom = selected.Last().Y + selected.Last().Height;
				var available = bottom - top - selected.Sum(element => element.Height);
				var gap = available / (selected.Length - 1);
				var cursor = top;
				foreach (var element in selected)
				{
					element.Y = Math.Clamp(SnapIfEnabled(cursor), 0, Math.Max(0, PageHeight - element.Height));
					cursor += element.Height + gap;
				}
			}
		});
	}

	private void MatchSelectionSize(bool width)
	{
		if (!CanAlignSelection()) return;
		RunEdit(() =>
		{
			var reference = SelectedElement ?? _selection.First();
			foreach (var element in _selection)
			{
				if (width)
				element.Width = Math.Min(reference.Width, PageWidth - element.X);
				else
				element.Height = Math.Min(reference.Height, PageHeight - element.Y);
			}
		});
	}

	private bool CanAlignSelection() => CanManage && IsEditing && _selection.Count >= 2;
	private bool CanDistributeSelection() => CanManage && IsEditing && _selection.Count >= 3;

	private void ReloadVersions(bool selectActive, int? forceSelection = null)
	{
		var currentVersion = forceSelection ?? (selectActive ? null : SelectedVersion?.Version);
		Versions.Clear();
		foreach (var template in _service.ListVersions(SelectedType)) Versions.Add(new DocumentTemplateVersionItem(template));
		var target = currentVersion is int version
			? Versions.FirstOrDefault(item => item.Version == version)
			: Versions.FirstOrDefault(item => item.IsActive);

		_selectedVersion = target ?? Versions.LastOrDefault();
		OnPropertyChanged(nameof(SelectedVersion));
		if (_selectedVersion is not null) LoadTemplate(_selectedVersion.Template, editing: false, markDirty: false);
		RaiseCommandStates();
	}

	private void LoadTemplate(DocumentTemplate template, bool editing, bool markDirty)
	{
		_applyingSnapshot = true;
		try
		{
			foreach (var existing in Elements) existing.ModelChanged -= OnElementChanged;
			Elements.Clear();
			foreach (var element in template.Elements) AddEditor(new DocumentDesignerElementViewModel(CloneElement(element)));
			_templateName = template.Id;
			OnPropertyChanged(nameof(TemplateName));
			_selection.Clear();
			SelectedElement = null;
			OnPropertyChanged(nameof(HasSelectedElement));
		}
		finally
		{
			_applyingSnapshot = false;
		}

		IsEditing = editing;
		_undoHistory.Clear();
		_redoHistory.Clear();
		_transactionDepth = 0;
		_transactionStart = null;
		var snapshot = CaptureSnapshot();
		_lastSnapshot = snapshot;
		_cleanSnapshot = markDirty ? null : snapshot;
		UpdateValidation();
		UpdateDirtyState();
		RaiseCommandStates();
	}

	private void AddEditor(DocumentDesignerElementViewModel editor)
	{
		editor.ModelChanged += OnElementChanged;
		Elements.Add(editor);
	}

	private void OnElementChanged(object? sender, EventArgs e)
	{
		if (_applyingSnapshot) return;
		if (_transactionDepth > 0)
		{
			UpdateValidation();
			UpdateDirtyState();
			return;
		}

		var before = _lastSnapshot ?? CaptureSnapshot();
		var after = CaptureSnapshot();
		if (!SnapshotsEqual(before, after))
		{
			PushUndo(before);
			_redoHistory.Clear();
		}
		_lastSnapshot = after;
		UpdateValidation();
		UpdateDirtyState();
		RaiseCommandStates();
	}

	private void RunEdit(Action action)
	{
		if (!IsEditing) return;
		var ownsTransaction = _transactionDepth == 0;
		if (ownsTransaction) BeginEditTransaction();
		try { action(); }
		finally { if (ownsTransaction) EndEditTransaction(); }
	}

	private void ApplySnapshot(DesignerSnapshot snapshot)
	{
		_applyingSnapshot = true;
		try
		{
			foreach (var existing in Elements) existing.ModelChanged -= OnElementChanged;
			Elements.Clear();
			foreach (var element in snapshot.Elements) AddEditor(new DocumentDesignerElementViewModel(CloneElement(element)));
			_templateName = snapshot.TemplateName;
			OnPropertyChanged(nameof(TemplateName));
			_selection.Clear();
			SelectedElement = null;
			OnPropertyChanged(nameof(HasSelectedElement));
		}
		finally
		{
			_applyingSnapshot = false;
		}
	}

	private DesignerSnapshot CaptureSnapshot() =>
		new(TemplateName, Elements.Select(element => CloneElement(element.ToModel())).ToArray());

	private bool SnapshotsEqual(DesignerSnapshot left, DesignerSnapshot right) =>
		string.Equals(SnapshotFingerprint(left), SnapshotFingerprint(right), StringComparison.Ordinal);

	private static string SnapshotFingerprint(DesignerSnapshot snapshot) =>
		JsonSerializer.Serialize(snapshot);

	private void PushUndo(DesignerSnapshot snapshot)
	{
		_undoHistory.Add(snapshot);
		if (_undoHistory.Count > HistoryLimit) _undoHistory.RemoveAt(0);
	}

	private void UpdateDirtyState()
	{
		if (!IsEditing)
		{
			IsDirty = false;
			return;
		}
		var current = CaptureSnapshot();
		IsDirty = _cleanSnapshot is null || !SnapshotsEqual(_cleanSnapshot, current);
	}

	private void UpdateValidation()
	{
		ValidationErrors.Clear();
		if (Elements.Count > 0)
		{
			foreach (var error in _service.Validate(BuildTemplate(1, false))) ValidationErrors.Add(error);
		}
		OnPropertyChanged(nameof(ValidationSummary));
		RaiseCommandStates();
	}

	private DocumentTemplate BuildTemplate(int version, bool active) => new()
	{
		Id = string.IsNullOrWhiteSpace(TemplateName) ? "untitled-template" : TemplateName.Trim(),
		Type = SelectedType,
		Version = version,
		IsActive = active,
		PageWidth = PageWidth,
		PageHeight = PageHeight,
		Elements = Elements.Select(element => element.ToModel()).ToArray()
	};

	private string UniqueElementId(string prefix)
	{
		var candidate = prefix;
		var suffix = 2;
		while (Elements.Any(element => string.Equals(element.Id, candidate, StringComparison.Ordinal)))
			candidate = $"{prefix}-{suffix++}";
		return candidate;
	}

	private string UniqueTemplateName(string prefix)
	{
		var candidate = prefix;
		var suffix = 2;
		var existing = Versions.Select(version => version.Template.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		while (existing.Contains(candidate)) candidate = $"{prefix}-{suffix++}";
		return candidate;
	}

	private double Snap(double value) => GridSize <= 0 ? value : Math.Round(value / GridSize) * GridSize;
	private double SnapIfEnabled(double value) => SnapToGrid ? Snap(value) : value;

	private static DocumentTemplateElement CloneElement(DocumentTemplateElement element) =>
		element with { Columns = element.Columns.Select(column => column with { }).ToArray() };

	private static DocumentTemplateElement CreateElement(DocumentTemplateElementType type, int index)
	{
		var id = $"{type.ToString().ToLowerInvariant()}-{index + 1}";
		var y = 40d + (index % 20) * 12d;
		return type switch
		{
			DocumentTemplateElementType.Text => new() { Id = id, Type = type, Text = "Text", X = 40, Y = y, Width = 180, Height = 24, FontSize = 10 },
			DocumentTemplateElementType.BoundText => new() { Id = id, Type = type, Binding = "Document.Number", X = 40, Y = y, Width = 180, Height = 24, FontSize = 10 },
			DocumentTemplateElementType.Image => new() { Id = id, Type = type, Binding = "Company.Logo", X = 40, Y = y, Width = 90, Height = 48 },
			DocumentTemplateElementType.Line => new() { Id = id, Type = type, X = 40, Y = y, Width = 240, Height = 0 },
			DocumentTemplateElementType.Rectangle => new() { Id = id, Type = type, X = 40, Y = y, Width = 180, Height = 60 },
			DocumentTemplateElementType.LineTable => new()
			{
				Id = id, Type = type, X = 40, Y = Math.Max(270, y), Width = 515, Height = 20, RowHeight = 20,
				Columns =
				[
					new("Item", "Line.Item", 75),
					new("Description", "Line.Description", 245),
					new("Qty", "Line.Quantity", 55, DocumentTemplateAlignment.Right),
					new("Unit", "Line.UnitPrice", 70, DocumentTemplateAlignment.Right),
					new("Total", "Line.Total", 70, DocumentTemplateAlignment.Right)
				]
			},
			DocumentTemplateElementType.TotalsBlock => new() { Id = id, Type = type, X = 370, Y = 10, Width = 185, Height = 64, Placement = DocumentTemplatePlacement.Flow },
			DocumentTemplateElementType.PageNumber => new() { Id = id, Type = type, X = 455, Y = 812, Width = 100, Height = 14, FontSize = 8, Alignment = DocumentTemplateAlignment.Right, RepeatOnEveryPage = true, Format = "Page {0} of {1}" },
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported designer element type.")
		};
	}

	private void RaiseInspectorProperties()
	{
		OnPropertyChanged(nameof(HasSelectedElement));
		OnPropertyChanged(nameof(ShowBindingProperty));
		OnPropertyChanged(nameof(ShowTextProperty));
		OnPropertyChanged(nameof(ShowTypographyProperties));
		OnPropertyChanged(nameof(ShowFormatProperty));
		OnPropertyChanged(nameof(ShowVisibilityProperty));
		OnPropertyChanged(nameof(ShowBorderProperty));
		OnPropertyChanged(nameof(ShowPlacementProperty));
		OnPropertyChanged(nameof(ShowRepeatProperty));
		OnPropertyChanged(nameof(ShowRowHeightProperty));
		OnPropertyChanged(nameof(ShowLineTableHint));
	}

	private void RaiseCommandStates()
	{
		NewFromDefaultCommand.RaiseCanExecuteChanged();
		DuplicateCommand.RaiseCanExecuteChanged();
		CopySelectedVersionAsDraftCommand.RaiseCanExecuteChanged();
		DiscardChangesCommand.RaiseCanExecuteChanged();
		SaveDraftCommand.RaiseCanExecuteChanged();
		ActivateCommand.RaiseCanExecuteChanged();
		ResetDefaultCommand.RaiseCanExecuteChanged();
		PreviewCommand.RaiseCanExecuteChanged();
		DuplicateElementsCommand.RaiseCanExecuteChanged();
		DeleteElementsCommand.RaiseCanExecuteChanged();
		UndoCommand.RaiseCanExecuteChanged();
		RedoCommand.RaiseCanExecuteChanged();
		AlignLeftCommand.RaiseCanExecuteChanged();
		AlignCenterCommand.RaiseCanExecuteChanged();
		AlignRightCommand.RaiseCanExecuteChanged();
		AlignTopCommand.RaiseCanExecuteChanged();
		AlignMiddleCommand.RaiseCanExecuteChanged();
		AlignBottomCommand.RaiseCanExecuteChanged();
		DistributeHorizontallyCommand.RaiseCanExecuteChanged();
		DistributeVerticallyCommand.RaiseCanExecuteChanged();
		SameWidthCommand.RaiseCanExecuteChanged();
		SameHeightCommand.RaiseCanExecuteChanged();
	}

	private sealed record DesignerSnapshot(string TemplateName, IReadOnlyList<DocumentTemplateElement> Elements);

	private enum AlignmentOperation
	{
		Left,
		Center,
		Right,
		Top,
		Middle,
		Bottom
	}
}
