namespace Voltage.Editor;

public class Program
{
    public static string[] CommandLineArgs { get; private set; }
    
    public static void Main(string[] args)
    {
        CommandLineArgs = args;

        using var game = new Editor();
        game.Run();
    }
}