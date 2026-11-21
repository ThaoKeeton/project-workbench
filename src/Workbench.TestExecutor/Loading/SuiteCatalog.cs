namespace Workbench.TestExecutor;

public sealed record SuiteListItem(string Id, string Name, int Cases, string Format, bool Loadable, string? Error);

public sealed class SuiteCatalog
{
    private readonly TestSuite _builtin;

    public SuiteCatalog(SuiteLoader loader)
    {
        _ = loader;
        _builtin = SuiteLoader.LoadBuiltin();
    }

    public TestSuite Builtin => _builtin;

    public IReadOnlyList<SuiteListItem> List(string dataDirectory)
    {
        var loaded = ReadFiles(dataDirectory);
        var items = new List<SuiteListItem>();
        foreach (var group in loaded.Where(file => file.Suite is not null).GroupBy(file => file.Suite!.Id, StringComparer.Ordinal))
        {
            if (group.Count() > 1)
            {
                items.Add(new SuiteListItem(group.Key, "", 0, group.First().Format, false, ExecutorError.DuplicateSuiteId));
                continue;
            }
            var file = group.Single();
            items.Add(new SuiteListItem(file.Suite!.Id, file.Suite.Name, file.Suite.Cases.Count, file.Format, true, null));
        }
        foreach (var invalid in loaded.Where(file => file.Suite is null && file.Stem is not null))
            items.Add(new SuiteListItem(invalid.Stem!, "", 0, invalid.Format, false, ExecutorError.InvalidSuite));
        if (items.All(item => item.Id != _builtin.Id))
            items.Insert(0, new SuiteListItem(_builtin.Id, _builtin.Name, _builtin.Cases.Count, "builtin", true, null));
        return items;
    }

    public static string? TryGetFile(string dataDirectory, string suiteId)
        => ReadFiles(dataDirectory).FirstOrDefault(file => file.Suite?.Id == suiteId && file.Path is not null)?.Path;

    public TestSuite Require(string dataDirectory, string suiteId)
    {
        if (!TextRules.IsSafeId(suiteId)) throw new ExecutorDataException(ExecutorError.SuiteNotFound);
        var matches = ReadFiles(dataDirectory).Where(file => file.Suite?.Id == suiteId).Select(file => file.Suite!).ToArray();
        if (matches.Length > 1) throw new ExecutorDataException(ExecutorError.DuplicateSuiteId);
        if (matches.Length == 1) return matches[0];
        if (suiteId == _builtin.Id) return _builtin;
        throw new ExecutorDataException(ExecutorError.SuiteNotFound);
    }

    private static List<LoadedFile> ReadFiles(string dataDirectory)
    {
        var result = new List<LoadedFile>();
        var directory = Path.Combine(Path.GetFullPath(dataDirectory), "suites");
        if (!Directory.Exists(directory)) return result;
        var info = new DirectoryInfo(directory);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return result;
        foreach (var path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            var file = new FileInfo(path);
            var extension = file.Extension.ToLowerInvariant();
            if (extension is not (".json" or ".yaml" or ".yml")) continue;
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.Length > SuiteLoader.MaxDocumentBytes) continue;
            var format = extension == ".json" ? "json" : "yaml";
            var stem = Path.GetFileNameWithoutExtension(file.Name);
            var safeStem = TextRules.IsSafeId(stem) ? stem : null;
            try
            {
                var suite = SuiteLoader.Load(File.ReadAllText(path));
                result.Add(new LoadedFile(suite, format, safeStem, path));
            }
            catch (ExecutorDataException)
            {
                result.Add(new LoadedFile(null, format, safeStem, null));
            }
            catch (IOException)
            {
                result.Add(new LoadedFile(null, format, safeStem, null));
            }
        }
        return result;
    }

    private sealed record LoadedFile(TestSuite? Suite, string Format, string? Stem, string? Path);
}
