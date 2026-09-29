using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Inventory.Tests.Support;

/// <summary>A directory under the temp folder, deleted afterwards.</summary>
public sealed class TempDirectory : IDisposable
{
	public TempDirectory() => Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "inventory-tests", Guid.NewGuid().ToString("N"));

	public string Path { get; }

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(Path))
			{
				Directory.Delete(Path, recursive: true);
			}
		}
		catch (IOException)
		{
			// A sink still closing; the temp folder is the OS's to tidy.
		}
		catch (UnauthorizedAccessException)
		{
			// As above.
		}
	}
}
