namespace PlayServ.Schema.Tool;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);
            return new ToolApplication(options).Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"PlayServ Schema Tool failed: {exception.GetBaseException().Message}");
            return 2;
        }
    }
}
