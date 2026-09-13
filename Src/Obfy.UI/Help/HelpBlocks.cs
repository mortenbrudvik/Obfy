namespace Obfy.UI.Help;

public abstract record HelpBlock;

public sealed record HelpParagraph(string Text) : HelpBlock;

public sealed record HelpShortcut(string Keys, string Action) : HelpBlock;

public sealed record HelpNamedNote(string Heading, string Text) : HelpBlock;
