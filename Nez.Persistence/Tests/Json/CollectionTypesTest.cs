using System;
using System.Collections;
using System.Collections.Generic;
using Nez.Persistence;
using NUnit.Framework;

namespace Nez.Persistence.JsonTests
{
	[TestFixture]
	public class CollectionTypesTest
	{
		[Test]
		public void DumpRank1Array()
		{
			var array = new[] { 3, 1, 4 };
			Assert.That("[3,1,4]", Is.EqualTo(Json.ToJson(array)));
		}

		[Test]
		public void DumpRank2Array()
		{
			var array = new[,] { { 1, 2, 3 }, { 4, 5, 6 } };
			Assert.That("[[1,2,3],[4,5,6]]", Is.EqualTo(Json.ToJson(array)));
		}

		[Test]
		public void DumpRank3Array()
		{
			var array = new[, ,] { { { 1, 2 }, { 3, 4 } }, { { 5, 6 }, { 7, 8 } }, { { 9, 0 }, { 1, 2 } } };
			Assert.That("[[[1,2],[3,4]],[[5,6],[7,8]],[[9,0],[1,2]]]", Is.EqualTo(Json.ToJson(array)));
		}

		[Test]
		public void DumpJaggedArray()
		{
			var array = new[] { new[] { 1, 2, 3 }, new[] { 4, 5, 6 } };
			Assert.That("[[1,2,3],[4,5,6]]", Is.EqualTo(Json.ToJson(array)));
		}


		[Test]
		public void LoadRank1Array()
		{
			var json = "[1,2,3]";
			var array = Json.FromJson<int[]>( json );

			Assert.That(null, Is.Not.EqualTo(array));
			Assert.That(3, Is.EqualTo(array.Length));
			Assert.That(1, Is.EqualTo(array[0]));
			Assert.That(2, Is.EqualTo(array[1]));
			Assert.That(3, Is.EqualTo(array[2]));
		}

		[Test]
		public void LoadRank2Array()
		{
			var json = "[[1,2,3],[4,5,6]]";
			var array = Json.FromJson<int[,]>( json );

			Assert.That(null, Is.Not.EqualTo(array));

			Assert.That(2, Is.EqualTo(array.Rank));
			Assert.That(2, Is.EqualTo(array.GetLength(0)));
			Assert.That(3, Is.EqualTo(array.GetLength(1)));

			Assert.That(1, Is.EqualTo(array[0, 0]));
			Assert.That(2, Is.EqualTo(array[0, 1]));
			Assert.That(3, Is.EqualTo(array[0, 2]));

			Assert.That(4, Is.EqualTo(array[1, 0]));
			Assert.That(5, Is.EqualTo(array[1, 1]));
			Assert.That(6, Is.EqualTo(array[1, 2]));
		}

		[Test]
		public void LoadRank2StringArray()
		{
			var json = "[[\"1\",\"2\",\"3\"],[\"4\",\"5\",\"6\"]]";
			var array = Json.FromJson<string[,]>( json );

			Assert.That(null, Is.Not.EqualTo(array));

			Assert.That(2, Is.EqualTo(array.Rank));
			Assert.That(2, Is.EqualTo(array.GetLength(0)));
			Assert.That(3, Is.EqualTo(array.GetLength(1)));

			Assert.That( "1", Is.EqualTo(array[0, 0]) );
			Assert.That("2", Is.EqualTo(array[0, 1]));
			Assert.That("3", Is.EqualTo(array[0, 2]));

			Assert.That("4", Is.EqualTo(array[1, 0]));
			Assert.That("5", Is.EqualTo(array[1, 1]));
			Assert.That("6", Is.EqualTo(array[1, 2]));
		}

		struct TestStruct
		{
			public int x;
			public int y;
		}

