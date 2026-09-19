// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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

	public void Move(double deltaX, double deltaY, bool snap, double gridSize)
	{
		X = snap ? Snap(X + deltaX, gridSize) : X + deltaX;
		Y = snap ? Snap(Y + deltaY, gridSize) : Y + deltaY;
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

	private static double Snap(double value, double gridSize) => gridSize <= 0 ? value : Math.Round(value / gridSize) * gridSize;
	private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DocumentTemplateDesignerViewModel : BaseViewModel
{
	private readonly DocumentTemplateDesignerService _service;
	private readonly HashSet<DocumentDesignerElementViewModel> _selection = [];
	private DocumentTemplateType _selectedType;
	private DocumentTemplateVersionItem? _selectedVersion;
	private DocumentDesignerElementViewModel? _selectedElement;
	private string _templateName = string.Empty;
	private bool _isEditing;
	private bool _showGrid = true;
	private bool _snapToGrid = true;
	private double _gridSize = 10;
	private double _zoomPercent = 90;
	private string _previewStatus = string.Empty;

	public DocumentTemplateDesignerViewModel(DocumentTemplateDesignerService service)
	{
		_service = service ?? throw new ArgumentNullException(nameof(service));
		DocumentTypes = Enum.GetValues<DocumentTemplateType>();
		Bindings = ["", .. service.Bindings];
		FontWeights = Enum.GetValues<DocumentTemplateFontWeight>();
		Alignments = Enum.GetValues<DocumentTemplateAlignment>();
		Placements = Enum.GetValues<DocumentTemplatePlacement>();

		NewFromDefaultCommand = new RelayCommand(NewFromDefault, () => CanManage);
		DuplicateCommand = new RelayCommand(Duplicate, () => CanManage && (SelectedVersion is not null || Elements.Count > 0));
		SaveDraftCommand = new RelayCommand(SaveDraft, () => CanManage && IsEditing && ValidationErrors.Count == 0 && Elements.Count > 0);
		ActivateCommand = new RelayCommand(Activate, () => CanManage && SelectedVersion is not null && !SelectedVersion.IsActive);
		ResetDefaultCommand = new RelayCommand(ResetDefault, () => CanManage);
		PreviewCommand = new RelayCommand(Preview, () => Elements.Count > 0 && ValidationErrors.Count == 0);
		DuplicateElementsCommand = new RelayCommand(DuplicateSelectedElements, () => CanManage && IsEditing && _selection.Count > 0);
		DeleteElementsCommand = new RelayCommand(DeleteSelectedElements, () => CanManage && IsEditing && _selection.Count > 0);

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
	public RelayCommand SaveDraftCommand { get; }
	public RelayCommand ActivateCommand { get; }
	public RelayCommand ResetDefaultCommand { get; }
	public RelayCommand PreviewCommand { get; }
	public RelayCommand DuplicateElementsCommand { get; }
	public RelayCommand DeleteElementsCommand { get; }

	public bool CanManage => _service.CanManage;
	public string Breadcrumb => "Administration / Documents / Document Designer";
	public string ModeText => IsEditing ? "Editing unsaved draft" : SelectedVersion is null ? "No template selected" : $"Read-only version {SelectedVersion.Version}";
	public double PageWidth => 595;
	public double PageHeight => 842;

	public DocumentTemplateType SelectedType
	{
		get => _selectedType;
		set
		{
			if (_selectedType == value) return;
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
			if (ReferenceEquals(_selectedVersion, value)) return;
			_selectedVersion = value;
			OnPropertyChanged();
			if (value is not null) LoadTemplate(value.Template, false);
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
		}
	}

	public string TemplateName
	{
		get => _templateName;
		set
		{
			if (_templateName == value) return;
			_templateName = value;
			OnPropertyChanged();
			UpdateValidation();
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

	public bool IsReadOnly => !IsEditing;
	public bool ShowGrid { get => _showGrid; set { if (_showGrid == value) return; _showGrid = value; OnPropertyChanged(); } }
	public bool SnapToGrid { get => _snapToGrid; set { if (_snapToGrid == value) return; _snapToGrid = value; OnPropertyChanged(); } }
	public double GridSize { get => _gridSize; set { var normalized = Math.Clamp(value, 1, 100); if (Math.Abs(_gridSize - normalized) < 0.001) return; _gridSize = normalized; OnPropertyChanged(); } }
	public double ZoomPercent { get => _zoomPercent; set { var normalized = Math.Clamp(value, 25, 200); if (Math.Abs(_zoomPercent - normalized) < 0.001) return; _zoomPercent = normalized; OnPropertyChanged(); OnPropertyChanged(nameof(ZoomScale)); } }
	public double ZoomScale => ZoomPercent / 100d;
	public string PreviewStatus { get => _previewStatus; private set { if (_previewStatus == value) return; _previewStatus = value; OnPropertyChanged(); } }
	public string ValidationSummary => ValidationErrors.Count == 0 ? "Template is valid." : $"{ValidationErrors.Count} validation issue(s)";

	public Task LoadAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ReloadVersions(selectActive: SelectedVersion is null);
		return Task.CompletedTask;
	}

	public void AddElement(DocumentTemplateElementType type)
	{
		if (!CanManage || !IsEditing) return;
		var element = CreateElement(type, Elements.Count);
		AddEditor(new DocumentDesignerElementViewModel(element));
		SelectedElement = Elements[^1];
		SetSelectedElements([Elements[^1]]);
		UpdateValidation();
	}

	public void SetSelectedElements(IEnumerable<DocumentDesignerElementViewModel> elements)
	{
		_selection.Clear();
		foreach (var element in elements.Where(Elements.Contains)) _selection.Add(element);
		SelectedElement = _selection.LastOrDefault();
		RaiseCommandStates();
	}

	public void MoveSelection(double deltaX, double deltaY)
	{
		if (!IsEditing) return;
		foreach (var element in _selection)
			element.Move(deltaX, deltaY, SnapToGrid, GridSize);
		UpdateValidation();
	}

	private void NewFromDefault()
	{
		var source = _service.GetDefault(SelectedType);
		LoadTemplate(source with { Id = $"{SelectedType.ToString().ToLowerInvariant()}-custom", Version = 0, IsActive = false }, true);
	}

	private void Duplicate()
	{
		var source = SelectedVersion?.Template ?? BuildTemplate(1, false);
		LoadTemplate(source with { Id = $"{source.Id}-copy", Version = 0, IsActive = false }, true);
	}

	private void SaveDraft()
	{
		try
		{
			var saved = _service.SaveDraft(BuildTemplate(1, false));
			ReloadVersions(selectActive: false);
			SelectedVersion = Versions.Single(item => item.Version == saved.Version);
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
			ReloadVersions(selectActive: false);
			SelectedVersion = Versions.Single(item => item.Version == active.Version);
			CompleteOperation(statusText: $"Version {active.Version} activated.");
		}
		catch (Exception exception) { FailOperation(exception, "Template version could not be activated"); }
	}

	private void ResetDefault()
	{
		try
		{
			var active = _service.ResetToDefault(SelectedType);
			ReloadVersions(selectActive: false);
			SelectedVersion = Versions.Single(item => item.Version == active.Version);
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
		UpdateValidation();
	}

	private void DeleteSelectedElements()
	{
		if (!IsEditing) return;
		foreach (var element in _selection.ToArray())
		{
			element.ModelChanged -= OnElementChanged;
			Elements.Remove(element);
		}
		SetSelectedElements([]);
		UpdateValidation();
	}

	private void ReloadVersions(bool selectActive)
	{
		var currentVersion = selectActive ? (int?)null : SelectedVersion?.Version;
		Versions.Clear();
		foreach (var template in _service.ListVersions(SelectedType)) Versions.Add(new DocumentTemplateVersionItem(template));
		var target = currentVersion is int version ? Versions.FirstOrDefault(item => item.Version == version) : Versions.FirstOrDefault(item => item.IsActive);
		_selectedVersion = null;
		SelectedVersion = target ?? Versions.LastOrDefault();
	}

	private void LoadTemplate(DocumentTemplate template, bool editing)
	{
		foreach (var existing in Elements) existing.ModelChanged -= OnElementChanged;
		Elements.Clear();
		foreach (var element in template.Elements) AddEditor(new DocumentDesignerElementViewModel(element));
		TemplateName = template.Id;
		IsEditing = editing;
		SetSelectedElements([]);
		UpdateValidation();
	}

	private void AddEditor(DocumentDesignerElementViewModel editor)
	{
		editor.ModelChanged += OnElementChanged;
		Elements.Add(editor);
	}

	private void OnElementChanged(object? sender, EventArgs e) => UpdateValidation();

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

	private void RaiseCommandStates()
	{
		NewFromDefaultCommand.RaiseCanExecuteChanged();
		DuplicateCommand.RaiseCanExecuteChanged();
		SaveDraftCommand.RaiseCanExecuteChanged();
		ActivateCommand.RaiseCanExecuteChanged();
		ResetDefaultCommand.RaiseCanExecuteChanged();
		PreviewCommand.RaiseCanExecuteChanged();
		DuplicateElementsCommand.RaiseCanExecuteChanged();
		DeleteElementsCommand.RaiseCanExecuteChanged();
	}
}
