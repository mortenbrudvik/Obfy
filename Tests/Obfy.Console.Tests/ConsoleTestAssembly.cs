using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace Obfy.Console.Tests;

internal static class ConsoleTestAssembly
{
    public static string Create(string directory, string name, string typeName = "TestClass")
    {
        var path = Path.Combine(directory, name);

        var module = new ModuleDefUser(name, Guid.NewGuid(), AssemblyRefUser.CreateMscorlibReferenceCLR40());
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
}
