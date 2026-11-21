Console.WriteLine("Hello, World!");
Console.WriteLine($"Sum:{Sum(10)}");

static int Sum(int n)
{
    var total = 0;
    for (var i = 1; i <= n; i++)
        total += i % 3 == 0 ? i * 7 : i;
    return total;
}
