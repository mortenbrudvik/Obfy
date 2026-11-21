namespace Obfy.Tests;

/// <summary>
/// The same version.json contents are given to both readers, build/Get-ProductVersion.ps1 (installer build and
/// release-tag check) and Directory.Build.props (every Src project), so that they cannot disagree about what the
/// product version is. A file is either exactly the three top-level integer keys major, minor and patch (each
/// 0..65535, no leading zeros, no duplicates, no other content) or it is rejected.
/// </summary>
public static class VersionJsonCases
{
    public static TheoryData<string, string> Accepted => new()
    {
        { "{ \"major\": 2, \"minor\": 0, \"patch\": 1 }", "2.0.1" },
        // The order of the keys does not matter.
        { "{\"patch\":7,\"minor\":12,\"major\":3}", "3.12.7" },
        // CRLF, tabs and a two-digit patch, as an editor on Windows writes the file.
        { "{\r\n\t\"major\": 2,\r\n\t\"minor\": 0,\r\n\t\"patch\": 10\r\n}\r\n", "2.0.10" },
        // A byte order mark.
        { "﻿{ \"major\": 2, \"minor\": 0, \"patch\": 10 }", "2.0.10" },
        { "{ \"major\": 65535, \"minor\": 0, \"patch\": 0 }", "65535.0.0" },
        { "{ \"Major\": 2, \"MINOR\": 1, \"patch\": 0 }", "2.1.0" },
    };

    public static TheoryData<string> Rejected => new()
    {
        "",
        "not json",
        "{ \"major\": 2, \"minor\": 0 }",
        "{ \"major\": \"2\", \"minor\": 0, \"patch\": 0 }",
        "{ \"major\": 2.5, \"minor\": 0, \"patch\": 0 }",
        "{ \"major\": -1, \"minor\": 0, \"patch\": 0 }",
        "{ \"major\": 2, \"minor\": 65536, \"patch\": 0 }",
        "{ \"major\": true, \"minor\": 0, \"patch\": 0 }",
        "{ \"major\": 007, \"minor\": 0, \"patch\": 0 }",
        "{ \"major\": 2, \"minor\": 0, \"patch\": 0, }",
        // Anything but the three keys: a nested object would be read as 1.9.9 by a first-match reader.
        "{\"previous\":{\"major\":1,\"minor\":9,\"patch\":9},\"major\":2,\"minor\":0,\"patch\":0}",
        "{ \"major\": 2, \"minor\": 0, \"patch\": 0, \"label\": \"beta\" }",
        "{\"major\":1,\"major\":2,\"minor\":0,\"patch\":0}",
        "{ \"major\": 2, \"minor\": 0, \"patch\": 0, \"note\": \"it's $(Bad) %24 ;\" }",
    };
}
