module OldSiteParser.Shared.Hashing

open System.IO
open System.Security.Cryptography

/// Compute SHA-256 hash of a file using streaming (no full-file memory load).
/// Returns the hash as a lowercase hex string.
let computeFileHash (filePath: string) : string =
    use stream = File.OpenRead(filePath)
    let hashBytes = SHA256.HashData(stream)
    hashBytes |> Array.map (fun b -> b.ToString("x2")) |> String.concat ""
