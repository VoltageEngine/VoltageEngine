using NUnit.Framework;


namespace Nez.Persistence.JsonTests
{
	[TestFixture]
	public class BasicTypeEncodingTests
	{
		[Test]
		public void DumpBoolean()
		{
			Assert.That( "true", Is.EqualTo(Json.ToJson(true)));
			Assert.That("false", Is.EqualTo(Json.ToJson(false)));
		}

		[Test]
		public void DumpNumber()
		{
			Assert.That("12345", Is.EqualTo(Json.ToJson(12345)));
			Assert.That("12.34", Is.EqualTo(Json.ToJson(12.34)));
		}

		[Test]
		public void DumpString()
		{
			Assert.That("\"string\"", Is.EqualTo(Json.ToJson("string")));
		}

		[Test]
		public void DumpArray()
		{
			Assert.That("[1,true,\"three\"]", Is.EqualTo(Json.ToJson(Json.FromJson("[1,true,\"three\"]"))));
		}

		[Test]
		public void DumpObject()
		{
			Assert.That("{\"x\":1,\"y\":2}", Is.EqualTo(Json.ToJson(Json.FromJson("{\"x\":1,\"y\":2}"))));
		}
	}
}