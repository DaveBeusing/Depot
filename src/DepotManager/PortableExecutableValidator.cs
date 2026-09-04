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
			var peHeader = reader.PEHeaders.PEHeader;
			if (peHeader is null || peHeader.Subsystem is not (Subsystem.WindowsGui or Subsystem.WindowsCui))
				throw new InvalidOperationException("The downloaded asset is not a valid Windows executable.");
		}
		catch (BadImageFormatException exception)
		{
			throw new InvalidOperationException("The downloaded asset is not a valid Windows executable.", exception);
		}
	}
}
