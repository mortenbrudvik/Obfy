namespace Game;

public static class GameEntry
{
    public static string Ping() => Player.Secret();
}

internal sealed class Player : UnityEngine.MonoBehaviour
{
    internal static string Secret() => "unity-secret";
}
