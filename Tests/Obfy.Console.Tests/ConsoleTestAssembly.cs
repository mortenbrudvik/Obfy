using System.Reflection;
using System.Runtime.Loader;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using TypeAttributes = dnlib.DotNet.TypeAttributes;
using MethodAttributes = dnlib.DotNet.MethodAttributes;
using MethodImplAttributes = dnlib.DotNet.MethodImplAttributes;

namespace Obfy.Console.Tests;

internal static class ConsoleTestAssembly
{
    public static string Create(string directory, string name, string typeName = "TestClass")
    {
        var path = Path.Combine(directory, name);

        var module = new ModuleDefUser(name, Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            module.Kind = ModuleKind.Dll;
        var assembly = new AssemblyDefUser(Path.GetFileNameWithoutExtension(name), new Version(1, 0, 0, 0));
        assembly.Modules.Add(module);

        var typeDef = new TypeDefUser("TestNamespace", typeName, module.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        module.Types.Add(typeDef);

        var method = new MethodDefUser(
            "TestMethod",
            MethodSig.CreateStatic(module.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);

        var body = new CilBody();
        body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "Hello, World!"));
        body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        method.Body = body;
        typeDef.Methods.Add(method);

        module.Write(path);
        return path;
    }

    public static (string LibPath, string AppPath) CreateClosedSetPair(string directory)
    {
        Directory.CreateDirectory(directory);
        var libPath = Path.Combine(directory, "Lib.dll");
        var appPath = Path.Combine(directory, "App.exe");

        var libModule = new ModuleDefUser("Lib.dll", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40())
        {
            Kind = ModuleKind.Dll
        };
        var libAssembly = new AssemblyDefUser("Lib", new Version(1, 0, 0, 0));
        libAssembly.Modules.Add(libModule);

        var greeter = new TypeDefUser("Lib.Api", "Greeter", libModule.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Class
        };
        libModule.Types.Add(greeter);

        var hello = new MethodDefUser(
            "Hello",
            MethodSig.CreateStatic(libModule.CorLibTypes.String),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        hello.Body = new CilBody();
        hello.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "hi"));
        hello.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        greeter.Methods.Add(hello);
        libModule.Write(libPath);

        var appModule = new ModuleDefUser("App.exe", Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40())
        {
            Kind = ModuleKind.Console
        };
        var appAssembly = new AssemblyDefUser("App", new Version(1, 0, 0, 0));
        appAssembly.Modules.Add(appModule);

        var libRef = new AssemblyRefUser(libAssembly);
        var greeterRef = new TypeRefUser(appModule, "Lib.Api", "Greeter", libRef);
        var helloRef = new MemberRefUser(
            appModule,
            "Hello",
            MethodSig.CreateStatic(appModule.CorLibTypes.String),
            greeterRef);

        var program = new TypeDefUser("", "Program", appModule.CorLibTypes.Object.TypeDefOrRef)
        {
            Attributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class
        };
        appModule.Types.Add(program);

        var run = new MethodDefUser(
            "Run",
            MethodSig.CreateStatic(appModule.CorLibTypes.String),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        run.Body = new CilBody();
        run.Body.Instructions.Add(Instruction.Create(OpCodes.Call, helloRef));
        run.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        program.Methods.Add(run);

        var main = new MethodDefUser(
            "Main",
            MethodSig.CreateStatic(appModule.CorLibTypes.Void),
            MethodImplAttributes.IL,
            MethodAttributes.Public | MethodAttributes.Static);
        main.Body = new CilBody();
        main.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        program.Methods.Add(main);
        appModule.EntryPoint = main;
        appModule.Write(appPath);

        return (libPath, appPath);
    }

    public static string InvokeProgramRun(string appPath, string libPath)
    {
        var alc = new AssemblyLoadContext($"cli-closed-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            alc.LoadFromAssemblyPath(libPath);
            var asm = alc.LoadFromAssemblyPath(appPath);
            var program = asm.GetType("Program")
                ?? asm.GetTypes().Single(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(IsRunLike));
            var run = program.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                ?? program.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(IsRunLike);
            return (string)run.Invoke(null, null)!;
        }
        finally
        {
            alc.Unload();
        }
    }

    private static bool IsRunLike(MethodInfo method) =>
        method.IsStatic
        && method.ReturnType == typeof(string)
        && method.GetParameters().Length == 0
        && method.Name != "Main";
}
