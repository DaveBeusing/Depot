// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace Depot.Services.Help;

public sealed partial class DiagnosticsSanitizer
{
	private const string Mask = "[REDACTED]";

	public string Sanitize(string? diagnostics)
	{
		if (string.IsNullOrWhiteSpace(diagnostics)) return string.Empty;
		var sanitized = ConnectionStringLineRegex().Replace(diagnostics, match => $"{match.Groups[1].Value}{Mask}");
		sanitized = SensitiveAssignmentRegex().Replace(sanitized, match => $"{match.Groups[1].Value}{match.Groups[2].Value}{Mask}");
		sanitized = SensitiveJsonRegex().Replace(sanitized, match => $"{match.Groups[1].Value}{Mask}{match.Groups[2].Value}");
		return sanitized;
	}

	[GeneratedRegex("(?im)^(.*connection\\s*string\\s*[:=]\\s*).*$")]
	private static partial Regex ConnectionStringLineRegex();
	[GeneratedRegex("(?i)\\b(password|pwd|hash|salt|secret|access[_-]?token|refresh[_-]?token|encryption[_-]?key|private[_-]?key|protected[_-]?configuration|sensitive[_-]?sql[_-]?parameter)(\\s*[:=]\\s*)[^;\\r\\n,}]+")]
	private static partial Regex SensitiveAssignmentRegex();
	[GeneratedRegex("(?i)(\"(?:password|pwd|hash|salt|secret|connectionString|protectedConfiguration)\"\\s*:\\s*\")[^\"]*(\")")]
	private static partial Regex SensitiveJsonRegex();
}
