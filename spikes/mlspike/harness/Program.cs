using HappyPhoton.LibRaw.Interop;
using MlSpike.Harness;

try
{
    NativeLibraryResolver.ConfigureOpenMpThreadLimit();
    var context = new RunContext(args);
    try
    {
        var result = context.Execute();
        context.Json("result.json", new { context.Identity, context.Mode, Status = "measured", Result = result });
        return 0;
    }
    catch (Exception error)
    {
        context.Json("result.json", new { context.Identity, context.Mode, Status = "error", Error = error.ToString() });
        throw;
    }
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}
