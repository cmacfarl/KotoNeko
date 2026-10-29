using KotoNeko.Data.Backup;
using Xunit;

namespace KotoNeko.Tests;

public class BackupTests
{
    [Fact]
    public void Finds_The_Backups_Folder_At_The_Repository_Root()
    {
        string root = Directory.CreateTempSubdirectory("kotoneko-repo-").FullName;
        try {
            File.WriteAllText(Path.Combine(root, "KotoNeko.sln"), "");
            string deep = Directory.CreateDirectory(Path.Combine(root, "src", "KotoNeko.Web", "bin")).FullName;

            Assert.Equal(Path.Combine(root, "backups"), BackupFiles.FindRepositoryFolder(deep));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void No_Repository_Means_No_Backups_Folder()
    {
        string lonely = Directory.CreateTempSubdirectory("kotoneko-lonely-").FullName;
        try {
            // Only correct when nothing above the temp folder holds KotoNeko.sln, which holds for temp.
            Assert.Null(BackupFiles.FindRepositoryFolder(lonely));
        } finally {
            Directory.Delete(lonely);
        }
    }

    [Theory]
    [InlineData(null, "kotoneko-2026-10-29-161500.sql.gz")]
    [InlineData("before-migration", "kotoneko-2026-10-29-161500-before-migration.sql.gz")]
    [InlineData("Odd / Reason!", "kotoneko-2026-10-29-161500-odd---reason.sql.gz")]
    public void Names_Sort_By_Time_And_Carry_A_Safe_Reason(string? reason, string expected)
    {
        Assert.Equal(expected, BackupFiles.NewName(new DateTime(2026, 10, 29, 16, 15, 0), reason));
    }

    [Fact]
    public void Lists_Only_Backups_Newest_First()
    {
        string folder = Directory.CreateTempSubdirectory("kotoneko-backups-").FullName;
        try {
            string older = Path.Combine(folder, "kotoneko-2026-10-01-000000.sql.gz");
            string newer = Path.Combine(folder, "kotoneko-2026-10-29-000000.sql.gz");
            File.WriteAllText(older, "a");
            File.WriteAllText(newer, "bb");
            File.WriteAllText(Path.Combine(folder, "kotoneko-2026-10-30-000000.sql.gz.partial"), "x");
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "x");
            File.SetLastWriteTimeUtc(older, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(newer, new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc));

            List<BackupFile> files = BackupFiles.List(folder);

            Assert.Equal(new[] { Path.GetFileName(newer), Path.GetFileName(older) }, files.Select(f => f.Name));
            Assert.Equal(2, files[0].Size);
        } finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Option_File_Quotes_And_Escapes_The_Password()
    {
        string file = BackupFiles.OptionFile("localhost", 3306, "kn", "p\"a\\ss word");

        Assert.Contains("[client]", file);
        Assert.Contains("port=3306", file);
        Assert.Contains("user=\"kn\"", file);
        Assert.Contains("password=\"p\\\"a\\\\ss word\"", file);
    }
}
