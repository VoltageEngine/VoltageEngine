using Voltage.Data;
using Voltage.DeferredLighting;
using Voltage.Sprites;

namespace Voltage.Serialization;

public static class ComponentDataSerializationBootstrap
{
	private static bool _inited;

	public static void EnsureInitialized()
	{
		if (_inited)
			return;
		_inited = true;

		ComponentDataSerializationRegistry.Register(1, typeof(EntityData));
		ComponentDataSerializationRegistry.Register(2, typeof(PrefabData));
	}
}
