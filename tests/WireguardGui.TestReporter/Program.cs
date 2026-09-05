using System.Diagnostics;
using System.Xml.Linq;
using Spectre.Console;

namespace WireguardGui.TestReporter;

internal static class Program
{
    private static readonly string[] TestProjects =
    [
        "tests/WireguardGui.Application.Tests/WireguardGui.Application.Tests.csproj",
        "tests/WireguardGui.App.Tests/WireguardGui.App.Tests.csproj",
        "tests/WireguardGui.Infrastructure.Tests/WireguardGui.Infrastructure.Tests.csproj",
    ];

    private static async Task<int> Main(string[] args)
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            AnsiConsole.MarkupLine("[red]Не найден корень репозитория (нет .sln)[/]");
            return 1;
        }

        var filter = args.Length > 0 ? args[0] : null;
        var trxDir = Path.Combine(Path.GetTempPath(), "wg-trx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(trxDir);

        AnsiConsole.MarkupLine("[bold]Запуск тестов…[/]");
        var results = new List<TestProjectResult>();

        foreach (var project in TestProjects)
        {
            var projectPath = Path.Combine(root, project);
            if (!File.Exists(projectPath))
                continue;

            var trxPath = Path.Combine(trxDir, Path.GetFileNameWithoutExtension(project) + ".trx");
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("test");
            psi.ArgumentList.Add(projectPath);
            psi.ArgumentList.Add("--logger");
            psi.ArgumentList.Add($"trx;LogFileName={trxPath}");
            if (filter is not null)
            {
                psi.ArgumentList.Add("--filter");
                psi.ArgumentList.Add(filter);
            }

            using var process = Process.Start(psi);
            if (process is null)
                continue;

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            results.Add(ParseTrx(trxPath, Path.GetFileNameWithoutExtension(project), process.ExitCode));
        }

        PrintReport(results);
        return results.Any(r => r.Failed > 0 || r.Errors > 0) ? 1 : 0;
    }

    private static TestProjectResult ParseTrx(string trxPath, string projectName, int exitCode)
    {
        var result = new TestProjectResult(projectName);
        if (!File.Exists(trxPath))
        {
            result.Errors = exitCode;
            return result;
        }

        var doc = XDocument.Load(trxPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        foreach (var unit in doc.Descendants(ns + "UnitTestResult"))
        {
            var outcome = (string?)unit.Attribute("outcome") ?? "NotExecuted";
            var testName = (string?)unit.Attribute("testName") ?? "?";
            var duration = (string?)unit.Attribute("duration") ?? "00:00:00";
            var errorMessage = unit.Descendants(ns + "Message").FirstOrDefault()?.Value?.Trim();

            switch (outcome)
            {
                case "Passed":
                    result.Passed++;
                    result.Details.Add(new TestDetail(testName, TestOutcome.Passed, duration, null));
                    break;
                case "Failed":
                    result.Failed++;
                    result.Details.Add(new TestDetail(testName, TestOutcome.Failed, duration, errorMessage));
                    break;
                default:
                    result.Skipped++;
                    result.Details.Add(new TestDetail(testName, TestOutcome.Skipped, duration, null));
                    break;
            }
        }

        return result;
    }

    private static void PrintReport(List<TestProjectResult> results)
    {
        var totalPassed = results.Sum(r => r.Passed);
        var totalFailed = results.Sum(r => r.Failed);
        var totalSkipped = results.Sum(r => r.Skipped);
        var totalErrors = results.Sum(r => r.Errors);

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[bold]Результаты тестов[/]") { Style = Style.Parse("blue") });

        foreach (var project in results)
        {
            AnsiConsole.MarkupLine($"[bold]{project.Name}[/]");
            foreach (var detail in project.Details)
            {
                var icon = detail.Outcome switch
                {
                    TestOutcome.Passed => "[green]✅[/]",
                    TestOutcome.Failed => "[red]❌[/]",
                    _ => "[yellow]⏭[/]",
                };
                var color = detail.Outcome switch
                {
                    TestOutcome.Passed => "green",
                    TestOutcome.Failed => "red",
                    _ => "yellow",
                };
                var shortName = ShortName(detail.Name);
                AnsiConsole.MarkupLine($"  {icon} [{color}]{shortName.EscapeMarkup()}[/] [dim]({detail.Duration})[/]");
                if (detail.ErrorMessage is not null)
                    AnsiConsole.MarkupLine($"      [red]{detail.ErrorMessage.EscapeMarkup()}[/]");
            }
            AnsiConsole.MarkupLine(
                $"  [dim]Итого:[/] [green]{project.Passed} ✅[/] [red]{project.Failed} ❌[/] [yellow]{project.Skipped} ⏭[/]");
            AnsiConsole.WriteLine();
        }

        AnsiConsole.Write(new Rule() { Style = Style.Parse("blue") });
        var summaryColor = totalFailed > 0 || totalErrors > 0 ? "red" : "green";
        AnsiConsole.MarkupLine(
            $"[bold {summaryColor}]Итого: {totalPassed} ✅  {totalFailed} ❌  {totalSkipped} ⏭  ({totalErrors} ошибок сборки)[/]");
    }

    private static string ShortName(string fullName)
    {
        // xUnit: "Namespace.Class.Method(parameters)" — показываем Class.Method.
        var withoutParams = fullName.Split('(')[0];
        var parts = withoutParams.Split('.');
        if (parts.Length < 2)
            return fullName;
        var name = string.Join(".", parts[^2..]);
        return name.Length <= 60 ? name : name[..57] + "…";
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WireguardGui.sln")) ||
                File.Exists(Path.Combine(dir.FullName, "WireguardGui.slnx")) ||
                Directory.GetFiles(dir.FullName, "*.sln").Length > 0 ||
                Directory.GetFiles(dir.FullName, "*.slnx").Length > 0)
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private sealed record TestProjectResult(string Name)
    {
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
        public List<TestDetail> Details { get; } = [];
    }

    private sealed record TestDetail(string Name, TestOutcome Outcome, string Duration, string? ErrorMessage);

    private enum TestOutcome
    {
        Passed,
        Failed,
        Skipped,
    }
}