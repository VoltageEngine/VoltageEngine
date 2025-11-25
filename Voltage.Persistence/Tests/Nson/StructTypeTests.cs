using Nez.Persistence;
using NUnit.Framework;
using System;


namespace Nez.Persistence.NsonTests
{
	[TestFixture]
	public class StructTypeTests
	{
		public static bool LoadCallbackFired;

		struct TestStruct
		{
			public int x;
			public int y;

			[NonSerialized]
			public int z;

			[AfterDecodeAttribute]
			public void OnLoad()
			{
				LoadCallbackFired = true;
			}
		}


		[Test]
		public void DumpStruct()
		{
			var testStruct = new TestStruct { x = 5, y = 7, z = 0 };
            var json = Nson.ToNson(testStruct);

            Assert.That("Nez.Persistence.NsonTests.StructTypeTests+TestStruct(x:5,y:7)", Is.EqualTo(json) );
		}


		[Test]
		public void LoadStruct()
		{
			var testStruct = Nson.FromNson<TestStruct>("Nez.Persistence.NsonTests.StructTypeTests+TestStruct(x:5,y:7)");

			Assert.That(5, Is.EqualTo(testStruct.x));
			Assert.That(7, Is.EqualTo(testStruct.y));
			Assert.That(0, Is.EqualTo(testStruct.z)); // should not get assigned

			Assert.That( LoadCallbackFired, Is.True);
		}
	}
}