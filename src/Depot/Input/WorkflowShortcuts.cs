// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

using Depot.Commands;
using Depot.ViewModels;
using Depot.ViewModels.Administration;
using Depot.ViewModels.MasterData;
using Depot.ViewModels.ReasonCodes;

namespace Depot.Input;

public enum WorkflowShortcutAction
{
	Save = 1,
	SaveAndNew = 2,
	Refresh = 3,
	Search = 4,
	Close = 5,
	AddLine = 6,
	DeleteDraftLine = 7,
	PreviousRecord = 8,
	NextRecord = 9,
	PrimaryAction = 10,
	ContextAction = 11
}

public sealed record WorkflowShortcutDefinition(
	WorkflowShortcutAction Action,
	Key Key,
	ModifierKeys Modifiers,
	string GestureText,
	string Description,
	bool IsDestructive = false,
	bool IsShellHandled = false);

public static class WorkflowShortcutCatalog
{
	private static readonly WorkflowShortcutDefinition[] Entries =
	[
		new(WorkflowShortcutAction.Save, Key.S, ModifierKeys.Control, "Ctrl+S", "Save the current draft or editor"),
		new(WorkflowShortcutAction.SaveAndNew, Key.S, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+S", "Save and start a new record"),
		new(WorkflowShortcutAction.Refresh, Key.F5, ModifierKeys.None, "F5", "Refresh the current workspace"),
		new(WorkflowShortcutAction.Search, Key.F, ModifierKeys.Control, "Ctrl+F", "Open search", IsShellHandled: true),
		new(WorkflowShortcutAction.Close, Key.W, ModifierKeys.Control, "Ctrl+W", "Close the active workspace tab", IsShellHandled: true),
		new(WorkflowShortcutAction.AddLine, Key.Insert, ModifierKeys.Alt, "Alt+Insert", "Add or update a draft line"),
		new(WorkflowShortcutAction.DeleteDraftLine, Key.Delete, ModifierKeys.Alt, "Alt+Delete", "Delete the selected draft line", IsDestructive: true),
		new(WorkflowShortcutAction.PreviousRecord, Key.PageUp, ModifierKeys.Control, "Ctrl+PgUp", "Select the previous record"),
		new(WorkflowShortcutAction.NextRecord, Key.PageDown, ModifierKeys.Control, "Ctrl+PgDn", "Select the next record"),
		new(WorkflowShortcutAction.PrimaryAction, Key.Enter, ModifierKeys.Control, "Ctrl+Enter", "Run the primary workflow action"),
		new(WorkflowShortcutAction.ContextAction, Key.Enter, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+Enter", "Run the current context action")
	];

	public static IReadOnlyList<WorkflowShortcutDefinition> Definitions => Entries;

	public static bool TryMatch(Key key, ModifierKeys modifiers, out WorkflowShortcutDefinition definition)
	{
		var normalized = modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt | ModifierKeys.Windows);
		definition = Entries.FirstOrDefault(entry => entry.Key == key && entry.Modifiers == normalized)!;
		return definition is not null;
	}

	public static WorkflowShortcutDefinition Get(WorkflowShortcutAction action) =>
		Entries.First(entry => entry.Action == action);
}

internal enum WorkflowShortcutRegistrationKind
{
	Direct = 1,
	Sequential = 2,
	RelativeSelection = 3
}

internal sealed record WorkflowShortcutRegistration(
	Type ContextType,
	WorkflowShortcutAction Action,
	WorkflowShortcutRegistrationKind Kind,
	PropertyInfo PrimaryProperty,
	PropertyInfo? SecondaryProperty = null,
	PropertyInfo? CollectionProperty = null,
	int RelativeOffset = 0);

internal static class WorkflowShortcutCommandRegistry
{
	private static readonly WorkflowShortcutRegistration[] Entries =
	[
		Direct<ReasonCodeViewModel>(WorkflowShortcutAction.Save, nameof(ReasonCodeViewModel.SaveCommand)),
		Sequential<ReasonCodeViewModel>(WorkflowShortcutAction.SaveAndNew, nameof(ReasonCodeViewModel.SaveCommand), nameof(ReasonCodeViewModel.NewCommand)),
		RelativeSelection<ReasonCodeViewModel>(WorkflowShortcutAction.PreviousRecord, nameof(ReasonCodeViewModel.SelectedReasonCode), nameof(ReasonCodeViewModel.ReasonCodes), -1),
		RelativeSelection<ReasonCodeViewModel>(WorkflowShortcutAction.NextRecord, nameof(ReasonCodeViewModel.SelectedReasonCode), nameof(ReasonCodeViewModel.ReasonCodes), 1),
		Direct<ReasonCodeViewModel>(WorkflowShortcutAction.ContextAction, nameof(ReasonCodeViewModel.ToggleActiveCommand)),

		Direct<ItemReferenceDataViewModel>(WorkflowShortcutAction.Save, nameof(ItemReferenceDataViewModel.SaveCommand)),
		Sequential<ItemReferenceDataViewModel>(WorkflowShortcutAction.SaveAndNew, nameof(ItemReferenceDataViewModel.SaveCommand), nameof(ItemReferenceDataViewModel.NewCommand)),
		RelativeSelection<ItemReferenceDataViewModel>(WorkflowShortcutAction.PreviousRecord, nameof(ItemReferenceDataViewModel.SelectedItem), nameof(ItemReferenceDataViewModel.Items), -1),
		RelativeSelection<ItemReferenceDataViewModel>(WorkflowShortcutAction.NextRecord, nameof(ItemReferenceDataViewModel.SelectedItem), nameof(ItemReferenceDataViewModel.Items), 1),
		Direct<ItemReferenceDataViewModel>(WorkflowShortcutAction.ContextAction, nameof(ItemReferenceDataViewModel.ToggleActiveCommand)),

		Direct<MaterialIssuesViewModel>(WorkflowShortcutAction.Save, nameof(MaterialIssuesViewModel.SaveCommand)),
		Direct<MaterialIssuesViewModel>(WorkflowShortcutAction.AddLine, nameof(MaterialIssuesViewModel.AddLineCommand)),
		Direct<MaterialIssuesViewModel>(WorkflowShortcutAction.DeleteDraftLine, nameof(MaterialIssuesViewModel.RemoveLineCommand)),
		Direct<MaterialIssuesViewModel>(WorkflowShortcutAction.PrimaryAction, nameof(MaterialIssuesViewModel.PostCommand)),
		Direct<MaterialIssuesViewModel>(WorkflowShortcutAction.ContextAction, nameof(MaterialIssuesViewModel.ReverseCommand)),

		Direct<MaterialReturnsViewModel>(WorkflowShortcutAction.Save, nameof(MaterialReturnsViewModel.SaveCommand)),
		Direct<MaterialReturnsViewModel>(WorkflowShortcutAction.AddLine, nameof(MaterialReturnsViewModel.AddLineCommand)),
		Direct<MaterialReturnsViewModel>(WorkflowShortcutAction.DeleteDraftLine, nameof(MaterialReturnsViewModel.RemoveLineCommand)),
		Direct<MaterialReturnsViewModel>(WorkflowShortcutAction.PrimaryAction, nameof(MaterialReturnsViewModel.PostCommand)),
		Direct<MaterialReturnsViewModel>(WorkflowShortcutAction.ContextAction, nameof(MaterialReturnsViewModel.CorrectCommand)),

		Direct<SupplierReturnsViewModel>(WorkflowShortcutAction.Save, nameof(SupplierReturnsViewModel.SaveCommand)),
		Direct<SupplierReturnsViewModel>(WorkflowShortcutAction.AddLine, nameof(SupplierReturnsViewModel.AddLineCommand)),
		Direct<SupplierReturnsViewModel>(WorkflowShortcutAction.DeleteDraftLine, nameof(SupplierReturnsViewModel.RemoveLineCommand)),
		Direct<SupplierReturnsViewModel>(WorkflowShortcutAction.PrimaryAction, nameof(SupplierReturnsViewModel.PostCommand)),
		Direct<SupplierReturnsViewModel>(WorkflowShortcutAction.ContextAction, nameof(SupplierReturnsViewModel.ReverseCommand)),

		Direct<StockTransfersViewModel>(WorkflowShortcutAction.Save, nameof(StockTransfersViewModel.SaveTransferCommand)),
		Direct<StockTransfersViewModel>(WorkflowShortcutAction.AddLine, nameof(StockTransfersViewModel.AddLineCommand)),
		Direct<StockTransfersViewModel>(WorkflowShortcutAction.DeleteDraftLine, nameof(StockTransfersViewModel.RemoveLineCommand)),
		Direct<StockTransfersViewModel>(WorkflowShortcutAction.PrimaryAction, nameof(StockTransfersViewModel.PostTransferCommand)),
		Direct<StockTransfersViewModel>(WorkflowShortcutAction.ContextAction, nameof(StockTransfersViewModel.ReverseTransferCommand)),

		Direct<ProcurementViewModel>(WorkflowShortcutAction.Save, nameof(ProcurementViewModel.SaveOrderCommand)),
		Direct<ProcurementViewModel>(WorkflowShortcutAction.AddLine, nameof(ProcurementViewModel.AddLineCommand)),
		Direct<ProcurementViewModel>(WorkflowShortcutAction.DeleteDraftLine, nameof(ProcurementViewModel.RemoveLineCommand)),
		Direct<ProcurementViewModel>(WorkflowShortcutAction.PrimaryAction, nameof(ProcurementViewModel.SubmitForApprovalCommand)),

		Direct<FinancePayablesViewModel>(WorkflowShortcutAction.Save, nameof(FinancePayablesViewModel.SaveDraftCommand)),
		Direct<FinancePayablesViewModel>(WorkflowShortcutAction.Refresh, nameof(FinancePayablesViewModel.RefreshCommand)),
		Direct<FinancePayablesViewModel>(WorkflowShortcutAction.AddLine, nameof(FinancePayablesViewModel.AddLineCommand)),
		Direct<FinancePayablesViewModel>(WorkflowShortcutAction.DeleteDraftLine, nameof(FinancePayablesViewModel.RemoveLineCommand)),
		Direct<FinancePayablesViewModel>(WorkflowShortcutAction.PrimaryAction, nameof(FinancePayablesViewModel.SubmitCommand)),

		Direct<FinanceBankingViewModel>(WorkflowShortcutAction.Refresh, nameof(FinanceBankingViewModel.RefreshCommand)),
		Direct<FinanceFinancialReportingViewModel>(WorkflowShortcutAction.Refresh, nameof(FinanceFinancialReportingViewModel.RefreshCommand)),
		Direct<FinanceLocalizationViewModel>(WorkflowShortcutAction.Refresh, nameof(FinanceLocalizationViewModel.RefreshCommand)),
		Direct<FinanceReceivablesViewModel>(WorkflowShortcutAction.Refresh, nameof(FinanceReceivablesViewModel.RefreshCommand)),
		Direct<DatabaseSettingsViewModel>(WorkflowShortcutAction.Save, nameof(DatabaseSettingsViewModel.SaveCommand)),
		Direct<DatabaseSettingsViewModel>(WorkflowShortcutAction.Refresh, nameof(DatabaseSettingsViewModel.RefreshCommand)),
		Direct<CompanyProfileViewModel>(WorkflowShortcutAction.Save, nameof(CompanyProfileViewModel.SaveCommand)),
		Direct<SecurityCenterViewModel>(WorkflowShortcutAction.Refresh, nameof(SecurityCenterViewModel.RefreshCommand)),
		Direct<NotificationCenterViewModel>(WorkflowShortcutAction.Refresh, nameof(NotificationCenterViewModel.RefreshCommand))
	];

