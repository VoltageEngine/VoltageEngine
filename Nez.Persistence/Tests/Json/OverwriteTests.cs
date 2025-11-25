using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Nez.Persistence.JsonTests
{
	[TestFixture]
	public class OverwriteTests
	{
		class TestClass
		{
			public int x;
			public int y;

			[JsonExclude]
			public int z;

			public List<int> list;

			public int p1 { get; set; }

			public int p2 { get; private set; }
			public int p3 { get; }


			public TestClass()
			{
				p1 = 1;
				p2 = 2;
				p3 = 3;
			}
		}


		[Test]
		public void OverwriteField()
		{
			var json = "{\"x\":5,\"y\":7,\"list\":[3,1,4],\"p1\":1,\"p2\":2,\"p3\":3}";

			var testClass = Json.FromJson<TestClass>( json );
			Assert.That(5, Is.EqualTo(testClass.x));

			Json.FromJsonOverwrite( "{\"x\":10}", testClass );
			Assert.That(10, Is.EqualTo(testClass.x));
			Assert.That(7, Is.EqualTo(testClass.y));

			Assert.That(3, Is.EqualTo(testClass.list.Count));
			Assert.That( 3, Is.EqualTo(testClass.list[0]) );
			Assert.That(1, Is.EqualTo(testClass.list[1]));
			Assert.That(4, Is.EqualTo(testClass.list[2]));

			Assert.That(1, Is.EqualTo(testClass.p1));
			Assert.That(2, Is.EqualTo(testClass.p2));
			Assert.That(3, Is.EqualTo(testClass.p3));
		}


		[Test]
		public void OverwriteArray()
		{
			var json = "{\"x\":5,\"y\":7,\"list\":[3,1,4],\"p1\":1,\"p2\":2,\"p3\":3}";

			var testClass = Json.FromJson<TestClass>( json );
			Assert.That(3, Is.EqualTo(testClass.list.Count));

			Json.FromJsonOverwrite( "{\"list\":[5,6,7,8]}", testClass );
			Assert.That(5, Is.EqualTo(testClass.x));
			Assert.That(7, Is.EqualTo(testClass.y));

			Assert.That(4, Is.EqualTo(testClass.list.Count));
			Assert.That(5, Is.EqualTo(testClass.list[0]));
			Assert.That(6, Is.EqualTo(testClass.list[1]));
			Assert.That(7, Is.EqualTo(testClass.list[2]));
			Assert.That(8, Is.EqualTo(testClass.list[3]));

			Assert.That(1, Is.EqualTo(testClass.p1));
			Assert.That(2, Is.EqualTo(testClass.p2));
			Assert.That(3, Is.EqualTo(testClass.p3));
		}

	}
}