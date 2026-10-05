using System.Text.Json;

namespace Voltage.Effects.Tests;

public static class Program
{
	public static int Main(string[] args)
	{
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