	internal static IReadOnlyList<WorkflowShortcutRegistration> Registrations => Entries;

	public static bool HasBindings(object? context) =>
		context is not null && Entries.Any(entry => entry.ContextType.IsInstanceOfType(context));

	public static ICommand? Resolve(object? context, WorkflowShortcutAction action)
	{
		if (context is null) return null;
		var entry = Entries.FirstOrDefault(candidate => candidate.Action == action && candidate.ContextType.IsInstanceOfType(context));
		if (entry is null) return null;
		return entry.Kind switch
		{
			WorkflowShortcutRegistrationKind.Direct => entry.PrimaryProperty.GetValue(context) as ICommand,
			WorkflowShortcutRegistrationKind.Sequential => CreateSequential(context, entry),
			WorkflowShortcutRegistrationKind.RelativeSelection => CreateRelativeSelection(context, entry),
			_ => null
		};
	}

	public static WorkflowShortcutAction? FindDirectAction(object? context, ICommand? command)
	{
		if (context is null || command is null) return null;
		foreach (var entry in Entries)
		{
			if (entry.Kind != WorkflowShortcutRegistrationKind.Direct || !entry.ContextType.IsInstanceOfType(context)) continue;
			if (ReferenceEquals(entry.PrimaryProperty.GetValue(context), command)) return entry.Action;
		}
		return null;
	}

