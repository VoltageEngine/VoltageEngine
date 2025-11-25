using Nez.Persistence;
using NUnit.Framework;
using System;


namespace Nez.Persistence.JsonTests
{
	[TestFixture]
	public class StructTypeTests
	{
		public static bool LoadCallbackFired;

		struct TestStruct
		{
			public int x;
			public int y;

			[JsonExclude]
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

			Assert.That("{\"x\":5,\"y\":7}", Is.EqualTo(Json.ToJson(testStruct)));
		}


		[Test]
		public void LoadStruct()
		{
			var testStruct = Json.FromJson<TestStruct>( "{\"x\":5,\"y\":7,\"z\":3}" );

			Assert.That(5, Is.EqualTo(testStruct.x));
			Assert.That(7, Is.EqualTo(testStruct.y));
			Assert.That(0, Is.EqualTo(testStruct.z)); // should not get assigned

			Assert.That(LoadCallbackFired, Is.True);
		}
	}
}