		[Test]
		public void LoadRank2ObjectArray()
		{
			var json = "[[{\"x\":5,\"y\":7}, {\"x\":5,\"y\":7}],[{\"x\":5,\"y\":7}, {\"x\":77,\"y\":7}]]";
			var array = Json.FromJson<TestStruct[,]>( json );

			Assert.That(null, Is.Not.EqualTo(array));

			Assert.That(2, Is.EqualTo(array.Rank));
			Assert.That(2, Is.EqualTo(array.GetLength(0)));
			Assert.That(2, Is.EqualTo(array.GetLength(1)));

			Assert.That(77, Is.EqualTo(array[1, 1].x));
		}

		[Test]
		public void LoadRank3Array()
		{
			var json = "[[[1,2],[3,4]],[[5,6],[7,8]],[[9,0],[1,2]]]";
			var array = Json.FromJson<int[,,]>( json );
			Assert.That(null, Is.Not.EqualTo(array));

			Assert.That(3, Is.EqualTo(array.Rank));
			Assert.That(3, Is.EqualTo(array.GetLength(0)));
			Assert.That(2, Is.EqualTo(array.GetLength(1)));
			Assert.That(2, Is.EqualTo(array.GetLength(2)));

			Assert.That(1, Is.EqualTo(array[0, 0, 0]));
			Assert.That(2, Is.EqualTo(array[0, 0, 1]));

			Assert.That(3, Is.EqualTo(array[0, 1, 0]));
			Assert.That(4, Is.EqualTo(array[0, 1, 1]));

			Assert.That(5, Is.EqualTo(array[1, 0, 0]));
			Assert.That(6, Is.EqualTo(array[1, 0, 1]));

			Assert.That(7, Is.EqualTo(array[1, 1, 0]));
			Assert.That(8, Is.EqualTo(array[1, 1, 1]));

			Assert.That(9, Is.EqualTo(array[2, 0, 0]));
			Assert.That(0, Is.EqualTo(array[2, 0, 1]));

			Assert.That(1, Is.EqualTo(array[2, 1, 0]));
			Assert.That(2, Is.EqualTo(array[2, 1, 1]));
		}

		[Test]
		public void LoadJaggedArray()
		{
			var json = "[[1,2,3],[4,5,6]]";
			var array = Json.FromJson<int[][]>( json );
			Assert.That(null, Is.Not.EqualTo(array));

			Assert.That(2, Is.EqualTo(array.Length));

			Assert.That(3, Is.EqualTo(array[0].Length));
			Assert.That(1, Is.EqualTo(array[0][0]));
			Assert.That(2, Is.EqualTo(array[0][1]));
			Assert.That(3, Is.EqualTo(array[0][2]));

			Assert.That(3, Is.EqualTo(array[1].Length));
			Assert.That(4, Is.EqualTo(array[1][0]));
			Assert.That(5, Is.EqualTo(array[1][1]));
			Assert.That(6, Is.EqualTo(array[1][2]));
		}


		[Test]
		public void DumpList()
		{
			var list = new List<int>() { { 3 }, { 1 }, { 4 } };
			Assert.That("[3,1,4]", Is.EqualTo(Json.ToJson(list)));
		}

		[Test]
		public void LoadList()
		{
			var list = Json.FromJson<List<int>>( "[3,1,4]" );
			Assert.That(null, Is.Not.EqualTo(list));

			Assert.That(3, Is.EqualTo(list.Count));
			Assert.That(3, Is.EqualTo(list[0]));
			Assert.That(1, Is.EqualTo(list[1]));
			Assert.That(4, Is.EqualTo(list[2]));
		}

		class ListOfObjects
		{
			public int x = 5;
			public int y = 10;
		}

		[Test]
		public void DumpListOfObjects()
		{
			var list = new List<ListOfObjects> { { new ListOfObjects() }, { new ListOfObjects() }, { new ListOfObjects() } };
			var json = Json.ToJson( list );

			Assert.That("[{\"x\":5,\"y\":10},{\"x\":5,\"y\":10},{\"x\":5,\"y\":10}]", Is.EqualTo(Json.ToJson(list)));
		}

