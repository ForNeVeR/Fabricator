// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Resources.Archive

open System.IO.Compression
open Fabricator.Core
open Fabricator.Resources.Hash
open TruePath
open TruePath.SystemIo

let private unpackAllFiles(archive: AbsolutePath, target: AbsolutePath) =
    ZipFile.ExtractToDirectory(archive.Value, target.Value)

let unpackArchive(
    archive: AbsolutePath,
    hash: Sha256Hash,
    destinationDirectory: AbsolutePath
): Resource =
    let outputHashFile = destinationDirectory / "fabricator-hash.txt"
    {
        PresentableName = $"Unpack archive \"{archive}\" to \"{destinationDirectory.Value}\""
        DependsOn = Resource.NoDependencies
        AlreadyApplied = fun () -> async {
            if not(outputHashFile.Exists()) then return false
            else

            let content = outputHashFile.ReadAllText()
            let existingHash = Sha256(content.Trim())
            return hash = existingHash
        }
        Apply = fun () -> async {
            if not(archive.Exists()) then failwithf $"Archive file \"{archive.Value}\" does not exist."
            let! hash = Sha256Hash.OfFile archive
            unpackAllFiles(archive, destinationDirectory)
            outputHashFile.WriteAllText(hash.ToString())
        }
    }
