module Fabricator.Resources.Docker

open Fabricator.Core

type DockerSourceSpecification =
    {
        GitRepository: string
        GitReference: string
    }

type DockerOption =
    | Volume of hostPath: string * containerPath: string

type DockerSpecification =
    {
        Sources: DockerSourceSpecification
        DockerfilePath: string
        Name: string
        Tag: string
        Options: DockerOption seq
    }

type DockerResource(spec: DockerSpecification) =
    interface IResource with
        member this.AlreadyApplied() = failwith "todo"
        member this.Apply() = failwith "todo"
        member this.PresentableName = failwith "todo"

let dockerContainer(spec: DockerSpecification): DockerResource =
    DockerResource spec
