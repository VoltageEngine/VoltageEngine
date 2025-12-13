using NUnit.Framework;
using System;

namespace Voltage.Persistence.JsonTests
{
	[TestFixture]
	public class ReadOnlyPropertySerializationTests
	{
		class TestClass
		{
			// Regular field - should always serialize
			[JsonInclude]
			public int RegularField = 42;

			// Read-write property - should always serialize
			[JsonInclude]
			public int ReadWriteProperty { get; set; } = 100;

			// Readonly property (no setter) - should be SKIPPED by default
			[JsonInclude]
			public int ReadOnlyProperty { get; } = 200;

			// Property with private setter - should be SKIPPED by default
			[JsonInclude]
			public int PropertyWithPrivateSetter { get; private set; } = 300;

			// Property with protected setter - should be SKIPPED by default
			[JsonInclude]
			public int PropertyWithProtectedSetter { get; protected set; } = 400;

			// Property with internal setter - should be SKIPPED by default
			[JsonInclude]
			public int PropertyWithInternalSetter { get; internal set; } = 500;

			public TestClass()
			{
				ReadWriteProperty = 100;
				PropertyWithPrivateSetter = 300;
				PropertyWithProtectedSetter = 400;
				PropertyWithInternalSetter = 500;
			}
		}

		[Test]
		public void DefaultBehavior_SkipsReadOnlyProperties()
		{
			var testObj = new TestClass();
			
			// Default settings should skip readonly properties
			var json = Json.ToJson(testObj);
			
			Console.WriteLine("JSON with default settings (skip readonly):");
			Console.WriteLine(json);
			Console.WriteLine();

			// Should contain field and read-write property
			Assert.That(json, Does.Contain("RegularField"), "RegularField should be serialized");
			Assert.That(json, Does.Contain("ReadWriteProperty"), "ReadWriteProperty should be serialized");
			
			// Should NOT contain readonly properties
			Assert.That(json, Does.Not.Contain("ReadOnlyProperty"), "ReadOnlyProperty should be skipped");
			Assert.That(json, Does.Not.Contain("PropertyWithPrivateSetter"), "PropertyWithPrivateSetter should be skipped");
			Assert.That(json, Does.Not.Contain("PropertyWithProtectedSetter"), "PropertyWithProtectedSetter should be skipped");
			Assert.That(json, Does.Not.Contain("PropertyWithInternalSetter"), "PropertyWithInternalSetter should be skipped");
		}

		[Test]
		public void WithSkipDisabled_IncludesAllProperties()
		{
			var testObj = new TestClass();
			
			// Disable SkipReadOnlyProperties
			var settings = new JsonSettings
			{
				SkipReadOnlyProperties = false
			};
			
			var json = Json.ToJson(testObj, settings);
			
			Console.WriteLine("JSON with SkipReadOnlyProperties = false:");
			Console.WriteLine(json);
			Console.WriteLine();

			// Should now contain ALL properties with [JsonInclude]
			Assert.That(json, Does.Contain("RegularField"), "RegularField should be serialized");
			Assert.That(json, Does.Contain("ReadWriteProperty"), "ReadWriteProperty should be serialized");
			Assert.That(json, Does.Contain("ReadOnlyProperty"), "ReadOnlyProperty should be serialized");
			Assert.That(json, Does.Contain("PropertyWithPrivateSetter"), "PropertyWithPrivateSetter should be serialized");
			Assert.That(json, Does.Contain("PropertyWithProtectedSetter"), "PropertyWithProtectedSetter should be serialized");
			Assert.That(json, Does.Contain("PropertyWithInternalSetter"), "PropertyWithInternalSetter should be serialized");
		}

		[Test]
		public void VerifyValues_WhenSerializedWithSkipDisabled()
		{
			var testObj = new TestClass();
			
			var settings = new JsonSettings
			{
				SkipReadOnlyProperties = false,
				PrettyPrint = true
			};
			
			var json = Json.ToJson(testObj, settings);
			
			Console.WriteLine("Pretty-printed JSON:");
			Console.WriteLine(json);

			// Verify actual values are correct
			Assert.That(json, Does.Contain("\"RegularField\": 42"));
			Assert.That(json, Does.Contain("\"ReadWriteProperty\": 100"));
			Assert.That(json, Does.Contain("\"ReadOnlyProperty\": 200"));
			Assert.That(json, Does.Contain("\"PropertyWithPrivateSetter\": 300"));
			Assert.That(json, Does.Contain("\"PropertyWithProtectedSetter\": 400"));
			Assert.That(json, Does.Contain("\"PropertyWithInternalSetter\": 500"));
		}

		class ComponentWithReadOnlyBounds
		{
			[JsonInclude]
			public string Name = "TestComponent";

			[JsonInclude]
			public int X { get; set; } = 10;

			[JsonInclude]
			public int Y { get; set; } = 20;

			// This simulates the Bounds property from Voltage components
			[JsonInclude]
			public BoundsStruct Bounds { get; } = new BoundsStruct(10, 20, 100, 50);
		}

		struct BoundsStruct
		{
			public int X { get; set; }
			public int Y { get; set; }
			public int Width { get; set; }
			public int Height { get; set; }

			public BoundsStruct(int x, int y, int width, int height)
			{
				X = x;
				Y = y;
				Width = width;
				Height = height;
			}
		}

		[Test]
		public void RealWorldScenario_ComponentWithBounds()
		{
			var component = new ComponentWithReadOnlyBounds();
			
			// Default: Bounds should be skipped
			var json = Json.ToJson(component);
			
			Console.WriteLine("Component JSON (default - skips Bounds):");
			Console.WriteLine(json);
			Console.WriteLine();

			Assert.That(json, Does.Contain("Name"));
			Assert.That(json, Does.Contain("X"));
			Assert.That(json, Does.Contain("Y"));
			Assert.That(json, Does.Not.Contain("Bounds"), "Bounds should be skipped (readonly property)");
		}

		[Test]
		public void RoundTrip_OnlySerializablePropertiesCanBeDeserialized()
		{
			var original = new TestClass();
			
			// Serialize with default settings (skips readonly)
			var json = Json.ToJson(original);
			
			// Deserialize
			var deserialized = Json.FromJson<TestClass>(json);
			
			// Only read-write properties should have been round-tripped
			Assert.That(deserialized.RegularField, Is.EqualTo(42));
			Assert.That(deserialized.ReadWriteProperty, Is.EqualTo(100));
			
			// Readonly properties will have their default/initialized values
			Assert.That(deserialized.ReadOnlyProperty, Is.EqualTo(200), "Should have default value from initialization");
		}

		[Test]
		public void ExplicitTrue_SkipsReadOnlyProperties()
		{
			var testObj = new TestClass();
			
			var settings = new JsonSettings
			{
				SkipReadOnlyProperties = true // Explicitly true
			};
			
			var json = Json.ToJson(testObj, settings);

			Assert.That(json, Does.Not.Contain("ReadOnlyProperty"));
			Assert.That(json, Does.Not.Contain("PropertyWithPrivateSetter"));
		}
	}
}