// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Data.Common;
using System.Globalization;

using Depot.Data;
using Depot.DocumentRendering;

namespace Depot.Repositories;

public sealed class DocumentTemplateRepository : DatabaseRepository, IDocumentTemplateStore
{
	public DocumentTemplateRepository(DatabaseAccess database) : base(database)
	{
	}

	public void EnsureDefaults(IReadOnlyCollection<DocumentTemplate> defaults)
	{
		ArgumentNullException.ThrowIfNull(defaults);
		Database.ExecuteInWriteTransaction(session =>
		{
			foreach (var source in defaults.OrderBy(value => value.Type).ThenBy(value => value.Version))
			{
				DocumentTemplateValidator.ValidateAndThrow(source);
				var versionCount = Convert.ToInt32(session.ExecuteScalar(
					"SELECT COUNT(*) FROM DocumentTemplates WHERE DocumentType=$Type AND Version=$Version;",
					Parameter("$Type", (int)source.Type),
					Parameter("$Version", source.Version)) ?? 0, CultureInfo.InvariantCulture);
				if (versionCount != 0) continue;

				var typeCount = Convert.ToInt32(session.ExecuteScalar(
					"SELECT COUNT(*) FROM DocumentTemplates WHERE DocumentType=$Type;",
					Parameter("$Type", (int)source.Type)) ?? 0, CultureInfo.InvariantCulture);
				var stored = source with { IsActive = typeCount == 0 };
				session.Execute(
					"INSERT INTO DocumentTemplates (DocumentType,Version,IsActive,TemplateJson) VALUES ($Type,$Version,$Active,$Json);",
					Parameter("$Type", (int)stored.Type),
					Parameter("$Version", stored.Version),
					Parameter("$Active", stored.IsActive ? 1 : 0),
					Parameter("$Json", DocumentTemplateSerializer.Serialize(stored)));
			}
			return 0;
		});
	}

	public IReadOnlyList<DocumentTemplate> LoadAll() =>
		Database.Query(
			"SELECT DocumentType,Version,IsActive,TemplateJson FROM DocumentTemplates ORDER BY DocumentType,Version;",
			Read);

	public void Insert(DocumentTemplate template)
	{
		ArgumentNullException.ThrowIfNull(template);
		DocumentTemplateValidator.ValidateAndThrow(template);
		if (template.IsActive)
			throw new InvalidOperationException("New document-template versions must be saved as inactive drafts before activation.");

		var affected = Database.Execute(
			"INSERT INTO DocumentTemplates (DocumentType,Version,IsActive,TemplateJson) VALUES ($Type,$Version,0,$Json);",
			Parameter("$Type", (int)template.Type),
			Parameter("$Version", template.Version),
			Parameter("$Json", DocumentTemplateSerializer.Serialize(template)));
		if (affected != 1)
			throw new InvalidOperationException("The document-template draft was not persisted.");
	}

	public void Activate(DocumentTemplateType type, int version)
	{
		Database.ExecuteInWriteTransaction(session =>
		{
			var exists = Convert.ToInt32(session.ExecuteScalar(
				"SELECT COUNT(*) FROM DocumentTemplates WHERE DocumentType=$Type AND Version=$Version;",
				Parameter("$Type", (int)type),
				Parameter("$Version", version)) ?? 0, CultureInfo.InvariantCulture);
			if (exists != 1)
				throw new KeyNotFoundException($"Document template {type} v{version} is not registered.");

			session.Execute("UPDATE DocumentTemplates SET IsActive=0 WHERE DocumentType=$Type;", Parameter("$Type", (int)type));
			var affected = session.Execute(
				"UPDATE DocumentTemplates SET IsActive=1 WHERE DocumentType=$Type AND Version=$Version;",
				Parameter("$Type", (int)type),
				Parameter("$Version", version));
			if (affected != 1)
				throw new InvalidOperationException($"Document template {type} v{version} could not be activated.");
			return 0;
		});
	}

	private static DocumentTemplate Read(DbDataReader reader)
	{
		var rawType = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
		if (!Enum.IsDefined(typeof(DocumentTemplateType), rawType))
			throw new InvalidOperationException($"Persisted document-template type '{rawType}' is not supported.");
		var type = (DocumentTemplateType)rawType;
		var version = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
		var active = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture) != 0;
		var template = DocumentTemplateSerializer.Deserialize(reader.GetString(3));
		if (template.Type != type || template.Version != version)
			throw new InvalidOperationException($"Persisted document-template key {type} v{version} does not match its serialized payload.");
		var restored = template with { IsActive = active };
		DocumentTemplateValidator.ValidateAndThrow(restored);
		return restored;
	}
}
