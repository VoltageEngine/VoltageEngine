using System.Linq;
using NUnit.Framework;
using Voltage.Editor.Utils;

namespace Voltage.Effects.Tests;

[TestFixture]
public class LaunchPathTests
{
	private const string LaunchdPath = "/usr/bin:/bin:/usr/sbin:/sbin";

	[Test]
	public void A_Dock_launch_gains_dotnet_and_Homebrew_without_reordering_its_own_entries()
	{
		var present = new[] { "/usr/local/bin", "/opt/homebrew/bin", "/Users/dev/.dotnet" };
		var entries = LaunchPath.Complete(LaunchdPath, null, "/Users/dev", present.Contains).Split(':');

		Assert.That(entries.Take(4), Is.EqualTo(LaunchdPath.Split(':')));
		Assert.That(entries, Is.SupersetOf(present));
		Assert.That(entries, Is.Unique);
	}

	[Test]
	public void Missing_directories_and_entries_already_present_are_left_alone()
	{
		var path = "/usr/local/bin:" + LaunchdPath;
		Assert.That(LaunchPath.Complete(path, null, "/Users/dev", dir => dir == "/usr/local/bin"), Is.EqualTo(path));
	}

	[Test]
	public void DOTNET_ROOT_is_added_first()
	{
		var entries = LaunchPath.Complete(LaunchdPath, "/opt/dotnet", "/Users/dev", dir => dir is "/opt/dotnet" or "/usr/local/bin").Split(':');
		Assert.That(entries.Skip(4), Is.EqualTo(new[] { "/opt/dotnet", "/usr/local/bin" }));
	}
}
