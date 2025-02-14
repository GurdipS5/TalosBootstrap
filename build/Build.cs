using System;
using System.Diagnostics;
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

    public static int Main () => Execute<Build>(x => x.ExecutePolaris);

    [PathVariable("kubeneat")]
    readonly Tool KubeNeat;

    [PathVariable("polaris")]
    readonly Tool Polaris;

    [PathVariable("powershell")]
    readonly Tool Pwsh;

    [PathVariable("ggshield")]
    readonly Tool GGCli;


    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    Target KubeLintScan=> _ => _
        .Executes(() =>
        {
           if (NukeBuild.IsServerBuild) {
           // KubeNeat();
          }
        });

   Target KubeConform => _ => _
       .DependsOn(KubeLintScan)
       .AssuredAfterFailure()
        .Executes(() =>
        {
           Pwsh(@"Get-ChildItem -Path .\Manifests -Recurse -Filter *.yaml | ForEach-Object { kubeconform -strict -verbose -output junit -ignore-missing-schemas -summary  $_.FullName } " , @"D:\Repositories\Talosbootstrap");

        });

     Target KubeLint => _ => _
        .DependsOn(KubeConform)
        .AssuredAfterFailure()
        .Executes(() =>
        {
           Pwsh(@"Get-ChildItem -Path .\Manifests -Recurse -Include  '*.yaml',  '*.yml' | ForEach-Object { kube-linter lint $_.FullName } " );

        });

    Target RunSemGrep => _ => _
        .DependsOn(KubeLint)
        .AssuredAfterFailure()
        .Executes(() =>
        {
           Process.Start("semgrep", "ci")?.WaitForExit();
        
        });

       Target SecretScan => _ => _
        .DependsOn(RunSemGrep)
        .AssuredAfterFailure()
        .Executes(() =>
        {
        });


    Target ExecutePolaris => _ => _
        .DependsOn(SecretScan)
        .AssuredAfterFailure()
        .Executes(() =>
        {
            Polaris("audit --audit-path ./Manifests/ --set-exit-code-on-danger --set-exit-code-below-score 90");
        });

}
