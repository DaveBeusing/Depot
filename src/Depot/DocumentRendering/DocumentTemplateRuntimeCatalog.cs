// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.DocumentRendering;

public interface IDocumentTemplateStore
{
	void EnsureDefaults(IReadOnlyCollection<DocumentTemplate> defaults);
	IReadOnlyList<DocumentTemplate> LoadAll();
	void Insert(DocumentTemplate template);
	void Activate(DocumentTemplateType type, int version);
}

public sealed class DocumentTemplateRuntimeCatalog
{
	private readonly object _sync = new();
	private readonly List<DocumentTemplate> _templates;
	private readonly IReadOnlyDictionary<DocumentTemplateType, DocumentTemplate> _defaults;
	private IDocumentTemplateStore? _store;

	public DocumentTemplateRuntimeCatalog(IEnumerable<DocumentTemplate> defaults)
	{
		ArgumentNullException.ThrowIfNull(defaults);
		var materialized = defaults.Select(Clone).ToArray();
		_ = new DocumentTemplateCatalog(materialized);
		_templates = materialized.ToList();
		_defaults = materialized.ToDictionary(template => template.Type, Clone);
	}

	public DocumentTemplateCatalog Snapshot
	{
		get
		{
			lock (_sync)
			{
				ReloadFromStore();
				return new DocumentTemplateCatalog(_templates.Select(Clone).ToArray());
			}
		}
	}

	public void AttachStore(IDocumentTemplateStore store)
	{
		ArgumentNullException.ThrowIfNull(store);
		lock (_sync)
		{
			store.EnsureDefaults(_defaults.Values.Select(Clone).ToArray());
			_store = store;
			ReloadFromStore();
		}
	}

	public void DetachStore()
	{
		lock (_sync)
		{
			_store = null;
			_templates.Clear();
			_templates.AddRange(_defaults.Values.OrderBy(value => value.Type).Select(Clone));
		}
	}

	public DocumentTemplate GetDefault(DocumentTemplateType type)
	{
		lock (_sync)
		{
			if (!_defaults.TryGetValue(type, out var template))
				throw new KeyNotFoundException($"No default document template is registered for {type}.");
			return Clone(template);
		}
	}

	public IReadOnlyList<DocumentTemplate> ListVersions(DocumentTemplateType type)
	{
		lock (_sync)
		{
			ReloadFromStore();
			return _templates.Where(template => template.Type == type).OrderBy(template => template.Version).Select(Clone).ToArray();
		}
	}

	public DocumentTemplate SaveDraft(DocumentTemplate template)
	{
		ArgumentNullException.ThrowIfNull(template);
		lock (_sync)
		{
			ReloadFromStore();
			var nextVersion = _templates.Where(value => value.Type == template.Type).Select(value => value.Version).DefaultIfEmpty(0).Max() + 1;
			var draft = Clone(template) with { Version = nextVersion, IsActive = false };
			DocumentTemplateValidator.ValidateAndThrow(draft);

			if (_store is null)
			{
				_templates.Add(draft);
				return Clone(draft);
			}

			_store.Insert(draft);
			ReloadFromStore();
			return Clone(_templates.Single(value => value.Type == draft.Type && value.Version == draft.Version));
		}
	}

	public DocumentTemplate Activate(DocumentTemplateType type, int version)
	{
		lock (_sync)
		{
			ReloadFromStore();
			var selected = _templates.SingleOrDefault(template => template.Type == type && template.Version == version)
				?? throw new KeyNotFoundException($"Document template {type} v{version} is not registered.");
			DocumentTemplateValidator.ValidateAndThrow(selected);

			if (_store is not null)
			{
				_store.Activate(type, version);
				ReloadFromStore();
				return Clone(_templates.Single(template => template.Type == type && template.Version == version));
			}

			for (var index = 0; index < _templates.Count; index++)
			{
				if (_templates[index].Type == type)
					_templates[index] = _templates[index] with { IsActive = _templates[index].Version == version };
			}
			return Clone(_templates.Single(template => template.Type == type && template.Version == version));
		}
	}

	public DocumentTemplate ResetToDefault(DocumentTemplateType type)
	{
		lock (_sync)
		{
			if (!_defaults.TryGetValue(type, out var defaultTemplate))
				throw new KeyNotFoundException($"No default document template is registered for {type}.");

			ReloadFromStore();
			if (_store is not null)
			{
				_store.Activate(type, defaultTemplate.Version);
				ReloadFromStore();
				return Clone(_templates.Single(template => template.Type == type && template.Version == defaultTemplate.Version));
			}

			for (var index = 0; index < _templates.Count; index++)
			{
				if (_templates[index].Type == type)
					_templates[index] = _templates[index] with { IsActive = _templates[index].Version == defaultTemplate.Version };
			}
			return Clone(defaultTemplate with { IsActive = true });
		}
	}

	private void ReloadFromStore()
	{
		if (_store is null) return;
		var loaded = _store.LoadAll().Select(Clone).ToArray();
		foreach (var type in _defaults.Keys)
		{
			if (!loaded.Any(template => template.Type == type))
				throw new InvalidOperationException($"Persisted document templates are missing the required {type} template family.");
		}
		_ = new DocumentTemplateCatalog(loaded);
		_templates.Clear();
		_templates.AddRange(loaded);
	}

	private static DocumentTemplate Clone(DocumentTemplate template) =>
		template with
		{
			Elements = template.Elements.Select(element => element with
			{
				Columns = element.Columns.Select(column => column with { }).ToArray()
			}).ToArray()
		};
}
