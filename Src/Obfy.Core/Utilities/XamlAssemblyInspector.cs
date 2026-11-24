using System.Buffers.Binary;
using System.Resources;
using System.Text;
using dnlib.DotNet;

namespace Obfy.Core.Utilities;

/// <summary>
/// Extracts CLR full type names referenced by compiled WPF assemblies without loading runtime WPF assemblies:
/// <list type="bullet">
/// <item><description>Types implementing <c>IComponentConnector</c> or <c>IStyleConnector</c>.</description></item>
/// <item><description>Types listed in BAML <c>TypeInfo</c> (<c>0x1D</c>) / <c>TypeSerializerInfo</c> (<c>0x1E</c>) records inside <c>*.g.resources</c>.</description></item>
/// </list>
/// </summary>
public static class XamlAssemblyInspector
{
    private const byte DocumentEndRecord = 0x02;
    private const byte TypeInfoRecord = 0x1D;
    private const byte TypeSerializerInfoRecord = 0x1E;
    private const string StreamResourceType = "ResourceTypeCode.Stream";
    private const sbyte UnknownRecord = -1;
    private const sbyte VariableSizedRecord = -2;

    // Indexed by BamlRecordType (0..56). Non-negative values are fixed payload byte counts.
    private static readonly sbyte[] RecordPayloadSizes =
    [
        UnknownRecord,        // 0x00 Unknown
        6,                    // 0x01 DocumentStart
        0,                    // 0x02 DocumentEnd
        3,                    // 0x03 ElementStart
        0,                    // 0x04 ElementEnd
        VariableSizedRecord,  // 0x05 Property
        VariableSizedRecord,  // 0x06 PropertyCustom
        2,                    // 0x07 PropertyComplexStart
        0,                    // 0x08 PropertyComplexEnd
        2,                    // 0x09 PropertyArrayStart
        0,                    // 0x0A PropertyArrayEnd
        2,                    // 0x0B PropertyIListStart
        0,                    // 0x0C PropertyIListEnd
        2,                    // 0x0D PropertyIDictionaryStart
        0,                    // 0x0E PropertyIDictionaryEnd
        VariableSizedRecord,  // 0x0F LiteralContent
        VariableSizedRecord,  // 0x10 Text
        VariableSizedRecord,  // 0x11 TextWithConverter
        VariableSizedRecord,  // 0x12 RoutedEvent
        UnknownRecord,        // 0x13 ClrEvent
        VariableSizedRecord,  // 0x14 XmlnsProperty
        UnknownRecord,        // 0x15 XmlAttribute
        UnknownRecord,        // 0x16 ProcessingInstruction
        UnknownRecord,        // 0x17 Comment
        UnknownRecord,        // 0x18 DefTag
        VariableSizedRecord,  // 0x19 DefAttribute
        UnknownRecord,        // 0x1A EndAttributes
        VariableSizedRecord,  // 0x1B PIMapping
        VariableSizedRecord,  // 0x1C AssemblyInfo
        VariableSizedRecord,  // 0x1D TypeInfo
        VariableSizedRecord,  // 0x1E TypeSerializerInfo
        VariableSizedRecord,  // 0x1F AttributeInfo
        VariableSizedRecord,  // 0x20 StringInfo
        4,                    // 0x21 PropertyStringReference
        4,                    // 0x22 PropertyTypeReference
        6,                    // 0x23 PropertyWithExtension
        VariableSizedRecord,  // 0x24 PropertyWithConverter
        4,                    // 0x25 DeferableContentStart
        VariableSizedRecord,  // 0x26 DefAttributeKeyString
        9,                    // 0x27 DefAttributeKeyType
        9,                    // 0x28 KeyElementStart
        0,                    // 0x29 KeyElementEnd
        0,                    // 0x2A ConstructorParametersStart
        0,                    // 0x2B ConstructorParametersEnd
        2,                    // 0x2C ConstructorParameterType
        4,                    // 0x2D ConnectionId
        2,                    // 0x2E ContentProperty
        UnknownRecord,        // 0x2F NamedElementStart
        3,                    // 0x30 StaticResourceStart
        0,                    // 0x31 StaticResourceEnd
        2,                    // 0x32 StaticResourceId
        VariableSizedRecord,  // 0x33 TextWithId
        VariableSizedRecord,  // 0x34 PresentationOptionsAttribute
        8,                    // 0x35 LineNumberAndPosition
        4,                    // 0x36 LinePosition
        3,                    // 0x37 OptimizedStaticResource
        4                     // 0x38 PropertyWithStaticResourceId
    ];

    /// <summary>
    /// Collects full type names (<c>Namespace.TypeName</c>) referenced by compiled XAML/BAML
    /// in <paramref name="module"/>. Never throws on malformed resource or BAML payloads.
    /// </summary>
    public static HashSet<string> CollectReferencedTypes(ModuleDef module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var results = new HashSet<string>(StringComparer.Ordinal);
        CollectConnectorTypes(module, results);
        CollectBamlFromResources(module, results);
        return results;
    }

    /// <summary>
    /// Collects full type names (<c>Namespace.TypeName</c>) of types in <paramref name="module"/>
    /// that implement <c>IComponentConnector</c> or <c>IStyleConnector</c>.
    /// </summary>
    public static HashSet<string> CollectConnectorTypes(ModuleDef module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var results = new HashSet<string>(StringComparer.Ordinal);
        CollectConnectorTypes(module, results);
        return results;
    }

