// SPDX-FileCopyrightText: 2020-2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

namespace Fabricator.Core

open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Text.Json.Serialization

type IMachineDesignator =
    abstract member PresentableName: string
    abstract member IsCurrentMachine: bool

type MachineType =
    | Linux = 0
    | Windows = 1

type Machine = {
    Name: string
    Designator: IMachineDesignator
    Resources: IResource seq
    Type: MachineType
}

module Designators =
    type MachineKind =
        | Undefined = 0
        | Local = 1

    type internal MachineSpecification = {
        Kind: MachineKind
    }

    let currentMachine: IMachineDesignator =
        { new IMachineDesignator with
            member _.PresentableName = "[current machine]"
            member _.IsCurrentMachine = true }

    let fromConnectionsFile (path: string) (name: string): IMachineDesignator =
        let options = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
        options.Converters.Add(JsonFSharpConverter())
        options.Converters.Add(JsonStringEnumConverter())

        let content = File.ReadAllText(path)
        let specMap = JsonSerializer.Deserialize<Dictionary<string, MachineSpecification>>(content, options)
        let spec = specMap.[name]

        match spec.Kind with
        | MachineKind.Local -> currentMachine
        | _ -> failwith $"Could not create a designator of kind {spec.Kind}"
