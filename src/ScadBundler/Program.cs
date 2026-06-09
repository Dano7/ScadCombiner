namespace ScadBundler.Cli;

/// <summary>The <c>scadbundler</c> CLI entry point.</summary>
internal static class Program
{
    private static int Main(string[] args) =>
        BundleCommand.Run(args, Console.Out, Console.Error);
}
