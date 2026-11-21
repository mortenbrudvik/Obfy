using dnlib.DotNet;

namespace Obfy.Tests;

/// <summary>
/// Replaces the generic parameters of a signature with type arguments: what the CLR does when it compares the
/// signature of a member of a base type, <c>T Get()</c> of <c>Box&lt;T&gt;</c>, with a reference written for the
/// derived type that instantiates it, <c>int32 ()</c> for <c>IntBox : Box&lt;int&gt;</c>. (dnlib's own helper for this is
/// not public.) Method generic parameters (<c>!!0</c>) are left alone.
/// </summary>
internal static class TypeSigSubstitution
{
    public static TypeSig? Substitute(TypeSig? sig, IList<TypeSig> typeArguments)
    {
        switch (sig)
        {
            case null:
                return null;
            case GenericVar variable:
                return variable.Number < typeArguments.Count ? typeArguments[(int)variable.Number] : sig;
            case GenericInstSig generic:
                return new GenericInstSig(
                    generic.GenericType,
                    generic.GenericArguments.Select(argument => Substitute(argument, typeArguments)!).ToArray());
            case SZArraySig array:
                return new SZArraySig(Substitute(array.Next, typeArguments));
            case ArraySig array:
                return new ArraySig(Substitute(array.Next, typeArguments), array.Rank, array.Sizes, array.LowerBounds);
            case PtrSig pointer:
                return new PtrSig(Substitute(pointer.Next, typeArguments));
            case ByRefSig byRef:
                return new ByRefSig(Substitute(byRef.Next, typeArguments));
            case PinnedSig pinned:
                return new PinnedSig(Substitute(pinned.Next, typeArguments));
            case CModReqdSig required:
                return new CModReqdSig(required.Modifier, Substitute(required.Next, typeArguments));
            case CModOptSig optional:
                return new CModOptSig(optional.Modifier, Substitute(optional.Next, typeArguments));
            default:
                return sig;
        }
    }

    public static MethodSig Substitute(MethodSig signature, IList<TypeSig> typeArguments) =>
        new(
            signature.CallingConvention,
            signature.GenParamCount,
            Substitute(signature.RetType, typeArguments),
            signature.Params.Select(parameter => Substitute(parameter, typeArguments)!).ToList());

    public static FieldSig Substitute(FieldSig signature, IList<TypeSig> typeArguments) =>
        new(Substitute(signature.Type, typeArguments));
}
