namespace UnityEngine;

public class Object
{
    public string name = "";
}

public class GameObject : Object
{
}

public class MonoBehaviour
{
    public GameObject gameObject { get; set; } = new();
}
