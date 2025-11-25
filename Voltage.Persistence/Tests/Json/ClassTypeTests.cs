using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Nez.Persistence.JsonTests
{
	[TestFixture]
	public class ClassTypeTests
	{
		public static bool AfterDecodeCallbackFired;
		public static bool BeforeEncodeCallbackFired;


		class TestClass
		{
			public DateTime date = new DateTime( 2020, 1, 1 );
			public int x;
			public int y;

			[JsonExclude]
			public int z;

			public List<int> list;

			[JsonInclude]
			public int p1 { get; set; }

			[JsonInclude]
			public int p2 { get; private set; }

			[JsonInclude]
			public int p3 { get; }


			public TestClass()
			{
				p1 = 1;
				p2 = 2;
				p3 = 3;
			}


			[AfterDecode]
			public void AfterDecode()
			{
				AfterDecodeCallbackFired = true;
			}


			[BeforeEncode]
			public void BeforeDecode()
			{
				BeforeEncodeCallbackFired = true;
			}
		}


		[Test]
		public void DumpClass()
		{
			var testClass = new TestClass { x = 5, y = 7, z = 0 };
			testClass.list = new List<int> { 3, 1, 4 };

			var json = Json.ToJson( testClass );
			Assert.That( "{\"date\":\"2020-01-01T00:00:00Z\",\"x\":5,\"y\":7,\"list\":[3,1,4],\"p1\":1,\"p2\":2,\"p3\":3}", Is.EqualTo(json) );

			Assert.That( BeforeEncodeCallbackFired, Is.True);
		}

		[Test]
		public void DumpClassNoTypeHint()
		{
			var testClass = new TestClass { x = 5, y = 7, z = 0 };
			testClass.list = new List<int> { 3, 1, 4 };

			var settings = new JsonSettings()
			{
				TypeNameHandling = TypeNameHandling.None
			};
			var json = Json.ToJson( testClass, settings );
			Assert.That("{\"date\":\"2020-01-01T00:00:00Z\",\"x\":5,\"y\":7,\"list\":[3,1,4],\"p1\":1,\"p2\":2,\"p3\":3}", Is.EqualTo(json));
		}

		[Test]
		public void DumpClassPrettyPrint()
		{
			var testClass = new TestClass { x = 5, y = 7, z = 0 };
			testClass.list = new List<int> { 3, 1, 4 };

			var settings = new JsonSettings()
			{
				TypeNameHandling = TypeNameHandling.None,
				PrettyPrint = true
			};
			Assert.That(@"{
	""date"": ""2020-01-01T00:00:00Z"",
	""x"": 5,
	""y"": 7,
	""list"": [
		3,
		1,
		4
	],
	""p1"": 1,
	""p2"": 2,
	""p3"": 3
}", Is.EqualTo(Json.ToJson(testClass, settings)));
		}

		[Test]
		public void DumpClassIncludePublicProperties()
		{
			var testClass = new TestClass { x = 5, y = 7, z = 0 };
			Assert.That("{\"date\":\"2020-01-01T00:00:00Z\",\"x\":5,\"y\":7,\"list\":null,\"p1\":1,\"p2\":2,\"p3\":3}", Is.EqualTo(Json.ToJson(testClass)));
		}

		[Test]
		public void LoadClass()
		{
			var testClass = Json.FromJson<TestClass>( "{\"date\":\"2020-01-01T00:00:00Z\",\"x\":5,\"y\":7,\"z\":3,\"list\":[3,1,4],\"p1\":1,\"p2\":2,\"p3\":3}" );

			Assert.That(new DateTime(2020, 1, 1), Is.EqualTo(testClass.date));
			Assert.That(5, Is.EqualTo(testClass.x));
			Assert.That(7, Is.EqualTo(testClass.y));
			Assert.That(0, Is.EqualTo(testClass.z)); // should not get assigned

			Assert.That(3, Is.EqualTo(testClass.list.Count));
			Assert.That(3, Is.EqualTo(testClass.list[0]));
			Assert.That(1, Is.EqualTo(testClass.list[1]));
			Assert.That(4, Is.EqualTo(testClass.list[2]));

			Assert.That(1, Is.EqualTo(testClass.p1));
			Assert.That(2, Is.EqualTo(testClass.p2));
			Assert.That(3, Is.EqualTo(testClass.p3));

			Assert.That( AfterDecodeCallbackFired, Is.True );
		}


		class InnerClass { }

		class OuterClass
		{
			public InnerClass inner;
		}

		[Test]
		public void DumpOuterClassWithNoTypeHintPropagatesToInnerClasses()
		{
			var outerClass = new OuterClass();
			outerClass.inner = new InnerClass();
			Assert.That("{\"inner\":{}}", Is.EqualTo(Json.ToJson(outerClass)));
		}

	}
}