using System.Diagnostics;
using System.Text.Json;
using System.Security.AccessControl;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotebookYt.Api;
using NotebookYt.Api.Studio;
using Npgsql;

namespace NotebookYt.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows ACL and DPAPI acceptance requires Windows.";
    }
}

public class BackupRestoreTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [WindowsFact]
    public async Task OperationalScriptsRestoreAllRowsMediaPermissionsAndProtectedConfiguration()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The operational PowerShell scripts currently support Windows only.");
        await new ControlledMediaWorkflowTests(fixture).RunWorkflow();
        var source = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        var pgBin = Environment.GetEnvironmentVariable("TEST_PG_BIN") ?? throw new InvalidOperationException("Set TEST_PG_BIN to the PostgreSQL executable directory.");
        var root = Path.Combine(Path.GetTempPath(), "notebook-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var targetName = "notebook_restore_" + Guid.NewGuid().ToString("N");
        var target = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = targetName };
        var backup = Path.Combine(root, "backup"); var media = Path.Combine(root, "media"); var keys = Path.Combine(root, "keys");
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var protection = fixture.Factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("AcceptanceRestore.v1");
        const string controlledValue = "controlled-test-value-never-a-real-google-token";
        await using (var db = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(source.ConnectionString).Options))
        {
            db.Add(new GoogleConnection { Kind = "YouTube", Status = "Disconnected", ProtectedTokens = protection.Protect(controlledValue) });
            await db.SaveChangesAsync();
        }
        // All writers are stopped: the fixture disables the worker and all HTTP calls above have completed.
        var lastDiagnostic = "";
        async Task<int> Script(string name, params string[] arguments)
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(repository, "scripts", name), "-PgBin", pgBin, "-Port", source.Port.ToString(), "-Username", source.Username!, "-Server", source.Host! }.Concat(arguments)) start.ArgumentList.Add(arg);
            if (!string.IsNullOrEmpty(source.Password)) start.Environment["PGPASSWORD"] = source.Password;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
            var diagnostic = await output + await error;
            lastDiagnostic = diagnostic;
            if (process.ExitCode != 0 && name.StartsWith("Backup", StringComparison.Ordinal)) Assert.Fail(diagnostic);
            return process.ExitCode;
        }
        try
        {
            Assert.Equal(0, await Script("Backup-NotebookYt.ps1", "-Database", source.Database!, "-MediaDirectory", fixture.StorageRoot, "-KeyDirectory", Path.Combine(fixture.StorageRoot, "keys"), "-Destination", backup, "-ApplicationStopped"));
            Assert.True(await Script("Restore-NotebookYt.ps1", "-BackupDirectory", backup, "-NewDatabase", targetName, "-MediaDirectory", media, "-KeyDirectory", keys) == 0, lastDiagnostic);
            await using var original = new NpgsqlConnection(source.ConnectionString); await original.OpenAsync();
            await using var restored = new NpgsqlConnection(target.ConnectionString); await restored.OpenAsync();
            await using var model = new EditorialDb(new DbContextOptionsBuilder<EditorialDb>().UseNpgsql(target.ConnectionString).Options);
            foreach (var table in model.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct())
            {
                var sql = $"SELECT COALESCE(json_agg(t ORDER BY t.\"Id\")::text, '[]') FROM \"{table}\" t";
                Assert.Equal(await new NpgsqlCommand(sql, original).ExecuteScalarAsync(), await new NpgsqlCommand(sql, restored).ExecuteScalarAsync());
            }
            Assert.Equal(6, (await model.Database.GetAppliedMigrationsAsync()).Count());
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(backup, "manifest.json")));
            foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
            {
                var path = file.GetProperty("path").GetString()!;
                if (path == "database.dump") continue;
                var restoredPath = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                Assert.Equal(file.GetProperty("sha256").GetString()!.ToLowerInvariant(), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(restoredPath))));
            }
            foreach (var permission in manifest.RootElement.GetProperty("permissions").EnumerateArray())
            {
                var path = Path.Combine(root, permission.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                FileSystemSecurity acl = Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl();
                Assert.Equal(permission.GetProperty("sddl").GetString(), acl.GetSecurityDescriptorSddlForm(AccessControlSections.All));
            }
            var services = new ServiceCollection();
            services.AddDataProtection().SetApplicationName("NotebookYt").PersistKeysToFileSystem(new DirectoryInfo(keys)).ProtectKeysWithDpapi();
            using var provider = services.BuildServiceProvider();
            var connection = await model.Set<GoogleConnection>().SingleAsync();
            Assert.Equal(controlledValue, provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("AcceptanceRestore.v1").Unprotect(connection.ProtectedTokens));
            // Refuse overwriting a database, even when the requested directories are new.
            Assert.NotEqual(0, await Script("Restore-NotebookYt.ps1", "-BackupDirectory", backup, "-NewDatabase", targetName, "-MediaDirectory", Path.Combine(root, "other-media"), "-KeyDirectory", Path.Combine(root, "other-keys")));
            Assert.False(Directory.Exists(Path.Combine(root, "other-media")));
            await File.AppendAllTextAsync(Path.Combine(backup, "database.dump"), "corruption");
            Assert.NotEqual(0, await Script("Restore-NotebookYt.ps1", "-BackupDirectory", backup, "-NewDatabase", "notebook_restore_" + Guid.NewGuid().ToString("N"), "-MediaDirectory", Path.Combine(root, "other-media"), "-KeyDirectory", Path.Combine(root, "other-keys")));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(source.ConnectionString) { Database = "postgres" }.ConnectionString);
            await admin.OpenAsync(); await new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{targetName}\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("notebook-restore-", StringComparison.Ordinal)) Directory.Delete(root, true);
        }
    }
}
