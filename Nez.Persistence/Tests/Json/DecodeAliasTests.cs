using Nez.Persistence;
using NUnit.Framework;


namespace Nez.Persistence.JsonTests
{
	[TestFixture]
	public class DecodeAliasTests
	{
		class AliasData
		{
			[DecodeAlias( "numberFieldAlias" )]
			public int NumberField;

			[JsonInclude]
			[DecodeAlias( "NumberPropertyAlias" )]
			public int NumberProperty { get; set; }

			[DecodeAlias( "anotherNumberFieldAliasOne", "anotherNumberFieldAliasTwo" )]
			public int AnotherNumberField;

			[DecodeAlias( "AnotherNumberPropertyAliasOne" )]
			[DecodeAlias( "AnotherNumberPropertyAliasTwo" )]
			public int YetAnotherNumberField;
		}


		[Test]
		public void LoadAlias()
		{
			const string json = "{ \"numberFieldAlias\" : 1, \"NumberPropertyAlias\" : 2, \"anotherNumberFieldAliasOne\" : 3, \"anotherNumberFieldAliasTwo\" : 4, \"AnotherNumberPropertyAliasOne\" : 5, \"AnotherNumberPropertyAliasTwo\" : 6 }";
			var aliasData = Json.FromJson<AliasData>( json );

			Assert.That( 1, Is.EqualTo(aliasData.NumberField) );
			Assert.That(2, Is.EqualTo(aliasData.NumberProperty));
			Assert.That( aliasData.AnotherNumberField == 3 || aliasData.AnotherNumberField == 4, Is.True );
			Assert.That( aliasData.YetAnotherNumberField == 5 || aliasData.YetAnotherNumberField == 6, Is.True );
		}
	}
}