namespace BlazorWasmApp;

public static class Probe
{
    public static string Ping() => Secret.Get();
}
