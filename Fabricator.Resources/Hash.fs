module Fabricator.Resources.Hash

open System
open TruePath
open TruePath.SystemIo

type Sha256Hash(value: string) =
    member val private value = value

    override this.Equals(obj) =
        match obj with
        | :? Sha256Hash as other -> other.value = value
        | _ -> false

    override this.GetHashCode() = value.GetHashCode()
    override this.ToString() = value

    static member OfFile(path: AbsolutePath): Async<Sha256Hash> = async {
        use stream = path.OpenRead()
        use sha256 = System.Security.Cryptography.SHA256.Create()
        let! ct = Async.CancellationToken
        let! hash = Async.AwaitTask <| sha256.ComputeHashAsync(stream, ct)
        return Sha256Hash.OfBytes hash
    }

    static member OfString(value: string): Sha256Hash =
        Sha256Hash(value.ToUpperInvariant())

    static member OfBytes(bytes: byte[]): Sha256Hash =
        bytes
        |> Convert.ToHexString
        |> Sha256Hash.OfString

let Sha256(hash: string): Sha256Hash = Sha256Hash.OfString hash
