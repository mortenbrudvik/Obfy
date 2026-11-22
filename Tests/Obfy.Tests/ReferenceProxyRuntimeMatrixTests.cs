using System.Reflection;
using System.Runtime.Loader;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Obfy.Core.Models;
using Obfy.Core.Obfuscators.Assembly;
using Obfy.Core.Pipeline;
using Obfy.Core.Utilities;
using Shouldly;

namespace Obfy.Tests;

/// <summary>
/// Each call shape runs through the reference-proxy pass on its own, so a failure names the shape.
/// Shapes the pass is supposed to rewrite must contain a <c>calli</c>. Generic calls and calls with
/// more than 16 arguments stay direct and only have to keep returning the original value.
/// A call with 5 to 16 arguments is proxied. An out-of-module instance call on a value type is
/// proxied with a by-ref receiver when the type is known, and left direct when it is not.
/// <c>callvirt</c> on a value type, and calls prefixed with <c>readonly.</c> or <c>tail.</c>, stay direct.
/// </summary>
public class ReferenceProxyRuntimeMatrixTests
{
    public static TheoryData<string, string, int> Cases()
    {
        var data = new TheoryData<string, string, int>();
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Adder().Add(20, 22);
            }
            public class Adder
            {
                public int Add(int a, int b) => a + b;
            }
            """, "public-instance", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    IAdder adder = new Adder();
                    return adder.Add(20, 22);
                }
            }
            public interface IAdder { int Add(int a, int b); }
            public class Adder : IAdder
            {
                public int Add(int a, int b) => a + b;
            }
            """, "interface", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    var n = 20;
                    Bump(ref n);
                    return n;
                }
                static void Bump(ref int n) => n += 22;
            }
            """, "byref", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Make().N;
                static Pair Make() => new Pair(42);
            }
            public struct Pair
            {
                public int N;
                public Pair(int n) { N = n; }
            }
            """, "struct-return", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Derived().Value();
            }
            public class Base
            {
                public virtual int Compute() => 40;
            }
            public class Derived : Base
            {
                public override int Compute() => 1;
                public int Value() => base.Compute() + 2;
            }
            """, "base-call", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    IAdder adder = new Adder();
                    return adder.Add(20, 22);
                }
            }
            public interface IAdder { int Add(int a, int b); }
            public class Adder : IAdder
            {
                int IAdder.Add(int a, int b) => a + b;
            }
            """, "explicit-interface", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    Base b = new Derived();
                    return b.Compute();
                }
            }
            public class Base
            {
                public virtual int Compute() => 1;
            }
            public class Derived : Base
            {
                public override int Compute() => 42;
            }
            """, "virtual-override", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box<int>().Pick();
            }
            public class Box<T>
            {
                public int Pick() => From(20);
                public int From(int n) => n + 22;
            }
            """, "generic-instance", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Box<int>.Pick();
            }
            public class Box<T>
            {
                public static int Pick() => From(20);
                public static int From(int n) => n + 22;
            }
            """, "generic-static", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box<int>().Pick();
            }
            public class Box<T>
            {
                public int Pick() => From(20);
                internal int From(int n) => n + 22;
            }
            """, "generic-internal", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box<int>().Pick();
            }
            public class Box<T>
            {
                public int Pick() => Secret(20);
                private int Secret(int n) => n + 22;
            }
            """, "generic-private", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    var n = 20;
                    return Ref(ref n);
                }
                static ref int Ref(ref int n)
                {
                    n += 22;
                    return ref n;
                }
            }
            """, "ref-return", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Adder().Add(20, 22);
            }
            public struct Adder
            {
                public int Add(int a, int b) => a + b;
            }
            """, "struct-instance", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    IAdder adder = new Adder();
                    return adder.Add(20, 22);
                }
            }
            public interface IAdder { int Add(int a, int b); }
            public struct Adder : IAdder
            {
                public int Add(int a, int b) => a + b;
            }
            """, "struct-interface", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Factory().Make().A;
            }
            public class Factory
            {
                public Big Make() => new Big(42);
            }
            public struct Big
            {
                public int A, B, C, D;
                public Big(int n) { A = n; B = 1; C = 2; D = 3; }
            }
            """, "large-struct-return", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Adder(20).Bump(22).N;
            }
            public struct Adder
            {
                public int N;
                public Adder(int n) { N = n; }
                public Adder Bump(int n) { N += n; return this; }
            }
            """, "struct-method-returns-struct", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box().N;
            }
            public class Box
            {
                public int N;
                public Box() { N = Compute(); }
                public int Compute() => 42;
            }
            """, "ctor-calls-method", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box().N;
            }
            public class Box
            {
                public int N => 42;
            }
            """, "property", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Sum(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 26);
                static int Sum(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p, int q)
                    => a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p + q;
            }
            """, "skipped-many-args", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Add(8, 8, 8, 8, 10);
                static int Add(int a, int b, int c, int d, int e) => a + b + c + d + e;
            }
            """, "five-args", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Adder().Add(1, 2, 3, 36);
            }
            public class Adder
            {
                public int Add(int a, int b, int c, int d) => a + b + c + d;
            }
            """, "instance-four-args", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Adder().Add(1, 2, 3, 36);
            }
            public struct Adder
            {
                public int Add(int a, int b, int c, int d) => a + b + c + d;
            }
            """, "struct-four-args", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    var n = 10;
                    Add(ref n, 8, 8, 8, 8);
                    return n;
                }
                static void Add(ref int n, int a, int b, int c, int d) => n += a + b + c + d;
            }
            """, "five-args-byref", 42);
        data.Add("""
            using System.Collections.Generic;
            public static class Lib
            {
                public static int Get()
                {
                    var n = 0;
                    foreach (var i in Seq()) n += i;
                    return n;
                }
                static IEnumerable<int> Seq()
                {
                    yield return Abs(-20);
                    yield return 22;
                }
                static int Abs(int n) => n < 0 ? -n : n;
            }
            """, "iterator", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box(20).Add(22);
            }
            public ref struct Box
            {
                public int N;
                public Box(int n) { N = n; }
                public int Add(int n) => N + n;
            }
            """, "ref-struct", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Box(20).Get();
            }
            public ref struct Box
            {
                int N;
                public Box(int n) { N = n; }
                public int Get() => Add(22);
                int Add(int n) => N + n;
            }
            """, "ref-struct-private", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Use(new Box(42));
                static int Use(Box box) => box.N;
            }
            public ref struct Box
            {
                public int N;
                public Box(int n) { N = n; }
            }
            """, "ref-struct-param", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    var n = 41;
                    return Bump(in n);
                }
                static int Bump(in int n) => n + 1;
            }
            """, "in-param", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    Pick(out var n);
                    return n;
                }
                static void Pick(out int n) => n = 42;
            }
            """, "out-param", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Sum(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 27);
                static int Sum(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p)
                    => a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p;
            }
            """, "sixteen-args", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Adder().Sum(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 27);
            }
            public class Adder
            {
                public int Sum(int a, int b, int c, int d, int e, int f, int g, int h, int i, int j, int k, int l, int m, int n, int o, int p)
                    => a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p;
            }
            """, "instance-sixteen", 42);
        data.Add("""
            using System;
            public static class Lib
            {
                public static int Get()
                {
                    var n = 0;
                    try
                    {
                        n += InTry(20);
                        throw new InvalidOperationException("x");
                    }
                    catch (InvalidOperationException) when (Mark())
                    {
                        n += InCatch(2);
                    }
                    finally
                    {
                        n += InFinally(20);
                    }
                    return n;
                }
                static bool Mark() => true;
                static int InTry(int n) => n;
                static int InCatch(int n) => n;
                static int InFinally(int n) => n;
            }
            """, "exception-handlers", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get()
                {
                    int Inc(int n) => n + 1;
                    return Inc(41);
                }
            }
            """, "local-function", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => new Outer().Run();
            }
            public class Outer
            {
                public int Run() => new Hidden().Value();
                private class Hidden
                {
                    public int Value() => 42;
                }
            }
            """, "private-nested-type", 42);
        data.Add("""
            public static class Lib
            {
                public static unsafe int Get()
                {
                    int n = 42;
                    return Read(&n);
                }
                static unsafe int Read(int* p) => *p;
            }
            """, "pointer", 42);
        data.Add("""
            public static class Lib
            {
                public static unsafe int Get()
                {
                    delegate*<int, int> f = &Inc;
                    return Apply(f, 41);
                }
                static int Inc(int n) => n + 1;
                static unsafe int Apply(delegate*<int, int> f, int n) => f(n);
            }
            """, "fnptr", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Sum(__arglist(20, 22));
                static int Sum(__arglist)
                {
                    var it = new System.ArgIterator(__arglist);
                    int a = __refvalue(it.GetNextArg(), int);
                    int b = __refvalue(it.GetNextArg(), int);
                    return a + b;
                }
            }
            """, "skipped-vararg", 42);
        data.Add("""
            public static class Lib
            {
                public static int Get() => Use(new Adder());
                static int Use<T>(T value) where T : IAdd => value.Add(20, 22);
            }
            public interface IAdd { int Add(int a, int b); }
            public struct Adder : IAdd { public int Add(int a, int b) => a + b; }
            """, "skipped-constrained", 42);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ProxiedCall_ReturnsTheOriginalValue(string source, string name, int expected)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-proxy-matrix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = Compile(source, dir, "ProxyMatrix");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var context = PipelineContext.ForAssembly(module, new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { ReferenceProxy = true }
            });
            var result = await new ReferenceProxyObfuscator(NullLogger<ReferenceProxyObfuscator>.Instance)
                .ObfuscateAsync(context);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            var calli = module.GetTypes().SelectMany(t => t.Methods).Count(m =>
                m.HasBody && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Calli));
            if (!name.StartsWith("generic", StringComparison.Ordinal) &&
                !name.StartsWith("skipped-", StringComparison.Ordinal))
                calli.ShouldBeGreaterThan(0, name);

            var output = Path.Combine(dir, "ProxyMatrix.obf.dll");
            module.Write(output);
            Invoke(output).ShouldBe(expected, name);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    public static TheoryData<string, string> ExternalCases()
    {
        var data = new TheoryData<string, string>();
        data.Add("timespan", """
            public static class Lib
            {
                public static int Get() => (int)System.TimeSpan.FromTicks(42).Ticks;
            }
            """);
        data.Add("async-yield", """
            using System.Threading.Tasks;
            public static class Lib
            {
                public static int Get() => Run().GetAwaiter().GetResult();
                static async Task<int> Run()
                {
                    var text = await Worker.LoadAsync("abc");
                    var count = await Worker.CountAsync(new[] { 1, 2, 3 });
                    return text == "ABC-3" && count == 6 ? 42 : 0;
                }
            }
            public static class Worker
            {
                public static async Task<string> LoadAsync(string s)
                {
                    await Task.Delay(1);
                    return string.Concat(s.ToUpperInvariant(), "-", s.Length.ToString());
                }
                public static async Task<int> CountAsync(int[] items)
                {
                    var sum = 0;
                    foreach (var i in items)
                    {
                        await Task.Yield();
                        sum += System.Math.Abs(i);
                    }
                    return sum;
                }
            }
            """);
        data.Add("try-parse", """
            public static class Lib
            {
                public static int Get() => int.TryParse("42", out var n) && n == 42 ? 42 : 0;
            }
            """);
        return data;
    }

    [Theory]
    [MemberData(nameof(ExternalCases))]
    public async Task ExternalProxy_ReturnsTheOriginalValue(string name, string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-proxy-ext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = Compile(source, dir, "ProxyExternal");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var result = await ProxyExternal(module);
            result.Success.ShouldBeTrue(result.ErrorMessage);

            if (name == "timespan")
            {
                Proxies(module, "FromTicks").ShouldBeTrue(name);
                Proxies(module, "get_Ticks").ShouldBeTrue(name);
                ProxyTakesReceiverByRef(module, "get_Ticks").ShouldBeTrue(name);
            }

            if (name == "async-yield")
            {
                Proxies(module, "Abs").ShouldBeTrue(name);
                CallStaysDirect(module, "YieldAwaiter", "get_IsCompleted").ShouldBeTrue(name);
                CallStaysDirect(module, "YieldAwaiter", "GetResult").ShouldBeTrue(name);
            }

            if (name == "try-parse")
                CallStaysDirect(module, "Int32", "TryParse").ShouldBeTrue(name);

            var output = Path.Combine(dir, "ProxyExternal.obf.dll");
            module.Write(output);
            Invoke(output).ShouldBe(42, name);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExternalStructAndClass_AreProxied(bool valueType)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-proxy-dep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var depSource = valueType
                ? "public struct Pair { public int N; public int Get() => N + 1; }"
                : "public class Pair { public int N; public int Get() => N + 1; }";
            var dep = Compile(depSource, dir, "PairLib");
            var app = Compile("""
                public static class Lib
                {
                    public static int Get()
                    {
                        var pair = new Pair { N = 41 };
                        return pair.Get();
                    }
                }
                """, dir, "ProxyExternal", dep);

            using var module = ModuleDefMD.Load(File.ReadAllBytes(app));
            var result = await ProxyExternal(module);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            Proxies(module, "Get").ShouldBeTrue();
            if (valueType)
                ProxyTakesReceiverByRef(module, "Get").ShouldBeTrue();

            var output = Path.Combine(dir, "ProxyExternal.obf.dll");
            module.Write(output);
            Invoke(output).ShouldBe(42);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public async Task AsyncExternalProxy_SurvivesRenaming()
    {
        const string source = """
            using System.Threading.Tasks;
            public static class Lib
            {
                public static int Get() => Run().GetAwaiter().GetResult();
                static async Task<int> Run()
                {
                    var text = await Worker.LoadAsync("abc");
                    var count = await Worker.CountAsync(new[] { 1, 2, 3 });
                    return text == "ABC-3" && count == 6 ? 42 : 0;
                }
            }
            public static class Worker
            {
                public static async Task<string> LoadAsync(string s)
                {
                    await Task.Delay(1);
                    return string.Concat(s.ToUpperInvariant(), "-", s.Length.ToString());
                }
                public static async Task<int> CountAsync(int[] items)
                {
                    var sum = 0;
                    foreach (var i in items)
                    {
                        await Task.Yield();
                        sum += System.Math.Abs(i);
                    }
                    return sum;
                }
            }
            """;
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-proxy-rename-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = Compile(source, dir, "ProxyRename");
            using var module = ModuleDefMD.Load(File.ReadAllBytes(input));
            var settings = new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { ReferenceProxy = true, ProxyExternalCalls = true },
                SymbolRenaming = { Enabled = true, PreservePublicApi = true }
            };
            var context = PipelineContext.ForAssembly(module, settings);
            (await new ReferenceProxyObfuscator(NullLogger<ReferenceProxyObfuscator>.Instance)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();
            (await new SymbolRenamingObfuscator(new NameGenerator(), NullLogger<SymbolRenamingObfuscator>.Instance)
                .ObfuscateAsync(context)).Success.ShouldBeTrue();

            var output = Path.Combine(dir, "ProxyRename.obf.dll");
            module.Write(output);
            Invoke(output).ShouldBe(42);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    /// <summary>
    /// <c>callvirt</c> on a value type has no <c>constrained.</c> prefix and still passes the address.
    /// Rewriting it as <c>ldvirtftn</c> is not a legal trampoline. <c>readonly.</c> and <c>tail.</c>
    /// belong to the call they prefix, so those calls stay direct too.
    /// </summary>
    [Fact]
    public async Task ValueTypeCallvirtAndPrefixes_StayDirect()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"obfy-proxy-prefix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var input = Compile("""
                public static class Lib
                {
                    public static int Get() => FromCallvirt() + FromReadonly() + FromTail() == 126 ? 42 : 0;
                    public static int FromCallvirt() => 0;
                    public static int FromReadonly() => 0;
                    public static int FromTail() => Id(42);
                    public static int Id(int n) => n;
                }
                public struct Box
                {
                    public int N;
                    public Box(int n) { N = n; }
                    public int Value() => N + 1;
                }
                """, dir, "ProxyPrefix");
            var patched = Path.Combine(dir, "ProxyPrefix.patched.dll");
            PatchPrefixes(input, patched);
            Invoke(patched).ShouldBe(42);

            using var module = ModuleDefMD.Load(File.ReadAllBytes(patched));
            var context = PipelineContext.ForAssembly(module, new ObfySettings
            {
                Level = ObfuscationLevel.Custom,
                Protection = { ReferenceProxy = true }
            });
            var result = await new ReferenceProxyObfuscator(NullLogger<ReferenceProxyObfuscator>.Instance)
                .ObfuscateAsync(context);
            result.Success.ShouldBeTrue(result.ErrorMessage);
            CallerCalls(module, "FromCallvirt", OpCodes.Callvirt, "Value").ShouldBeTrue();
            HasPrefixBefore(module, "FromReadonly", OpCodes.Readonly, "Value").ShouldBeTrue();
            HasPrefixBefore(module, "FromTail", OpCodes.Tailcall, "Id").ShouldBeTrue();

            var output = Path.Combine(dir, "ProxyPrefix.obf.dll");
            module.Write(output);
            Invoke(output).ShouldBe(42);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    private static void PatchPrefixes(string sourcePath, string patchedPath)
    {
        using var module = ModuleDefMD.Load(sourcePath);
        var lib = module.Types.First(t => t.Name == "Lib");
        var box = module.Types.First(t => t.Name == "Box");
        var ctor = box.Methods.First(m => m.Name == ".ctor");
        var value = box.Methods.First(m => m.Name == "Value");
        var id = lib.Methods.First(m => m.Name == "Id");

        var callvirt = lib.Methods.First(m => m.Name == "FromCallvirt");
        var local = new Local(new ValueTypeSig(box));
        callvirt.Body = new CilBody { InitLocals = true, MaxStack = 8, Variables = { local } };
        callvirt.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, local));
        callvirt.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 41));
        callvirt.Body.Instructions.Add(Instruction.Create(OpCodes.Call, ctor));
        callvirt.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, local));
        callvirt.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, value));
        callvirt.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var read = lib.Methods.First(m => m.Name == "FromReadonly");
        var readLocal = new Local(new ValueTypeSig(box));
        read.Body = new CilBody { InitLocals = true, MaxStack = 8, Variables = { readLocal } };
        read.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, readLocal));
        read.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 41));
        read.Body.Instructions.Add(Instruction.Create(OpCodes.Call, ctor));
        read.Body.Instructions.Add(Instruction.Create(OpCodes.Ldloca, readLocal));
        read.Body.Instructions.Add(Instruction.Create(OpCodes.Readonly));
        read.Body.Instructions.Add(Instruction.Create(OpCodes.Call, value));
        read.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

        var tail = lib.Methods.First(m => m.Name == "FromTail");
        tail.Body = new CilBody
        {
            MaxStack = 8,
            Instructions =
            {
                Instruction.Create(OpCodes.Ldc_I4, 42),
                Instruction.Create(OpCodes.Tailcall),
                Instruction.Create(OpCodes.Call, id),
                Instruction.Create(OpCodes.Ret)
            }
        };
        module.Write(patchedPath);
    }

    private static bool CallerCalls(ModuleDef module, string caller, OpCode opcode, string targetName)
    {
        foreach (var method in module.GetTypes().SelectMany(t => t.Methods))
        {
            if (method.Name != caller || !method.HasBody)
                continue;
            if (method.Body.Instructions.Any(i =>
                    i.OpCode == opcode && i.Operand is IMethod target && target.Name == targetName))
                return true;
        }

        return false;
    }

    private static bool HasPrefixBefore(ModuleDef module, string caller, OpCode prefix, string targetName)
    {
        foreach (var method in module.GetTypes().SelectMany(t => t.Methods))
        {
            if (method.Name != caller || !method.HasBody)
                continue;
            var instructions = method.Body.Instructions;
            for (var i = 1; i < instructions.Count; i++)
            {
                if (instructions[i - 1].OpCode != prefix)
                    continue;
                if (instructions[i].Operand is IMethod target && target.Name == targetName)
                    return true;
            }
        }

        return false;
    }

    private static Task<ObfuscationResult> ProxyExternal(ModuleDef module)
    {
        var context = PipelineContext.ForAssembly(module, new ObfySettings
        {
            Level = ObfuscationLevel.Custom,
            Protection = { ReferenceProxy = true, ProxyExternalCalls = true }
        });
        return new ReferenceProxyObfuscator(NullLogger<ReferenceProxyObfuscator>.Instance)
            .ObfuscateAsync(context);
    }

    private static bool Proxies(ModuleDef module, string methodName) =>
        module.GetTypes().SelectMany(t => t.Methods).Any(m =>
            m.HasBody && m.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Ldftn || i.OpCode == OpCodes.Ldvirtftn) &&
                i.Operand is IMethod target && target.Name == methodName));

    private static bool ProxyTakesReceiverByRef(ModuleDef module, string methodName)
    {
        foreach (var method in module.GetTypes().SelectMany(t => t.Methods))
        {
            if (!method.HasBody)
                continue;
            var targets = method.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Ldftn || i.OpCode == OpCodes.Ldvirtftn) &&
                i.Operand is IMethod target && target.Name == methodName);
            if (!targets)
                continue;
            var first = method.MethodSig?.Params.FirstOrDefault();
            if (first?.RemovePinnedAndModifiers().ElementType == ElementType.ByRef)
                return true;
        }

        return false;
    }

    private static bool CallStaysDirect(ModuleDef module, string typeName, string methodName) =>
        module.GetTypes().SelectMany(t => t.Methods).Any(m =>
            m.DeclaringType?.Name != "<RefProxy>" &&
            m.HasBody && m.Body.Instructions.Any(i =>
                (i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt) &&
                i.Operand is IMethod target &&
                target.Name == methodName &&
                target.DeclaringType?.Name == typeName));

    private static string Compile(string source, string dir, string assemblyName, params string[] extraRefs)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Concat(extraRefs.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: source.Contains("unsafe", StringComparison.Ordinal)));
        var path = Path.Combine(dir, assemblyName + ".dll");
        var emit = compilation.Emit(path);
        emit.Success.ShouldBeTrue(string.Join("\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    private static int Invoke(string assemblyPath)
    {
        var alc = new AssemblyLoadContext($"proxy-{Guid.NewGuid():N}", isCollectible: true);
        alc.Resolving += (_, name) =>
        {
            var candidate = Path.Combine(Path.GetDirectoryName(assemblyPath)!, name.Name + ".dll");
            return File.Exists(candidate) ? alc.LoadFromAssemblyPath(candidate) : null;
        };
        try
        {
            var type = alc.LoadFromAssemblyPath(assemblyPath).GetType("Lib");
            var method = type!.GetMethod("Get", BindingFlags.Public | BindingFlags.Static);
            return (int)method!.Invoke(null, null)!;
        }
        finally
        {
            alc.Unload();
        }
    }
}
