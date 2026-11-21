var b = new Box<int>(42);
Console.WriteLine(b.Item + b.Get());

var name = new Box<string>("generic");
Console.WriteLine(name.Get() + name.Item.Length);

var pair = new Pair<int>(3, 4);
pair.First += 10;
Console.WriteLine(pair.First + pair.Swap().First + Pair<int>.Of(5).Second);

var table = new Table<string>();
table[1] = "indexed";
table.Changed += s => Console.WriteLine("changed:" + s);
table.Set("raised");
Console.WriteLine(table[1] + table.Count);

var sum = Lambdas.Capture(7)() + Lambdas.Apply(3, x => x * 2);
Console.WriteLine(sum);

var nested = new Box<Box<int>>(new Box<int>(9));
Console.WriteLine(nested.Item.Item + nested.Get().Get());

var list = new List<Box<int>> { new(1), new(2) };
Console.WriteLine(list.Sum(x => x.Item) + list.Count);

Console.WriteLine(string.Join(",", Sequence<int>.Repeat(5, 3)));

internal class Box<T>
{
    public T Item;
    public Box(T item) { Item = item; }
    public T Get() => Item;
}

internal struct Pair<T>
{
    public T First;
    public T Second;
    public Pair(T first, T second) { First = first; Second = second; }
    public Pair<T> Swap() => new(Second, First);
    public static Pair<T> Of(T value) => new(value, value);
}

internal class Table<T>
{
    private readonly T[] _items = new T[4];
    public event Action<T>? Changed;
    public int Count => _items.Length;
    public T this[int index] { get => _items[index]; set => _items[index] = value; }
    public void Set(T value) { _items[0] = value; Changed?.Invoke(value); }
}

internal static class Lambdas
{
    public static Func<T> Capture<T>(T value) => () => value;
    public static U Apply<T, U>(T value, Func<T, U> f) => f(value);
}

internal static class Sequence<T>
{
    public static IEnumerable<T> Repeat(T value, int count)
    {
        for (var i = 0; i < count; i++)
            yield return value;
    }
}
