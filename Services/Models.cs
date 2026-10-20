using System.Collections.Generic;

namespace MCLauncher;

public class VersionManifest
{
    public List<VersionEntry> Versions { get; set; } = new();
}

public class VersionEntry
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
}

public class VersionDetail
{
    public string Id { get; set; } = "";
    public DownloadsInfo? Downloads { get; set; }
    public List<Library> Libraries { get; set; } = new();
    public AssetIndexInfo? AssetIndex { get; set; }
}

public class DownloadsInfo
{
    public Artifact? Client { get; set; }
}

public class Library
{
    public string Name { get; set; } = "";
    public LibraryDownloads? Downloads { get; set; }
    public List<Rule>? Rules { get; set; }
}

public class LibraryDownloads
{
    public Artifact? Artifact { get; set; }
}

public class Artifact
{
    public string Path { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
}

public class AssetIndexInfo
{
    public string Id { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public string Url { get; set; } = "";
}

public class AssetIndex
{
    public Dictionary<string, AssetObject> Objects { get; set; } = new();
}

public class AssetObject
{
    public string Hash { get; set; } = "";
    public long Size { get; set; }
}

public class Rule
{
    public string Action { get; set; } = "";
    public RuleOs? Os { get; set; }
}

public class RuleOs
{
    public string? Name { get; set; }
    public string? Arch { get; set; }
}