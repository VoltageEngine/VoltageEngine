using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Nez.Persistence.NsonTests
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

			[NonSerialized]
			public int z;

			public List<int> list;


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

			var json = Nson.ToNson( testClass );
			Assert.That("Nez.Persistence.NsonTests.ClassTypeTests+TestClass(date:\"2020-01-01T00:00:00Z\",x:5,y:7,list:[3,1,4])", Is.EqualTo(json));

			Assert.That(BeforeEncodeCallbackFired, Is.True);
		}

		[Test]
		public void DumpClassPrettyPrint()
		{
			var testClass = new TestClass { x = 5, y = 7, z = 0 };
			testClass.list = new List<int> { 3, 1, 4 };

			var settings = new NsonSettings()
			{
				PrettyPrint = true
			};
            var json = Nson.ToNson(testClass, settings);

            Assert.That(@"Nez.Persistence.NsonTests.ClassTypeTests+TestClass(
    date: ""2020-01-01T00:00:00Z"",
    x: 5,
    y: 7,
    list: [
        3,
        1,
        4
    ]
)".Replace("    ", "\t"), Is.EqualTo(json));
		}

		[Test]
		public void LoadClass()
        {
            var testClass = Nson.FromNson<TestClass>("Nez.Persistence.NsonTests.ClassTypeTests+TestClass(date:\"2020-01-01T00:00:00Z\",x:5,y:7,list:[3,1,4])");

			Assert.That(new DateTime(2020, 1, 1), Is.EqualTo(testClass.date));
			Assert.That(5, Is.EqualTo(testClass.x));
			Assert.That(7, Is.EqualTo(testClass.y));
			Assert.That(0, Is.EqualTo(testClass.z)); // should not get assigned

			Assert.That(3, Is.EqualTo(testClass.list.Count));
			Assert.That(3, Is.EqualTo(testClass.list[0]));
			Assert.That(1, Is.EqualTo(testClass.list[1]));
			Assert.That(4, Is.EqualTo(testClass.list[2]));

			Assert.That( AfterDecodeCallbackFired, Is.True);
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
            var nson = Nson.ToNson(outerClass);

            Assert.That("Nez.Persistence.NsonTests.ClassTypeTests+OuterClass(inner:Nez.Persistence.NsonTests.ClassTypeTests+InnerClass())", Is.EqualTo(nson));

            var back = Nson.FromNson(nson);
            Assert.That(back.GetType() == typeof(OuterClass), Is.True);
		}

	}
}