	private static ICommand? CreateSequential(object context, WorkflowShortcutRegistration entry)
	{
		if (entry.PrimaryProperty.GetValue(context) is not ICommand first || entry.SecondaryProperty?.GetValue(context) is not ICommand second) return null;
		return new SequentialWorkflowCommand(context as BaseViewModel, first, second);
	}

	private static ICommand? CreateRelativeSelection(object context, WorkflowShortcutRegistration entry)
	{
		if (entry.CollectionProperty?.GetValue(context) is not IList collection) return null;
		return new RelativeSelectionWorkflowCommand(context, entry.PrimaryProperty, collection, entry.RelativeOffset);
	}

	private static WorkflowShortcutRegistration Direct<T>(WorkflowShortcutAction action, string commandProperty) =>
		new(typeof(T), action, WorkflowShortcutRegistrationKind.Direct, RequireProperty<T>(commandProperty, typeof(ICommand)));

	private static WorkflowShortcutRegistration Sequential<T>(WorkflowShortcutAction action, string firstCommandProperty, string secondCommandProperty) =>
		new(
			typeof(T),
			action,
			WorkflowShortcutRegistrationKind.Sequential,
			RequireProperty<T>(firstCommandProperty, typeof(ICommand)),
			RequireProperty<T>(secondCommandProperty, typeof(ICommand)));

