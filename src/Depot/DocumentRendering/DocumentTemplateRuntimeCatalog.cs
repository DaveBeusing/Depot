// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

namespace Depot.DocumentRendering;

public sealed class DocumentTemplateRuntimeCatalog
{
	private readonly object _sync = new();
	private readonly List<DocumentTemplate> _templates;
	private readonly IReadOnlyDictionary<DocumentTemplateType, DocumentTemplate> _defaults;

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
			lock (_sync) return new DocumentTemplateCatalog(_templates.Select(Clone).ToArray());
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
			return _templates.Where(template => template.Type == type).OrderBy(template => template.Version).Select(Clone).ToArray();
	}

	public DocumentTemplate SaveDraft(DocumentTemplate template)
	{
		ArgumentNullException.ThrowIfNull(template);
		lock (_sync)
		{
			var nextVersion = _templates.Where(value => value.Type == template.Type).Select(value => value.Version).DefaultIfEmpty(0).Max() + 1;
			var draft = Clone(template) with { Version = nextVersion, IsActive = false };
			DocumentTemplateValidator.ValidateAndThrow(draft);
			_templates.Add(draft);
			return Clone(draft);
		}
	}

	public DocumentTemplate Activate(DocumentTemplateType type, int version)
	{
		lock (_sync)
		{
			var selected = _templates.SingleOrDefault(template => template.Type == type && template.Version == version)
				?? throw new KeyNotFoundException($"Document template {type} v{version} is not registered.");
			DocumentTemplateValidator.ValidateAndThrow(selected);

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
			var defaultTemplate = _defaults[type];
			for (var index = 0; index < _templates.Count; index++)
			{
				if (_templates[index].Type == type)
					_templates[index] = _templates[index] with { IsActive = _templates[index].Version == defaultTemplate.Version };
			}
			return Clone(defaultTemplate with { IsActive = true });
		}
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
