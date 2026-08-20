// Copyright (c) 2026 David Beusing
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text;

namespace Depot.Services;

public sealed class SalesDocumentEmailService
{
	public string CreateDraft(string pdfPath,string? recipient,string subject,string body)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pdfPath);
		if(!File.Exists(pdfPath))throw new FileNotFoundException("The sales document does not exist.",pdfPath);
		var emlPath=Path.Combine(Path.GetTempPath(),$"depot-mail-{Guid.NewGuid():N}.eml");
		var boundary=$"----Depot-{Guid.NewGuid():N}";
		var fileName=Path.GetFileName(pdfPath);
		var builder=new StringBuilder();
		builder.AppendLine($"To: {recipient??string.Empty}");
		builder.AppendLine($"Subject: {subject.Replace("\r",string.Empty).Replace("\n",string.Empty)}");
		builder.AppendLine("MIME-Version: 1.0");
		builder.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
		builder.AppendLine();
		builder.AppendLine($"--{boundary}");
		builder.AppendLine("Content-Type: text/plain; charset=utf-8");
		builder.AppendLine("Content-Transfer-Encoding: 8bit");
		builder.AppendLine();
		builder.AppendLine(body);
		builder.AppendLine();
		builder.AppendLine($"--{boundary}");
		builder.AppendLine($"Content-Type: application/pdf; name=\"{fileName}\"");
		builder.AppendLine("Content-Transfer-Encoding: base64");
		builder.AppendLine($"Content-Disposition: attachment; filename=\"{fileName}\"");
		builder.AppendLine();
		var base64=Convert.ToBase64String(File.ReadAllBytes(pdfPath));
		for(var index=0;index<base64.Length;index+=76)builder.AppendLine(base64.Substring(index,Math.Min(76,base64.Length-index)));
		builder.AppendLine($"--{boundary}--");
		File.WriteAllText(emlPath,builder.ToString(),new UTF8Encoding(false));
		return emlPath;
	}

	public void OpenDraft(string emlPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(emlPath);
		Process.Start(new ProcessStartInfo(emlPath){UseShellExecute=true});
	}
}
