// Copyright (c) Cratis. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Cratis.Cli.for_CliUpdate;

public class when_detecting_strategy : Specification
{
    [Fact]
    void should_detect_dotnet_tool_from_path()
    {
        var strategy = CliUpdate.DetectStrategy(
            "/home/user/.dotnet/tools/cratis",
            "/home/user/.dotnet/tools/.store/cratis.cli/",
            isNativeBuild: false,
            isMacOS: false,
            isLinux: true);

        strategy.ShouldEqual(CliUpdateStrategy.DotNetTool);
    }

    [Fact]
    void should_detect_homebrew_when_native_on_macos()
    {
        var strategy = CliUpdate.DetectStrategy(
            "/opt/homebrew/Cellar/cratis/1.2.3/bin/cratis",
            "/opt/homebrew/Cellar/cratis/1.2.3/bin/",
            isNativeBuild: true,
            isMacOS: true,
            isLinux: false);

        strategy.ShouldEqual(CliUpdateStrategy.Homebrew);
    }

    [Fact]
    void should_detect_manual_linux_when_native_on_linux()
    {
        var strategy = CliUpdate.DetectStrategy(
            "/usr/local/bin/cratis",
            "/usr/local/bin/",
            isNativeBuild: true,
            isMacOS: false,
            isLinux: true);

        strategy.ShouldEqual(CliUpdateStrategy.ManualLinux);
    }

    [Fact]
    void should_detect_winget_from_path()
    {
        var strategy = CliUpdate.DetectStrategy(
            @"C:\Users\user\AppData\Local\Microsoft\WinGet\Packages\Cratis.Cli_abc123\cratis.exe",
            @"C:\Users\user\AppData\Local\Microsoft\WinGet\Packages\Cratis.Cli_abc123\",
            isNativeBuild: true,
            isMacOS: false,
            isLinux: false);

        strategy.ShouldEqual(CliUpdateStrategy.Winget);
    }

    [Fact]
    void should_detect_winget_from_machine_scoped_path()
    {
        var strategy = CliUpdate.DetectStrategy(
            @"C:\Program Files\WinGet\Packages\Cratis.Cli_abc123\cratis.exe",
            @"C:\Program Files\WinGet\Packages\Cratis.Cli_abc123\",
            isNativeBuild: true,
            isMacOS: false,
            isLinux: false);

        strategy.ShouldEqual(CliUpdateStrategy.Winget);
    }

    [Fact]
    void should_detect_chocolatey_from_bin_path()
    {
        var strategy = CliUpdate.DetectStrategy(
            @"C:\ProgramData\chocolatey\bin\cratis.exe",
            @"C:\ProgramData\chocolatey\bin\",
            isNativeBuild: true,
            isMacOS: false,
            isLinux: false);

        strategy.ShouldEqual(CliUpdateStrategy.Chocolatey);
    }

    [Fact]
    void should_detect_chocolatey_from_lib_path()
    {
        var strategy = CliUpdate.DetectStrategy(
            @"C:\ProgramData\chocolatey\lib\cratis\tools\cratis.exe",
            @"C:\ProgramData\chocolatey\lib\cratis\tools\",
            isNativeBuild: true,
            isMacOS: false,
            isLinux: false);

        strategy.ShouldEqual(CliUpdateStrategy.Chocolatey);
    }

    [Fact]
    void should_use_cratis_update_hint_for_auto_update_strategies()
    {
        var hint = CliUpdate.GetUpdateHint(CliUpdateStrategy.Homebrew, "1.0.0", "1.1.0");
        hint.ShouldContain("run 'cratis update'");
        hint.ShouldContain("1.0.0 -> 1.1.0");
    }

    [Fact]
    void should_use_cratis_update_hint_for_winget()
    {
        var hint = CliUpdate.GetUpdateHint(CliUpdateStrategy.Winget, "1.0.0", "1.1.0");
        hint.ShouldContain("run 'cratis update'");
        hint.ShouldContain("1.0.0 -> 1.1.0");
    }

    [Fact]
    void should_use_cratis_update_hint_for_chocolatey()
    {
        var hint = CliUpdate.GetUpdateHint(CliUpdateStrategy.Chocolatey, "1.0.0", "1.1.0");
        hint.ShouldContain("run 'cratis update'");
        hint.ShouldContain("1.0.0 -> 1.1.0");
    }

    [Fact]
    void should_prepare_brew_update_before_upgrade_for_homebrew()
    {
        var startInfo = CliUpdate.CreatePreUpdateProcessStartInfo(CliUpdateStrategy.Homebrew);
        startInfo.ShouldNotBeNull();
        startInfo!.FileName.ShouldEqual("brew");
        startInfo.Arguments.ShouldEqual("update");
    }

    [Fact]
    void should_not_prepare_brew_update_when_target_version_is_set()
    {
        var startInfo = CliUpdate.CreatePreUpdateProcessStartInfo(CliUpdateStrategy.Homebrew, "1.2.3");
        startInfo.ShouldBeNull();
    }

    [Fact]
    void should_create_winget_upgrade_process_start_info()
    {
        var startInfo = CliUpdate.CreateUpdateProcessStartInfo(CliUpdateStrategy.Winget);
        startInfo.ShouldNotBeNull();
        startInfo!.FileName.ShouldEqual("winget");
        startInfo.Arguments.ShouldEqual("upgrade --id Cratis.Cli");
    }

    [Fact]
    void should_create_winget_upgrade_process_start_info_with_version()
    {
        var startInfo = CliUpdate.CreateUpdateProcessStartInfo(CliUpdateStrategy.Winget, "1.2.3");
        startInfo.ShouldNotBeNull();
        startInfo!.FileName.ShouldEqual("winget");
        startInfo.Arguments.ShouldEqual("upgrade --id Cratis.Cli --version 1.2.3");
    }

    [Fact]
    void should_create_chocolatey_upgrade_process_start_info()
    {
        var startInfo = CliUpdate.CreateUpdateProcessStartInfo(CliUpdateStrategy.Chocolatey);
        startInfo.ShouldNotBeNull();
        startInfo!.FileName.ShouldEqual("choco");
        startInfo.Arguments.ShouldEqual("upgrade cratis --yes");
    }

    [Fact]
    void should_create_chocolatey_upgrade_process_start_info_with_version()
    {
        var startInfo = CliUpdate.CreateUpdateProcessStartInfo(CliUpdateStrategy.Chocolatey, "1.2.3");
        startInfo.ShouldNotBeNull();
        startInfo!.FileName.ShouldEqual("choco");
        startInfo.Arguments.ShouldEqual("upgrade cratis --yes --version 1.2.3");
    }
}
