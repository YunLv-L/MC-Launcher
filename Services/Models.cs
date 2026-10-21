using System.Collections.Generic;

namespace MCLauncher;

public class VersionManifest
{
    public List<VersionEntry> Versions { get; set; } = new();
}

public class VersionEntry
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = ""; // 新增：版本类型（release, snapshot, old_alpha...）
    public string Url { get; set; } = "";
}

public class VersionDetail
{
    public string Id { get; set; } = "";
    public string? InheritsFrom { get; set; }
    public string? MainClass { get; set; }
    public DownloadsInfo? Downloads { get; set; }
    public List<Library> Libraries { get; set; } = new();
    public AssetIndexInfo? AssetIndex { get; set; }
    public Arguments? Arguments { get; set; }
}

public class Arguments
{
    public List<object>? Game { get; set; }
    public List<object>? Jvm { get; set; }
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

// ================== Fabric API 模型 ==================
public class FabricLoaderEntry
{
    public LoaderInfo Loader { get; set; } = new();
    public LauncherMetaInfo? LauncherMeta { get; set; }
}

public class LoaderInfo
{
    public string Version { get; set; } = "";
    public bool Stable { get; set; }
}

public class LauncherMetaInfo
{
    public LibrariesInfo? Libraries { get; set; }
}

public class LibrariesInfo
{
    public List<FabricLibrary>? Client { get; set; }
}

public class FabricLibrary
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Sha1 { get; set; }
}
