// SPDX-FileCopyrightText: 2025-2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Resources

open System.IO.Compression
open Fabricator.Core
open Fabricator.Resources.Hash
open Fabricator.Resources.ResourceUtil
open TruePath
open TruePath.SystemIo

type Archive =
    /// <summary>Creates a resource unpacking a ZIP archive to a directory.</summary>
    /// <param name="archive">The path of the archive.</param>
    /// <param name="hash">The SHA-256 hash of the archive, used to detect whether it is already unpacked.</param>
    /// <param name="destinationDirectory">The directory to unpack the archive to.</param>
    /// <param name="dependsOn">The resources this resource depends on, e.g. the one downloading the archive.</param>
    static member unpackArchive(
        archive: AbsolutePath,
        hash: Sha256Hash,
        destinationDirectory: AbsolutePath,
        ?dependsOn: Resource seq
    ): Resource =
        let outputHashFile = destinationDirectory / "fabricator-hash.txt"
        {
            PresentableName = $"Unpack archive \"{archive}\" to \"{destinationDirectory.Value}\""
            DependsOn = dependencies dependsOn
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
                ZipFile.ExtractToDirectory(archive.Value, destinationDirectory.Value)
                outputHashFile.WriteAllText(hash.ToString())
            }
        }
