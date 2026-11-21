namespace Obfy.VisualStudio.Commands;

/// <summary>
/// The package and command-set GUIDs. They must match <c>ObfyVsixCommandTable.vsct</c> (<c>guidObfyPackage</c> and
/// <c>guidObfyPackageCmdSet</c>); <c>CommandTableTests</c> reads the .vsct and fails when they drift.
/// </summary>
internal static class PackageGuids
{
    public const string PackageString = "5a9b8c7d-6e5f-4a3b-2c1d-0e9f8a7b6c5d";

    /// <summary>
    /// The GUID every command is registered under. The one-argument <c>[Command(id)]</c> would use the package
    /// GUID instead, which names no command in the .vsct, so the command would never be found.
    /// </summary>
    public const string CommandSetString = "6b0c9d8e-7f6a-5b4c-3d2e-1f0a9b8c7d6e";
}

/// <summary>
/// Command IDs - must match the values in the .vsct file (pinned by <c>CommandTableTests</c>)
/// </summary>
internal static class PackageIds
{
    public const int cmdidObfuscate = 0x0100;
    public const int cmdidTogglePostBuild = 0x0101;
    public const int cmdidOpenSettings = 0x0102;
    public const int cmdidCancelObfuscation = 0x0103;
}
