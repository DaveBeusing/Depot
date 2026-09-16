using System.IO;
using System.Reflection.PortableExecutable;

namespace DepotManager;

public static class PortableExecutableValidator
{
	public static void ValidateWindowsExecutable(string path)
	{
		try
		{
			using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var reader = new PEReader(stream);
			var headers = reader.PEHeaders;
			var peHeader = headers.PEHeader;
			if (peHeader is null || peHeader.Subsystem is not (Subsystem.WindowsGui or Subsystem.WindowsCui))
				throw InvalidExecutable();

			if (peHeader.SizeOfHeaders <= 0 || peHeader.SizeOfHeaders > stream.Length)
				throw InvalidExecutable();

			foreach (var section in headers.SectionHeaders)
			{
				if (section.PointerToRawData < 0 || section.SizeOfRawData < 0)
					throw InvalidExecutable();
				if (section.SizeOfRawData == 0)
					continue;

				var sectionEnd = (long)section.PointerToRawData + section.SizeOfRawData;
				if (section.PointerToRawData < peHeader.SizeOfHeaders || sectionEnd > stream.Length)
					throw InvalidExecutable();
			}
		}
		catch (BadImageFormatException exception)
		{
			throw InvalidExecutable(exception);
		}
		catch (EndOfStreamException exception)
		{
			throw InvalidExecutable(exception);
		}
	}

	private static InvalidOperationException InvalidExecutable(Exception? innerException = null) =>
		new("The downloaded asset is not a valid Windows executable.", innerException);
}
