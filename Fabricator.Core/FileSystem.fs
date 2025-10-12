// SPDX-FileCopyrightText: 2021-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Core.FileSystem

open System.IO

let findNearestDirectoryWithFile (masks: string seq) (startDirectory: string): string option =
    let mutable currentPath = startDirectory
    let mutable result = None
    while currentPath <> null && Option.isNone result do
        let files = masks |> Seq.map(fun m -> Directory.EnumerateFiles(currentPath, m)) |> Seq.concat
        match Seq.tryHead files with
        | Some _ -> result <- Some currentPath
        | None -> currentPath <- Path.GetDirectoryName currentPath
    result
