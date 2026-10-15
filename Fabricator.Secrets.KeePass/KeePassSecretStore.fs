// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Secrets.KeePass

open System
open Fabricator.Core
open Pidamg.KeePass
open TruePath

/// The scope of an opened <see cref="T:Fabricator.Secrets.KeePass.KeePassSecretStore"/>, owning the opened database.
type private KeePassScope(path: AbsolutePath, database: KdbxDatabase) =
    let lockObj = obj()
    let mutable disposed = false

    let isInRecycleBin (recycleBin: Group | null) (entry: Entry) =
        match recycleBin, entry.ParentGroup with
        | NonNull recycleBin, NonNull parent ->
            parent = recycleBin || recycleBin.IsAncestorOf parent
        | _ -> false

    interface ISecretStoreScope with
        member _.GetSecret(SecretName name) = async {
            return lock lockObj (fun () ->
                if disposed then raise <| ObjectDisposedException "KeePassSecretStore"
                let recycleBin = database.GetRecycleBin()
                let entries =
                    database.FindAllEntries(fun entry -> entry.Title = name)
                    |> Seq.filter(fun entry -> not(isInRecycleBin recycleBin entry))
                    |> Seq.toArray
                match entries with
                | [||] -> null
                | [| entry |] -> Secrets.OfString entry.Password
                | _ ->
                    raise <| InvalidOperationException(
                        $"Found {entries.Length} entries titled \"{name}\" in KeePass database \"{path.Value}\". " +
                        "Entry titles must be unique."
                    )
            )
        }

        member _.Dispose() =
            lock lockObj (fun () ->
                if not disposed then
                    disposed <- true
                    database.Dispose()
            )

/// <summary>
/// <para>Secret store based on <a href="https://keepass.info/">KeePass</a> database.</para>
/// <para>It will ask the database's master password in console when opening the store.</para>
/// <para>
/// A secret is looked up by the entry title, and its value is the entry's password. Entries in the recycle bin are
/// ignored. Getting a secret fails if several entries have the same title.
/// </para>
/// </summary>
/// <param name="path">Path to the database file.</param>
type KeePassSecretStore(path: AbsolutePath) =
    interface ISecretStore with
        member this.Open(ui, _) = async {
            let prompt = $"Master password for KeePass database \"{path.Value}\":"
            let! database = async {
                use! masterPassword = ui.ReadSecret prompt
                return KdbxDatabase.Open(path.Value, Secrets.Materialize masterPassword)
            }
            return new KeePassScope(path, database)
        }