    private static void CollectConnectorTypes(ModuleDef module, ISet<string> results)
    {
        foreach (var type in module.GetTypes())
        {
            if (ImplementsXamlConnector(type))
            {
                var fullName = type.FullName;
                if (!string.IsNullOrEmpty(fullName))
                    results.Add(fullName.Replace('/', '.'));
            }
        }
    }

    internal static HashSet<string> CollectBamlReferencedTypes(ModuleDef module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var results = new HashSet<string>(StringComparer.Ordinal);
        CollectBamlFromResources(module, results);
        return results;
    }

    internal static void CollectFromBaml(ReadOnlySpan<byte> baml, ISet<string> results)
    {
        if (baml.Length < 4)
            return;

        var sigLen = BinaryPrimitives.ReadInt32LittleEndian(baml);
        if (sigLen <= 0 || (sigLen & 1) != 0 || sigLen > baml.Length - 4)
            return;

        var pad = (4 - (sigLen % 4)) % 4;
        const int versionBytes = 12; // Reader, Updater, Writer VersionPairs (6 * Int16)
        if (pad + versionBytes > baml.Length - 4 - sigLen)
            return;

        var signature = Encoding.Unicode.GetString(baml.Slice(4, sigLen));
        if (!string.Equals(signature, "MSBAML", StringComparison.Ordinal))
            return;

        var offset = 4 + sigLen + pad + versionBytes;

        while (offset < baml.Length)
        {
            var recordType = baml[offset++];
            if (recordType >= RecordPayloadSizes.Length)
                return;

            var spec = RecordPayloadSizes[recordType];
            if (spec == UnknownRecord)
                return;

            if (spec >= 0)
            {
                if (spec > baml.Length - offset)
                    return;

                offset += spec;
                if (recordType == DocumentEndRecord)
                    return;

                continue;
            }

            // Variable-size BAML records carry a 7-bit-encoded total size that includes the size field itself.
            var sizeFieldStart = offset;
            if (!TryRead7BitEncodedInt(baml, ref offset, out var totalSize))
                return;

            var sizeFieldLength = offset - sizeFieldStart;
            var payloadSize = totalSize - sizeFieldLength;
            if (payloadSize < 0 || payloadSize > baml.Length - offset)
                return;

            var payloadEnd = offset + payloadSize;

            if ((recordType == TypeInfoRecord || recordType == TypeSerializerInfoRecord) &&
                payloadSize >= 5)
            {
                var typeOffset = offset + 4; // Int16 TypeId + Int16 AssemblyId/Flags
                if (TryRead7BitEncodedInt(baml[..payloadEnd], ref typeOffset, out var strLen) &&
                    strLen > 0 &&
                    strLen <= payloadEnd - typeOffset)
                {
                    var typeFullName = Encoding.UTF8.GetString(baml.Slice(typeOffset, strLen)).Trim();
                    if (typeFullName.Length > 0)
                        results.Add(typeFullName.Replace('+', '.'));
                }
            }

            offset = payloadEnd;
        }
    }

    private static bool ImplementsXamlConnector(TypeDef type)
    {
        foreach (var iface in type.Interfaces)
        {
            var fullName = iface.Interface?.FullName;
            if (string.Equals(fullName, "System.Windows.Markup.IComponentConnector", StringComparison.Ordinal) ||
                string.Equals(fullName, "System.Windows.Markup.IStyleConnector", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectBamlFromResources(ModuleDef module, ISet<string> results)
    {
        foreach (var resource in module.Resources)
        {
            if (resource is not EmbeddedResource embedded)
                continue;

            var resourceName = embedded.Name?.String;
            if (string.IsNullOrEmpty(resourceName) ||
                !resourceName.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var raw = embedded.CreateReader().ToArray();
                using var stream = new MemoryStream(raw, writable: false);
                using var reader = new ResourceReader(stream);
                var enumerator = reader.GetEnumerator();

                while (enumerator.MoveNext())
                {
                    if (enumerator.Key is not string entryName ||
                        !entryName.EndsWith(".baml", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    reader.GetResourceData(entryName, out var resourceType, out var resourceData);
                    if (!string.Equals(resourceType, StreamResourceType, StringComparison.Ordinal) ||
                        resourceData is null ||
                        resourceData.Length < 4)
                    {
                        continue;
                    }

                    var streamLength = BinaryPrimitives.ReadInt32LittleEndian(resourceData);
                    if (streamLength <= 0 || streamLength > resourceData.Length - 4)
                        continue;

                    CollectFromBaml(resourceData.AsSpan(4, streamLength), results);
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or FormatException or IOException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                // Ignore malformed .g.resources entries gracefully.
            }
        }
    }

    private static bool TryRead7BitEncodedInt(ReadOnlySpan<byte> data, ref int offset, out int value)
    {
        uint result = 0;
        var shift = 0;

        while (true)
        {
            if (offset >= data.Length || shift >= 35)
            {
                value = 0;
                return false;
            }

            var b = data[offset++];
            if (shift == 28 && (b & 0xF0) != 0)
            {
                value = 0;
                return false;
            }

            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                value = (int)result;
                return value >= 0;
            }

            shift += 7;
        }
    }
}
