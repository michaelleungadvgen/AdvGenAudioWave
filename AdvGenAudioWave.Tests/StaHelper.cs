namespace AdvGenAudioWave.Tests;

internal static class StaHelper
{
    public static T Run<T>(Func<T> func)
    {
        T result = default!;
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { caught = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (caught is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();
        return result;
    }

    public static void Run(Action action) => Run<byte>(() => { action(); return 0; });
}