	private static WorkflowShortcutRegistration RelativeSelection<T>(WorkflowShortcutAction action, string selectedProperty, string collectionProperty, int offset) =>
		new(
			typeof(T),
			action,
			WorkflowShortcutRegistrationKind.RelativeSelection,
			RequireWritableProperty<T>(selectedProperty),
			CollectionProperty: RequireProperty<T>(collectionProperty, typeof(IEnumerable)),
			RelativeOffset: offset);

	private static PropertyInfo RequireProperty<T>(string name, Type requiredType)
	{
		var property = typeof(T).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
			?? throw new InvalidOperationException($"Workflow shortcut property '{typeof(T).Name}.{name}' was not found.");
		if (!requiredType.IsAssignableFrom(property.PropertyType))
			throw new InvalidOperationException($"Workflow shortcut property '{typeof(T).Name}.{name}' must implement {requiredType.Name}.");
		return property;
	}

	private static PropertyInfo RequireWritableProperty<T>(string name)
	{
		var property = typeof(T).GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
			?? throw new InvalidOperationException($"Workflow shortcut property '{typeof(T).Name}.{name}' was not found.");
		if (!property.CanWrite) throw new InvalidOperationException($"Workflow shortcut property '{typeof(T).Name}.{name}' must be writable.");
		return property;
	}
}

internal sealed class SequentialWorkflowCommand : ICommand
{
	private readonly BaseViewModel? _viewModel;
	private readonly ICommand _first;
	private readonly ICommand _second;

	public SequentialWorkflowCommand(BaseViewModel? viewModel, ICommand first, ICommand second)
	{
		_viewModel = viewModel;
		_first = first;
		_second = second;
	}

