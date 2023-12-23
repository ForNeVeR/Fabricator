module Fabricator.Templates.FileTemplates

open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Fabricator.Resources.Files

let readParameterFile(path: string): Task<Map<string, string>> = task {
    let! content = File.ReadAllTextAsync path
    return JsonSerializer.Deserialize(content)
}

/// Processes the file, replacing all instances of <code>$VARIABLE_NAME$</code> with the value of the variable.
let templatedFile (localPath: string) (variables: Map<string, string>): FileSource =
    GeneratedContent(
        Path.GetFileName localPath,
        fun () ->
            let content = File.ReadAllText localPath
            let mutable result = content
            variables |> Map.iter(fun k v ->
                result <- result.Replace("$" + k + "$", v)
            )
            result |> Encoding.UTF8.GetBytes
    )
