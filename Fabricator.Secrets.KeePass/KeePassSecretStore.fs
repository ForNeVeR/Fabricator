// SPDX-FileCopyrightText: 2026 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Secrets.KeePass

open Fabricator.Core
open Pidamg.KeePass
open TruePath

type private KeePassScope(database: KdbxDatabase) =
    interface ISecretStoreScope with
        member this.GetSecret(SecretName name) =
            async.Return (
                match database.FindEntry name with
                | null -> null
                | entry -> Secrets.OfString entry.Password
            )

        member this.Dispose() =
            database.Dispose()

/// <summary>
/// <para>Secret store based on <a href="https://keepass.info/">KeePass</a> database.</para>
/// <para>It will ask the database's master password in console when opening the store.</para>
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
            return new KeePassScope(database)
        }