	public bool CanExecute(object? parameter) => _first.CanExecute(null) && _second.CanExecute(null);

	public async void Execute(object? parameter)
	{
		if (!CanExecute(null)) return;
		if (_first is AsyncRelayCommand asyncCommand) await asyncCommand.ExecuteAsync();
		else _first.Execute(null);
		if (_viewModel?.HasOperationError == true || !_second.CanExecute(null)) return;
		_second.Execute(null);
	}

	public event EventHandler? CanExecuteChanged { add { } remove { } }
}

internal sealed class RelativeSelectionWorkflowCommand : ICommand
{
	private readonly object _context;
	private readonly PropertyInfo _selectedProperty;
	private readonly IList _collection;
	private readonly int _offset;

	public RelativeSelectionWorkflowCommand(object context, PropertyInfo selectedProperty, IList collection, int offset)
	{
		_context = context;
		_selectedProperty = selectedProperty;
		_collection = collection;
		_offset = offset;
	}

	public bool CanExecute(object? parameter)
	{
		if (_collection.Count == 0) return false;
		var current = _selectedProperty.GetValue(_context);
		if (current is null) return true;
		var index = _collection.IndexOf(current);
		return index >= 0 && index + _offset >= 0 && index + _offset < _collection.Count;
	}

	public void Execute(object? parameter)
	{
		if (!CanExecute(null)) return;
		var current = _selectedProperty.GetValue(_context);
		var index = current is null ? (_offset > 0 ? -1 : _collection.Count) : _collection.IndexOf(current);
		var target = index + _offset;
		if (target < 0 || target >= _collection.Count) return;
		_selectedProperty.SetValue(_context, _collection[target]);
	}

	public event EventHandler? CanExecuteChanged { add { } remove { } }
}

public static class WorkflowShortcutRouter
{
	public static object? FindContext(DependencyObject? focusedElement, object? fallbackContext)
	{
		for (var current = focusedElement; current is not null; current = ParentOf(current))
		{
			if (current is FrameworkElement element && WorkflowShortcutCommandRegistry.HasBindings(element.DataContext)) return element.DataContext;
		}
		return WorkflowShortcutCommandRegistry.HasBindings(fallbackContext) ? fallbackContext : null;
	}

	public static bool TryExecute(
		object? context,
		WorkflowShortcutDefinition definition,
		Func<WorkflowShortcutAction, ICommand?> shellCommandResolver,
		IInputElement? commandTarget)
	{
		var command = WorkflowShortcutCommandRegistry.Resolve(context, definition.Action) ?? shellCommandResolver(definition.Action);
		return TryExecute(command, commandTarget);
	}

	internal static bool TryExecute(ICommand? command, IInputElement? commandTarget)
	{
		if (command is null) return false;
		if (command is RoutedCommand routed)
		{
			if (commandTarget is null || !routed.CanExecute(null, commandTarget)) return false;
			routed.Execute(null, commandTarget);
			return true;
		}
		if (!command.CanExecute(null)) return false;
		command.Execute(null);
		return true;
	}

	private static DependencyObject? ParentOf(DependencyObject current)
	{
		if (current is Visual or System.Windows.Media.Media3D.Visual3D)
		{
			var visualParent = VisualTreeHelper.GetParent(current);
			if (visualParent is not null) return visualParent;
		}
		return LogicalTreeHelper.GetParent(current);
	}
}

public static class WorkflowShortcutHints
{
	private static bool _initialized;

	public static void Initialize()
	{
		if (_initialized) return;
		_initialized = true;
		EventManager.RegisterClassHandler(typeof(ButtonBase), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnButtonLoaded), true);
	}

	private static void OnButtonLoaded(object sender, RoutedEventArgs e)
	{
		if (sender is not ButtonBase { Command: { } command } button || button.ToolTip is not null) return;
		var action = WorkflowShortcutCommandRegistry.FindDirectAction(button.DataContext, command);
		if (action is null) return;
		var definition = WorkflowShortcutCatalog.Get(action.Value);
		button.ToolTip = $"Keyboard shortcut: {definition.GestureText}";
	}
}
