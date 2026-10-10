using System.Text.Json;

namespace Voltage.Effects.Tests;

public static class Program
{
	public static int Main(string[] args)
	{
		if (args.Contains("--gpu-check"))
		{
			Environment.SetEnvironmentVariable("VOLTAGE_TEST_GPU", "1");
			var effects = new EffectsTests();
			effects.SetUp();
			try
			{
				effects.Native_gpu_accepts_all_bundled_effects();
                new AnimatedNormalMapTests().Animated_normals_track_atlas_frames_flips_rotation_and_unmapped_clips();
                new RoomLightTests().Room_fill_renders_and_tracks_driver_edits_without_leaking_outside();
				new PixelArtTests().Asset_backed_animators_share_textures_preserve_origins_and_release_on_scene_end();
			}
			finally { effects.TearDown(); }
			System.Console.WriteLine("Native GPU effects, animated normals, room colors and shared animator lifetime checks passed.");
			return 0;
		}
		if (args.Contains("flood"))
		{
			for (int i = 0; i < 256; i++) { System.Console.WriteLine(new string('a', 4096)); System.Console.Error.WriteLine(new string('b', 4096)); }
			return 0;
		}
		if (args.Contains("hang")) { Thread.Sleep(30000); return 0; }
		System.Console.WriteLine(JsonSerializer.Serialize(args));
		return 0;
	}
}
