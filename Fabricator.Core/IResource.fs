namespace Fabricator.Core

type IResource =
    abstract member PresentableName: string
    abstract member AlreadyApplied: unit -> Async<bool>
    abstract member Apply: unit -> Async<unit>
