namespace Obfy.UI.Help;

public sealed record HelpTopic(
    string Id,
    string Title,
    IReadOnlyList<HelpBlock> Blocks);
