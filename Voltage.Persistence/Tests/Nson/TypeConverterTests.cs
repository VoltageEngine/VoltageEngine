using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Nez.Persistence.NsonTests
{
    [TestFixture]
	public class TypeConverterTests
	{
		class Doodle
		{
			public int x;
			public int y;
			public int z;

			[NonSerialized]
			public int totalOrphanedKeys;
			[NonSerialized]
			public bool wasCreatedByObjectFactory;
		}

		class DoodleContainer
		{
			public Doodle firstDoodle;
			public Doodle secondDoodle;
		}

		class CustomDataConverter : NsonTypeConverter<Doodle>
		{
			public override void WriteNson( INsonEncoder encoder, Doodle value )
			{
				encoder.EncodeKeyValuePair( "key-that-isnt-on-object", true );
				encoder.EncodeKeyValuePair( "another_key", "with a value" );
			}

			public override Doodle OnFoundCustomData( Doodle instance, string key, object value )
			{
				instance.totalOrphanedKeys++;
				System.Console.WriteLine( $"field name: {key}, value: {value}" );
                return instance;
			}
		}

		class WantsExclusiveWriteConverter : NsonTypeConverter<Doodle>
		{
			public override bool WantsExclusiveWrite => true;

			public override void WriteNson( INsonEncoder encoder, Doodle value )
			{
				encoder.EncodeKeyValuePair( "key-that-isnt-on-object", true );
				encoder.EncodeKeyValuePair( "another_key", "with a value" );
				encoder.EncodeKeyValuePair( "string_array", new string[] { "first", "second" } );
			}

            public override Doodle OnFoundCustomData(Doodle instance, string key, object value)
            {
                return instance;
            }
		}

		class ObjectFactoryConverter : NsonObjectFactory<Doodle>
		{
			public override Doodle Create( Type objectType, Dictionary<string, object> objectData )
			{
				var doodle = new Doodle
				{
					wasCreatedByObjectFactory = true
				};

				doodle.x = Convert.ToInt32( objectData["x"] );
				doodle.y = Convert.ToInt32( objectData["y"] );
				doodle.z = Convert.ToInt32( objectData["z"] );

				return doodle;
			}
		}


		[Test]
		public void Converter_WriteJson()
		{
			var doodle = new Doodle { x = 5, y = 7, z = 9 };
			var json = Nson.ToNson( doodle, new CustomDataConverter() );

			Assert.That( json.Contains( "key-that-isnt-on-object" ), Is.True);
			Assert.That(json.Contains("another_key"), Is.True);
		}

		[Test]
		public void Converter_OnFoundCustomData()
		{
			var settings = new NsonSettings { TypeConverters = new NsonTypeConverter[] { new CustomDataConverter() } };
			var doodle = new Doodle { x = 5, y = 7, z = 9 };
			var nson = Nson.ToNson( doodle, settings );

			var newDoodle = Nson.FromNson<Doodle>( nson, settings );

			Assert.That( 2, Is.EqualTo(newDoodle.totalOrphanedKeys) );
			Assert.That( doodle.totalOrphanedKeys, Is.Not.EqualTo(newDoodle.totalOrphanedKeys) );
		}

		[Test]
		public void Converter_WantsExclusiveWrite()
		{
			var doodle = new Doodle { x = 5, y = 7, z = 9 };
			var json = Nson.ToNson( doodle, new WantsExclusiveWriteConverter() );

			Assert.That(json.Contains("key-that-isnt-on-object"), Is.True);
			Assert.That(json.Contains("another_key"), Is.True);
			Assert.That(json.Contains("string_array"), Is.True);
			Assert.That( json.Contains( "x" ), Is.False );
		}

		[Test]
		public void Converter_ObjectFactory()
		{
			var doodle = new Doodle { x = 5, y = 7, z = 9 };
			var json = Nson.ToNson( doodle );

			var settings = new NsonSettings { TypeConverters = new NsonTypeConverter[] { new ObjectFactoryConverter() } };
			var newDoodle = Nson.FromNson<Doodle>( json, settings );

			Assert.That(newDoodle.wasCreatedByObjectFactory, Is.True);

			Assert.That(newDoodle.x, Is.EqualTo(doodle.x));
			Assert.That(newDoodle.y, Is.EqualTo(doodle.y));
			Assert.That(newDoodle.z, Is.EqualTo(doodle.z));
		}

		[Test]
		public void Converter_ObjectFactoryNested()
		{
			var container = new DoodleContainer
			{
				firstDoodle = new Doodle { x = 1, y = 2, z = 3 },
				secondDoodle = new Doodle { x = 4, y = 5, z = 5 }
			};
			var json = Nson.ToNson( container );

			var settings = new NsonSettings { TypeConverters = new NsonTypeConverter[] { new ObjectFactoryConverter() } };
			var newContainer = Nson.FromNson<DoodleContainer>( json, settings );

			Assert.That(newContainer.firstDoodle.wasCreatedByObjectFactory, Is.True);
			Assert.That(newContainer.secondDoodle.wasCreatedByObjectFactory, Is.True);

			Assert.That(container.firstDoodle.x, Is.EqualTo(newContainer.firstDoodle.x));
			Assert.That(container.firstDoodle.y, Is.EqualTo(newContainer.firstDoodle.y));
			Assert.That(container.firstDoodle.z, Is.EqualTo(newContainer.firstDoodle.z));

			Assert.That(container.secondDoodle.x, Is.EqualTo(newContainer.secondDoodle.x));
			Assert.That(container.secondDoodle.y, Is.EqualTo(newContainer.secondDoodle.y));
			Assert.That(container.secondDoodle.z, Is.EqualTo(newContainer.secondDoodle.z));
		}

		[Test]
		public void Converter_ObjectFactoryList()
		{
			var list = new List<Doodle>
			{
				{ new Doodle { x = 1, y = 2, z = 3 } },
				{ new Doodle { x = 4, y = 5, z = 5 } }
			};
			var json = Nson.ToNson( list );

			var settings = new NsonSettings { TypeConverters = new NsonTypeConverter[] { new ObjectFactoryConverter() } };
			var newList = Nson.FromNson<List<Doodle>>( json, settings );

			Assert.That(newList[0].wasCreatedByObjectFactory, Is.True);
			Assert.That(newList[1].wasCreatedByObjectFactory, Is.True);

			Assert.That( list[0].x, Is.EqualTo(newList[0].x) );
			Assert.That(list[0].y, Is.EqualTo(newList[0].y));
			Assert.That(list[0].z, Is.EqualTo(newList[0].z));

			Assert.That(list[1].x, Is.EqualTo(newList[1].x));
			Assert.That(list[1].y, Is.EqualTo(newList[1].y));
			Assert.That(list[1].z, Is.EqualTo(newList[1].z));
		}

		[Test]
		public void Converter_ObjectFactoryWithTypeHint()
		{
			var doodle = new Doodle { x = 5, y = 7, z = 9 };
			var json = Nson.ToNson( doodle );

			var settings = new NsonSettings { TypeConverters = new NsonTypeConverter[] { new ObjectFactoryConverter() } };
			var newDoodle = Nson.FromNson<Doodle>( json, settings );

			Assert.That(newDoodle.wasCreatedByObjectFactory, Is.True);

			Assert.That(newDoodle.x, Is.EqualTo(doodle.x));
			Assert.That(newDoodle.y, Is.EqualTo(doodle.y));
			Assert.That(newDoodle.z, Is.EqualTo(doodle.z));
		}

		[Test]
		public void Converter_ObjectFactoryNestedWithTypeHint()
		{
			var container = new DoodleContainer
			{
				firstDoodle = new Doodle { x = 1, y = 2, z = 3 },
				secondDoodle = new Doodle { x = 4, y = 5, z = 5 }
			};
			var json = Nson.ToNson( container );

			var settings = new NsonSettings { TypeConverters = new NsonTypeConverter[] { new ObjectFactoryConverter() } };
			var newContainer = Nson.FromNson<DoodleContainer>( json, settings );

			Assert.That(newContainer.firstDoodle.wasCreatedByObjectFactory, Is.True);
			Assert.That(newContainer.secondDoodle.wasCreatedByObjectFactory, Is.True);

			Assert.That(container.firstDoodle.x, Is.EqualTo(newContainer.firstDoodle.x));
			Assert.That(container.firstDoodle.y, Is.EqualTo(newContainer.firstDoodle.y));
			Assert.That(container.firstDoodle.z, Is.EqualTo(newContainer.firstDoodle.z));

			Assert.That(container.secondDoodle.x, Is.EqualTo(newContainer.secondDoodle.x));
			Assert.That(container.secondDoodle.y, Is.EqualTo(newContainer.secondDoodle.y));
			Assert.That(container.secondDoodle.z, Is.EqualTo(newContainer.secondDoodle.z));
		}

		[Test]
		public void Converter_ObjectFactoryListWithTypeHint()
		{
			var list = new List<Doodle>
			{
				{ new Doodle { x = 1, y = 2, z = 3 } },
				{ new Doodle { x = 4, y = 5, z = 5 } }
			};
			var json = Nson.ToNson( list );

			var settings = new NsonSettings { TypeConverters = new NsonTypeConverter[] { new ObjectFactoryConverter() } };
			var newList = Nson.FromNson<List<Doodle>>( json, settings );

			Assert.That(newList[0].wasCreatedByObjectFactory, Is.True);
			Assert.That(newList[1].wasCreatedByObjectFactory, Is.True);

			Assert.That(list[0].x, Is.EqualTo(newList[0].x));
			Assert.That(list[0].y, Is.EqualTo(newList[0].y));
			Assert.That(list[0].z, Is.EqualTo(newList[0].z));

			Assert.That(list[1].x, Is.EqualTo(newList[1].x));
			Assert.That(list[1].y, Is.EqualTo(newList[1].y));
			Assert.That(list[1].z, Is.EqualTo(newList[1].z));
		}

	}
}