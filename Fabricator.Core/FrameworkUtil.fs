module Fabricator.Core.FrameworkUtil

open System.IO
open System.Reflection

let getApplicationDirectoryPath(): string =
    let assemblyPath = Assembly.GetEntryAssembly().Location
    Path.GetDirectoryName assemblyPath