		[Test]
		public void LoadListOfObjects()
		{
			var list = Json.FromJson<List<ListOfObjects>>( "[{\"x\":5,\"y\":10},{\"x\":15,\"y\":10},{\"x\":25,\"y\":10}]" );
			Assert.That(null, Is.Not.EqualTo(list));

			Assert.That(3, Is.EqualTo(list.Count));
			Assert.That(5, Is.EqualTo(list[0].x));
			Assert.That(15, Is.EqualTo(list[1].x));
			Assert.That(25, Is.EqualTo(list[2].x));
		}

		[Test]
		public void DumpDict()
		{
			var dict = new Dictionary<string, float>();
			dict["foo"] = 1337f;
			dict["bar"] = 3.14f;

			Assert.That("{\"foo\":1337,\"bar\":3.14}", Is.EqualTo(Json.ToJson(dict)));
		}


		[Test]
		public void LoadDict()
		{
			var dict = Json.FromJson<Dictionary<string, float>>( "{\"foo\":1337,\"bar\":3.14}" );

			Assert.That(null, Is.Not.EqualTo(dict));
			Assert.That(2, Is.EqualTo(dict.Count));
			Assert.That(1337f, Is.EqualTo(dict["foo"]));
			Assert.That(3.14f, Is.EqualTo(dict["bar"]));
		}


		[Test]
		public void LoadDictIntoGeneric()
		{
			var dict = Json.FromJson( "{\"foo\":1337,\"bar\":3.14}" ) as IDictionary;

			Assert.That( dict, Is.Not.Null);
			Assert.That(2, Is.EqualTo(dict.Count));
			Assert.That(1337f, Is.EqualTo(Convert.ToSingle(dict["foo"])));
			Assert.That(3.14f, Is.EqualTo(Convert.ToSingle(dict["bar"])));
		}


		enum TestEnum
		{
			Thing1,
			Thing2,
			Thing3
		}

		[Test]
		public void DumpEnum()
		{
			const TestEnum testEnum = TestEnum.Thing2;
			Assert.That("\"Thing2\"", Is.EqualTo(Json.ToJson(testEnum)));
		}

		[Test]
		public void LoadEnum()
		{
			var testEnum = Json.FromJson<TestEnum>( "\"Thing2\"" );
			Assert.That(TestEnum.Thing2, Is.EqualTo(testEnum));

			try
			{
				Json.FromJson<TestEnum>( "\"Thing4\"" );
			}
			catch( ArgumentException e )
			{
				Assert.That(e.Message, Is.EqualTo("Requested value 'Thing4' was not found."));
			}
		}

		[Test]
		public void DumpDictWithEnumKeys()
		{
			var dict = new Dictionary<TestEnum, string>
			{
				[TestEnum.Thing1] = "Item 1",
				[TestEnum.Thing2] = "Item 2",
				[TestEnum.Thing3] = "Item 3"
			};
			Assert.That("{\"Thing1\":\"Item 1\",\"Thing2\":\"Item 2\",\"Thing3\":\"Item 3\"}", Is.EqualTo(Json.ToJson(dict)));
		}

		[Test]
		public void LoadDictWithEnumKeys()
		{
			const string json = "{\"Thing1\":\"Item 1\",\"Thing2\":\"Item 2\",\"Thing3\":\"Item 3\"}";
			var dict = Json.FromJson<Dictionary<TestEnum, string>>( json );

			Assert.That(null, Is.Not.EqualTo(dict));
			Assert.That(3, Is.EqualTo(dict.Count));
			Assert.That("Item 1", Is.EqualTo(dict[TestEnum.Thing1]));
			Assert.That("Item 2", Is.EqualTo(dict[TestEnum.Thing2]));
			Assert.That("Item 3", Is.EqualTo(dict[TestEnum.Thing3]));
		}

	}
}