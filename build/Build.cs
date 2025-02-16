using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.PathConstruction;

class Build : NukeBuild
{
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main() => Execute<Build>(x => x.ExecutePolaris);


      AbsolutePath OutputsDirectory => RootDirectory / "output";


    [PathVariable("kubeneat")]
    readonly Tool KubeNeat;

    [PathVariable("polaris")]
    readonly Tool Polaris;

    [PathVariable("pwsh")]
    readonly Tool Pwsh;

    [PathVariable("ggshield")]
    readonly Tool GGCli;

    [PathVariable("kubescape")]
    readonly Tool Kubescape;

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild
        ? Configuration.Debug
        : Configuration.Release;

    Target KubeLintScan =>
        _ =>
            _.Executes(() =>
            {
                if (NukeBuild.IsServerBuild)
                {
                    // KubeNeat();
                }
            });

    Target KubeConform =>
        _ =>
            _.DependsOn(KubeLintScan)
                .AssuredAfterFailure()
                .Executes(() =>
                {
                    string currentDirectory = Directory.GetCurrentDirectory();
                    DirectoryInfo parentDir = Directory.GetParent(currentDirectory);

                    Pwsh(
                        @"Get-ChildItem -Path .\Manifests -Recurse -Filter *.yaml | ForEach-Object { kubeconform -strict -verbose -output junit -ignore-missing-schemas -summary  $_.FullName } ",
                        parentDir.FullName
                    );
                });

    Target KubeLint =>
        _ =>
            _.DependsOn(KubeConform)
                .AssuredAfterFailure()
                .Executes(() =>
                {
                    Pwsh(
                        @"Get-ChildItem -Path .\Manifests -Recurse -Include  '*.yaml',  '*.yml' | ForEach-Object { kube-linter lint $_.FullName } "
                    );
                });

    /// <summary>
    /// 
    /// </summary>
    Target RunSemGrep =>
        _ =>
            _.DependsOn(KubeLint)
                .AssuredAfterFailure()
                .Executes(() =>
                {
                    Process.Start("semgrep", "ci")?.WaitForExit();
                });


    /// <summary>
    /// 
    /// </summary>
    Target SecretScan => _ => _.DependsOn(RunSemGrep).AssuredAfterFailure().Executes(() => { });

    Target ExecuteKubescapes =>
        _ =>
            _.DependsOn(SecretScan)
                .AssuredAfterFailure()
                .Produces(OutputsDirectory / ".zip" )
                .Executes(() =>
                {
                    string junitFile = "junit-kubescape-report.xml"; // Change this to your actual JUnit file name
                    string outputDir = "outputs";
                    string zipFilePath = Path.Combine(outputDir, "junit-kubescape-report.zip");

                    Kubescape(
                        "scan . --format junit --output ./Outputs/junit-kubescape-results.xml"
                    );

                    using (FileStream zipToOpen = new FileStream(zipFilePath, FileMode.Create))
                    using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                    {
                        if (File.Exists(junitFile))
                        {
                            archive.CreateEntryFromFile(junitFile, Path.GetFileName(junitFile));
                            Console.WriteLine($"Successfully zipped {junitFile} to {zipFilePath}");
                        }
                        else
                        {
                            Console.WriteLine($"Error: {junitFile} not found");
                        }
                    }
                });

    Target ExecutePolaris =>
        _ =>
            _.DependsOn(ExecuteKubescapes)
                .AssuredAfterFailure()
                .Executes(() =>
                {
                    Polaris(
                        "audit --audit-path ./Manifests/ --set-exit-code-on-danger --set-exit-code-below-score 90"
                    );
                });
}
