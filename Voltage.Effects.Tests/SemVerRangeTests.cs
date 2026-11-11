using NUnit.Framework;
using Voltage.Editor.Plugins;

namespace Voltage.Effects.Tests;

[TestFixture]
public class SemVerRangeTests
{
	[TestCase("0.2.2.1", ">=0.2.0", true)]
	[TestCase("0.2.2.1", ">=0.2.2", true)]
	[TestCase("0.2.2.1", ">0.2.2", true)]
	[TestCase("0.2.2.1", "<0.2.3", true)]
	[TestCase("0.2.2.1", ">=0.2.2.2", false)]
	[TestCase("0.2.2", ">=0.2.2.1", false)]
	[TestCase("0.2.2.1", "0.2.2.1", true)]
	[TestCase("0.2.2.1.4", ">=0.2.0", false)]
	public void A_hotfix_number_orders_after_the_release_it_patches(string version, string range, bool expected)
	{
		Assert.That(SemVerRange.Satisfies(version, range), Is.EqualTo(expected));
	}

	[Test]
	public void A_hotfix_is_newer_than_its_release_and_older_than_the_next()
	{
		Assert.That(SemVerRange.IsNewer("0.2.2.1", "0.2.2"), Is.True);
		Assert.That(SemVerRange.IsNewer("0.2.3", "0.2.2.1"), Is.True);
	}
}
