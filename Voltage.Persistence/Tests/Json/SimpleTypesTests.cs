using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace Nez.Persistence.JsonTests
{
	[TestFixture]
	public class SimpleTypesTests
	{
		[Test]
		public void DumpBool()
		{
			Assert.That("true", Is.EqualTo(Json.ToJson(true)));
			Assert.That("false", Is.EqualTo(Json.ToJson(false)));
		}


		[Test]
		public void LoadBool()
		{
			Assert.That(true, Is.EqualTo((bool)Json.FromJson("true")));
			Assert.That(false, Is.EqualTo((bool)Json.FromJson("false")));
		}


		[Test]
		public void DumpIntegerTypes()
		{
			Assert.That("-12345", Is.EqualTo(Json.ToJson((short)(-12345))));
			Assert.That("-12345", Is.EqualTo(Json.ToJson((int)(-12345))));
			Assert.That("-12345", Is.EqualTo(Json.ToJson((long)(-12345))));

			Assert.That("12345", Is.EqualTo(Json.ToJson((ushort)12345)));
			Assert.That("12345", Is.EqualTo(Json.ToJson((uint)12345)));
			Assert.That("12345", Is.EqualTo(Json.ToJson((ulong)12345)));
		}


		[Test]
		public void LoadIntegerTypes()
		{
			Assert.That(-12345, Is.EqualTo(Json.FromJson<int>("-12345")));
		}


		[Test]
		public void DumpFloatTypes()
		{
			Assert.That("123.45", Is.EqualTo(Json.ToJson((float)123.45)));
			Assert.That("123.45", Is.EqualTo(Json.ToJson((double)123.45)));
		}


		[Test]
		public void DumpFloatTypesForGermanCulture()
		{
			var currentCulture = CultureInfo.CurrentCulture;
			CultureInfo.CurrentCulture = new CultureInfo( "de", false );
			Assert.That("123.45", Is.EqualTo(Json.ToJson((float)123.45)));
			Assert.That("123.45", Is.EqualTo(Json.ToJson((double)123.45)));
			CultureInfo.CurrentCulture = currentCulture;
		}


		[Test]
		public void DumpDecimalType()
		{
			Assert.That("79228162514264337593543950335", Is.EqualTo(Json.ToJson(decimal.MaxValue)));
			Assert.That("-79228162514264337593543950335", Is.EqualTo(Json.ToJson(decimal.MinValue)));
		}


		[Test]
		public void LoadFloatTypes()
		{
			Assert.That(123.45f, Is.EqualTo(Json.FromJson<float>("123.45")));
		}


		[Test]
		public void DumpString()
		{
			Assert.That("\"OHAI! Can haz ball of strings?\"", Is.EqualTo(Json.ToJson("OHAI! Can haz ball of strings?")));
			Assert.That("\"\\\"\"", Is.EqualTo(Json.ToJson("\"")));
			Assert.That("\"\\\\\"", Is.EqualTo(Json.ToJson("\\")));
			Assert.That("\"\\b\"", Is.EqualTo(Json.ToJson("\b")));
			Assert.That("\"\\f\"", Is.EqualTo(Json.ToJson("\f")));
			Assert.That("\"\\n\"", Is.EqualTo(Json.ToJson("\n")));
			Assert.That("\"\\r\"", Is.EqualTo(Json.ToJson("\r")));
			Assert.That("\"\\t\"", Is.EqualTo(Json.ToJson("\t")));
			Assert.That("\"c\"", Is.EqualTo(Json.ToJson('c')));
		}


		[Test]
		public void LoadString()
		{
			Assert.That("OHAI! Can haz ball of strings?", Is.EqualTo((string)Json.FromJson("\"OHAI! Can haz ball of strings?\"")));
			Assert.That("\"", Is.EqualTo((string)Json.FromJson("\"\\\"\"")));
			Assert.That("\\", Is.EqualTo((string)Json.FromJson("\"\\\\\"")));
			Assert.That("\b", Is.EqualTo((string)Json.FromJson("\"\\b\"")));
			Assert.That("\f", Is.EqualTo((string)Json.FromJson("\"\\f\"")));
			Assert.That("\n", Is.EqualTo((string)Json.FromJson("\"\\n\"")));
			Assert.That("\r", Is.EqualTo((string)Json.FromJson("\"\\r\"")));
			Assert.That("\t", Is.EqualTo((string)Json.FromJson("\"\\t\"")));
		}


		[Test]
		public void DumpNull()
		{
			List<int> list = null;
			Assert.That("null", Is.EqualTo(Json.ToJson(list)));
			Assert.That("null", Is.EqualTo(Json.ToJson(null)));
		}


		[Test]
		public void LoadNull()
		{
			Assert.That(null, Is.EqualTo(Json.FromJson("null")));
		}


		class ValueTypes
		{
			public short i16 = 1;
			public ushort u16 = 2;
			public int i32 = 3;
			public uint u32 = 4;
			public long i64 = 5;
			public ulong u64 = 6;
			public float s = 7;
			public double d = 8;
			public decimal m = 9;
			public bool b = true;
		}


		[Test]
		public void AOTCompatibility()
		{
			var item = new ValueTypes();
			const string json = "{\"i16\":1,\"u16\":2,\"i32\":3,\"u32\":4,\"i64\":5,\"u64\":6,\"s\":7,\"d\":8,\"m\":9,\"b\":true}";

			Assert.DoesNotThrow( () => Json.FromJson<ValueTypes>( json ) );
			Assert.DoesNotThrow( () => Json.FromJsonOverwrite( json, item ) );
		}
	}
}