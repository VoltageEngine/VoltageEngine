using System;
using Voltage.Data;

namespace Voltage.Serialization;

/// <summary>
/// Minimal engine-level bootstrap. Registers legacy type IDs and serves as
/// the single entry point that Scene.Serialization and DataManager call.
///
/// Component factory registrations (ComponentAotFactory) are handled entirely
/// by source-generated [ModuleInitializer] methods — no manual registration
/// is needed here. Serialization uses Voltage.Persistence.Json exclusively.
/// </summary>
public static class ComponentDataSerializationBootstrap
{
	private static bool _inited;

	public static void EnsureInitialized()
	{
		if (_inited)
			return;
		_inited = true;

		// Legacy numeric type-id registry (used by editor for Voltage.Persistence.Json)
		ComponentDataSerializationRegistry.Register(1, typeof(EntityData));
		ComponentDataSerializationRegistry.Register(2, typeof(PrefabData));

		// All ComponentAotFactory.Register calls are emitted by
		// Voltage.SourceGenerators via [ModuleInitializer] and run
		// automatically before this point. No manual entries needed.
	}
}