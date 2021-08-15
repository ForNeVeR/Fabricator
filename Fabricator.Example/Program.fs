module Fabricator.Example

open System.IO

open Fabricator.Console
open Fabricator.Core
open Fabricator.Resources.Files

let private cluster = [|
    {
        Name = "localhost"
        Designator = Designators.fromConnectionsFile "connections.json" "localhost"
        Resources = [|
            FileResource(
                ContentFile "data/file.txt",
                Path.Combine(Path.GetTempPath(), "Fabricator.Example/file.txt")
            )
        |]
        Type = MachineType.Windows
    }
|]

[<EntryPoint>]
let main(args: string[]): int =
    EntryPoint.main args cluster
