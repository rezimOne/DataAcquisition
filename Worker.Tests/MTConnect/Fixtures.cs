namespace DataAcquisition.Worker.Tests.MTConnect;

internal static class Fixtures
{
    /// <summary>Agent instance of the XML captured from the simulator (--seed 42).</summary>
    public const long InstanceId = 1793792583;

    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
