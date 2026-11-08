using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class UserSettings
{
    private const string SettingsFile = "settings.json";
    private const string QueryFile = "last-query.txt";
    private static readonly Encoding Utf8 = new UTF8Encoding(false);
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WaimaiCoupon", "v1.1");

    public bool DarkMode { get; set; }
    public int Concurrency { get; set; }
    public int IntervalMs { get; set; }
    public int SortIndex { get; set; }
    public int FilterMask { get; set; }

    public UserSettings()
    {
        Concurrency = 1;
        FilterMask = 0x7f;
    }

    internal static UserSettings Load()
    {
        string json = Read(SettingsFile);
        if (json != null)
        {
            try { return new JavaScriptSerializer().Deserialize<UserSettings>(json) ?? new UserSettings(); }
            catch (ArgumentException error) { Trace.WriteLine(error); }
            catch (InvalidOperationException error) { Trace.WriteLine(error); }
        }
        return new UserSettings();
    }

    internal void Save()
    {
        Write(SettingsFile, new JavaScriptSerializer().Serialize(this));
    }

    internal static string ReadLastQuery()
    {
        return Read(QueryFile);
    }

    internal static bool SaveLastQuery(string text)
    {
        return Write(QueryFile, text);
    }

    private static string Read(string fileName)
    {
        try
        {
            using (var stream = new FileStream(Path.Combine(DirectoryPath, fileName), FileMode.Open,
                FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var reader = new StreamReader(stream, Utf8, false))
                return reader.ReadToEnd();
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException error) { Trace.WriteLine(error); }
        catch (UnauthorizedAccessException error) { Trace.WriteLine(error); }
        return null;
    }

    private static bool Write(string fileName, string text)
    {
        string path = Path.Combine(DirectoryPath, fileName);
        string pending = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(pending, text, Utf8);
            try { File.Move(pending, path); }
            catch (IOException)
            {
                if (!File.Exists(path)) throw;
                File.Replace(pending, path, null);
            }
            return true;
        }
        catch (IOException error) { Trace.WriteLine(error); }
        catch (UnauthorizedAccessException error) { Trace.WriteLine(error); }
        finally
        {
            try { File.Delete(pending); }
            catch (IOException error) { Trace.WriteLine(error); }
            catch (UnauthorizedAccessException error) { Trace.WriteLine(error); }
        }
        return false;
    }
}
