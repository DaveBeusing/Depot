// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Collections.ObjectModel;
using System.Windows.Documents;

using Depot.Commands;
using Depot.Models;
using Depot.Services;
using Depot.Services.Help;

namespace Depot.ViewModels.Help;

public sealed class HelpViewModel : BaseViewModel, IDisposable
{
	private readonly IHelpService _service;
	private readonly HelpMarkdownRenderer _renderer;
	private readonly AsyncDebouncer _searchDebouncer = new(TimeSpan.FromMilliseconds(250));
	private string _searchText = string.Empty;
	private string? _selectedCategory;
	private HelpTopicDefinition? _selectedTopic;
	private FlowDocument? _document;
	private string _articleTitle = "Help Center";

	public HelpViewModel(IHelpService service, HelpMarkdownRenderer renderer)
	{
		_service = service;
		_renderer = renderer;
		CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
	}

	public ObservableCollection<string> Categories { get; } = [];
	public ObservableCollection<HelpTopicDefinition> Topics { get; } = [];
	public ObservableCollection<HelpTopicDefinition> RelatedTopics { get; } = [];
	public RelayCommand CloseCommand { get; }
	public event EventHandler? CloseRequested;

	public string SearchText
	{
		get => _searchText;
		set
		{
			if (_searchText == value) return;
			_searchText = value;
			OnPropertyChanged();
			_ = _searchDebouncer.DebounceAsync(token => RefreshTopicsAsync(token));
		}
	}

	public string? SelectedCategory
	{
		get => _selectedCategory;
		set
		{
			if (_selectedCategory == value) return;
			_selectedCategory = value;
			OnPropertyChanged();
			_ = RefreshTopicsAsync(CancellationToken.None);
		}
	}

	public HelpTopicDefinition? SelectedTopic
	{
		get => _selectedTopic;
		set
		{
			if (_selectedTopic == value) return;
			_selectedTopic = value;
			OnPropertyChanged();
			_ = LoadTopicAsync(value?.Id, CancellationToken.None);
		}
	}

	public FlowDocument? Document { get => _document; private set { _document = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDocument)); OnPropertyChanged(nameof(HasNoDocument)); } }
	public string ArticleTitle { get => _articleTitle; private set { _articleTitle = value; OnPropertyChanged(); } }
	public bool HasDocument => Document is not null;
	public bool HasNoDocument => !HasDocument;
	public bool HasTopics => Topics.Count > 0;
	public bool HasNoTopics => !HasTopics;
	public bool HasRelatedTopics => RelatedTopics.Count > 0;

	public async Task OpenAsync(string? topicId = null, CancellationToken cancellationToken = default)
	{
		BeginOperation("Help content is loading");
		try
		{
			var catalog = await _service.GetCatalogAsync(cancellationToken);
			Categories.Clear();
			Categories.Add("All topics");
			foreach (var category in catalog.Categories) Categories.Add(category.Name);
			_selectedCategory = "All topics";
			OnPropertyChanged(nameof(SelectedCategory));
			await RefreshTopicsAsync(cancellationToken, false);
			await LoadTopicAsync(topicId ?? HelpService.FallbackTopicId, cancellationToken);
			CompleteOperation(Topics.Count == 0, Topics.Count == 0 ? "No help topics are available" : "Help content loaded");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			Document = null;
			FailOperation(exception, "Help content could not be loaded");
		}
	}

	public async Task NavigateToTopicAsync(string topicId, CancellationToken cancellationToken = default) =>
		await LoadTopicAsync(topicId, cancellationToken);

	private async Task RefreshTopicsAsync(CancellationToken cancellationToken, bool preserveSelection = true)
	{
		var selectedId = preserveSelection ? SelectedTopic?.Id : null;
		var category = SelectedCategory == "All topics" ? null : SelectedCategory;
		var results = await _service.SearchAsync(SearchText, category, cancellationToken);
		Topics.Clear();
		foreach (var topic in results) Topics.Add(topic.Definition);
		OnPropertyChanged(nameof(HasTopics));
		OnPropertyChanged(nameof(HasNoTopics));
		if (selectedId is not null) _selectedTopic = Topics.FirstOrDefault(topic => topic.Id == selectedId);
		OnPropertyChanged(nameof(SelectedTopic));
	}

	private async Task LoadTopicAsync(string? topicId, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(topicId)) return;
		var topic = await _service.GetTopicAsync(topicId, cancellationToken) ??
			await _service.GetTopicAsync(HelpService.FallbackTopicId, cancellationToken);
		if (topic is null)
		{
			Document = null;
			ArticleTitle = "Help topic unavailable";
			return;
		}
		_selectedTopic = Topics.FirstOrDefault(item => item.Id == topic.Definition.Id) ?? topic.Definition;
		OnPropertyChanged(nameof(SelectedTopic));
		ArticleTitle = topic.Definition.Title;
		Document = _renderer.Render(topic.Markdown);
		RelatedTopics.Clear();
		foreach (var related in await _service.GetRelatedTopicsAsync(topic.Definition.Id, cancellationToken)) RelatedTopics.Add(related);
		OnPropertyChanged(nameof(HasRelatedTopics));
	}

	public void Dispose() => _searchDebouncer.Dispose();
}
