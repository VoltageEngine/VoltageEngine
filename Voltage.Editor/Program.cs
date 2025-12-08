namespace Voltage.Editor;

public class Program
{
    public static void Main()
    {
#if DEBUG
        //UnitTests.StartupTests.PublishComponentsMissingParameterlessCtor();
#endif
        using var game = new Editor();
            game.Run();
    }
}