// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Resources.Chocolatey

open System
open Fabricator.Core
open Fabricator.Resources.CommandUtil

let private getInstalledPackageVersion name = async {
    let! command = runCommand "choco" [|"list"; name; "--exact"; "--limit-output"|]
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

let private installPackage name version =
    runCommand "choco" [|"install"; name; "--version"; version; "--yes"|] |> Async.Ignore

let private upgradePackage name version =
    runCommand "choco" [|"upgrade"; name; "--version"; version; "--yes"|] |> Async.Ignore

/// <summary>
/// Represents a Chocolatey package resource within the Fabricator framework.
/// This resource ensures that the specified Chocolatey package is installed
/// with the given version on the system.
/// </summary>
/// <param name="name">The name of the Chocolatey package to manage.</param>
/// <param name="version">The version of the Chocolatey package to ensure is installed.</param>
/// <returns>
/// A <see cref="T:Fabricator.Core.Resource"/> checking the state of the package and applying necessary changes.
/// </returns>
let chocolateyPackage(name: string, version: string): Resource =
    {
        PresentableName = $"Package {name}"
        DependsOn = Resource.NoDependencies

        AlreadyApplied = fun () -> async {
            let! installedVersion = getInstalledPackageVersion name
            return installedVersion = Some version
        }
        Apply = fun () -> async {
            let! installedVersion = getInstalledPackageVersion name
            return!
                match installedVersion with
                | Some _ -> upgradePackage name version
                | None -> installPackage name version
        }
    }
