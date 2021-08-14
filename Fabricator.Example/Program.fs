module Fabricator.Example

open System.IO

open Fabricator.Console
open Fabricator.Core
open Fabricator.Resources.Files

let private cluster = [|
    {
        Designator = Designators.fromConnectionsFile "connections.json" "localhost"
        Resources = [|
            FileResource(
                ContentFile "data/file.txt",
                Path.Combine(Path.GetTempPath(), "Fabricator.Example/file.txt")
            )
        |]
    }
|]

[<EntryPoint>]
let main(args: string[]): int =
    EntryPoint.main args cluster
