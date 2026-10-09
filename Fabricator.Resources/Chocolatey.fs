// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open System
open Fabricator.Core
open Fabricator.Resources.CommandUtil
open Fabricator.Resources.ResourceUtil

type Chocolatey =
    /// <summary>
    /// The concurrency group of the resources running Chocolatey, which doesn't support concurrent package operations.
    /// </summary>
    static member ConcurrencyGroup: ConcurrencyGroup = ConcurrencyGroup "Chocolatey"

    /// <summary>
    /// Represents a Chocolatey package resource within the Fabricator framework.
    /// This resource ensures that the specified Chocolatey package is installed
    /// with the given version on the system.
    /// </summary>
    /// <param name="name">The name of the Chocolatey package to manage.</param>
    /// <param name="version">The version of the Chocolatey package to ensure is installed.</param>
    /// <param name="dependsOn">The resources this resource depends on.</param>
    /// <returns>
    /// A <see cref="T:Fabricator.Core.Resource"/> checking the state of the package and applying necessary changes.
    /// </returns>
    static member chocolateyPackage(name: string, version: string, ?dependsOn: Resource seq): Resource =
        let getInstalledPackageVersion(reporter: IReporter) = async {
            reporter.Status "Querying the installed version"
            let! command = runCommand reporter "choco" [|"list"; name; "--exact"; "--limit-output"|]
            let data = command.StandardOutput

            let parseEntry(entry: string) =
                match entry.Split('|') with
                | [|_; version|] -> Some version
                | _ -> failwithf $"Cannot parse data entry \"{entry}\"."

            return
                match data.Split('\n', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries) with
                | [||] -> None
                | [| entry |] -> parseEntry entry
                | _ -> failwithf $"More than one line found in \"{data}\"."
        }

        let installPackage(reporter: IReporter) =
            reporter.Status $"Installing version {version}"
            runCommand reporter "choco" [|"install"; name; "--version"; version; "--yes"|] |> Async.Ignore

        let upgradePackage(reporter: IReporter) =
            reporter.Status $"Upgrading to version {version}"
            runCommand reporter "choco" [|"upgrade"; name; "--version"; version; "--yes"|] |> Async.Ignore

        {
            PresentableName = $"Package {name}"
            DependsOn = dependencies dependsOn
            Lock = Some Chocolatey.ConcurrencyGroup

            AlreadyApplied = fun ctx -> async {
                let! installedVersion = getInstalledPackageVersion ctx.Reporter
                return installedVersion = Some version
            }
            Apply = fun ctx -> async {
                let! installedVersion = getInstalledPackageVersion ctx.Reporter
                return!
                    match installedVersion with
                    | Some _ -> upgradePackage ctx.Reporter
                    | None -> installPackage ctx.Reporter
            }
        }
