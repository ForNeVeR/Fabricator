namespace Fabricator.Resources

open System.Text.Json.Nodes
open Fabricator.Core
open Fabricator.Resources.CommandUtil
open TruePath
open TruePath.SystemIo

type DotNetTool =
    static member Install(name: string, version: string, installationPath: AbsolutePath): IResource = {
        new IResource with
            member _.PresentableName = $"{name} {version}"
            member this.AlreadyApplied() = async {
                if not <| installationPath.ExistsDirectory() then return false else
                let! execResult =
                    runCommand "dotnet" [|
                        "tool"
                        "list"
                        "--tool-path"; installationPath.Value
                        "--format"; "json"
                    |]
                if not execResult.Success then failwithf $"Exit code from dotnet tool: {execResult.ExitCode}. Error: {execResult.StandardError}"
                let document = JsonNode.Parse(execResult.StandardOutput) |> nonNull
                let document = document.AsObject()
                let data = document.["data"] |> nonNull
                let data = data.AsArray()
                let entry = data |> Seq.exactlyOne |> nonNull
                let packageId = entry["packageId"] |> nonNull
                let packageId = packageId.AsValue().ToString()
                let packageVersion = entry["version"] |> nonNull
                let packageVersion = packageVersion.AsValue().ToString()

                if packageId <> name.ToLowerInvariant()
                then failwithf $"dotnet tool returned metainfo for unexpected package: {packageId}"

                return packageVersion = version
            }
            member this.Apply() = async {
                let! execResult =
                    runCommand "dotnet" [|
                        "tool"
                        "install"
                        name
                        "--version"; version
                        "--tool-path"; installationPath.Value
                    |]
                if not execResult.Success then failwithf $"Exit code from dotnet tool: {execResult.ExitCode}. Error: {execResult.StandardError}"
            }
    }

