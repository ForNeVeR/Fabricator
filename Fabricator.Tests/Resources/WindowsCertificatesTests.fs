// SPDX-FileCopyrightText: 2025 Friedrich von Never <friedrich@fornever.me>
//
// SPDX-License-Identifier: MIT

module Fabricator.Tests.Resources.WindowsCertificatesTests

open System
open System.IO
open System.Runtime.InteropServices
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Threading.Tasks
open FSharp.Control.Tasks
open Xunit
open Fabricator.Core
open Fabricator.Resources.WindowsCertificates

// Helper to create a temporary self-signed certificate for testing
let private createTestCertificate() =
    use rsa = RSA.Create(2048)
    let request = new CertificateRequest(
        "CN=Test Certificate",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1
    )
    
    request.CertificateExtensions.Add(
        X509BasicConstraintsExtension(false, false, 0, true)
    )
    
    let certificate = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1.0),
        DateTimeOffset.UtcNow.AddDays(365.0)
    )
    
    certificate

// Helper to save certificate to a temporary file
let private saveCertificateToTempFile (cert: X509Certificate2) =
    let tempFile = Path.GetTempFileName()
    let certBytes = cert.Export(X509ContentType.Cert)
    File.WriteAllBytes(tempFile, certBytes)
    tempFile

// Helper to remove certificate from store if it exists
let private removeCertificateFromStore (cert: X509Certificate2) (storeLocation: CertificateStoreLocation) =
    use store = new X509Store(storeLocation.StoreName, storeLocation.Location)
    store.Open(OpenFlags.ReadWrite)
    let existing = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false)
    if existing.Count > 0 then
        store.Remove(existing.[0])

// Helper function to run a test with a temporary certificate file
let private withTempCertificate (action: X509Certificate2 -> string -> Task<'a>): Task<'a> = task {
    use cert = createTestCertificate()
    let tempFile = saveCertificateToTempFile cert
    try
        return! action cert tempFile
    finally
        if File.Exists(tempFile) then
            File.Delete(tempFile)
}

[<Fact(Skip="Windows-only test - requires Windows certificate store")>]
let ``PresentableName returns correct format``(): Task = task {
    do! withTempCertificate (fun cert tempFile -> task {
        let resource = installCertificate tempFile CertificateStores.CurrentUserMy
        let fileName = Path.GetFileName(tempFile)
        Assert.Equal($"Certificate \"{fileName}\" in CurrentUser/My", resource.PresentableName)
    })
}

[<Fact(Skip="Windows-only test - requires Windows certificate store")>]
let ``AlreadyApplied returns false when certificate not in store``(): Task = task {
    do! withTempCertificate (fun cert tempFile -> task {
        // Ensure certificate is not in store
        removeCertificateFromStore cert CertificateStores.CurrentUserMy
        
        let resource = installCertificate tempFile CertificateStores.CurrentUserMy
        let! result = resource.AlreadyApplied()
        Assert.False result
    })
}

[<Fact(Skip="Windows-only test - requires Windows certificate store")>]
let ``AlreadyApplied returns true when certificate already in store``(): Task = task {
    do! withTempCertificate (fun cert tempFile -> task {
        // Install certificate first
        use store = new X509Store(StoreName.My, StoreLocation.CurrentUser)
        store.Open(OpenFlags.ReadWrite)
        store.Add(cert)
        
        try
            let resource = installCertificate tempFile CertificateStores.CurrentUserMy
            let! result = resource.AlreadyApplied()
            Assert.True result
        finally
            // Clean up
            removeCertificateFromStore cert CertificateStores.CurrentUserMy
    })
}

[<Fact(Skip="Windows-only test - requires Windows certificate store")>]
let ``Apply installs certificate to store``(): Task = task {
    do! withTempCertificate (fun cert tempFile -> task {
        // Ensure certificate is not in store
        removeCertificateFromStore cert CertificateStores.CurrentUserMy
        
        try
            let resource = installCertificate tempFile CertificateStores.CurrentUserMy
            do! resource.Apply()
            
            // Verify certificate is now in store
            use store = new X509Store(StoreName.My, StoreLocation.CurrentUser)
            store.Open(OpenFlags.ReadOnly)
            let found = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false)
            Assert.Equal(1, found.Count)
        finally
            // Clean up
            removeCertificateFromStore cert CertificateStores.CurrentUserMy
    })
}

[<Fact(Skip="Windows-only test - requires Windows certificate store")>]
let ``Apply is idempotent``(): Task = task {
    do! withTempCertificate (fun cert tempFile -> task {
        // Ensure certificate is not in store
        removeCertificateFromStore cert CertificateStores.CurrentUserMy
        
        try
            let resource = installCertificate tempFile CertificateStores.CurrentUserMy
            do! resource.Apply()
            do! resource.Apply() // Apply twice
            
            // Verify certificate is in store only once
            use store = new X509Store(StoreName.My, StoreLocation.CurrentUser)
            store.Open(OpenFlags.ReadOnly)
            let found = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false)
            Assert.Equal(1, found.Count)
        finally
            // Clean up
            removeCertificateFromStore cert CertificateStores.CurrentUserMy
    })
}

[<Fact(Skip="Windows-only test - requires Windows certificate store")>]
let ``Apply and AlreadyApplied work together``(): Task = task {
    do! withTempCertificate (fun cert tempFile -> task {
        // Ensure certificate is not in store
        removeCertificateFromStore cert CertificateStores.CurrentUserMy
        
        try
            let resource = installCertificate tempFile CertificateStores.CurrentUserMy
            let! beforeApply = resource.AlreadyApplied()
            Assert.False beforeApply
            
            do! resource.Apply()
            
            let! afterApply = resource.AlreadyApplied()
            Assert.True afterApply
        finally
            // Clean up
            removeCertificateFromStore cert CertificateStores.CurrentUserMy
    })
}

[<Fact>]
let ``installCertificate fails with non-existent file``(): Task = task {
    let nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".cer")
    let resource = installCertificate nonExistentFile CertificateStores.CurrentUserMy
    
    let! ex = Assert.ThrowsAsync<Exception>(fun () -> Async.StartAsTask(resource.AlreadyApplied()) :> Task)
    Assert.Contains("Certificate file does not exist", ex.Message)
}

[<Fact(Skip="Windows-only test - requires Windows certificate store")>]
let ``Works with LocalMachineRoot store location``(): Task = task {
    do! withTempCertificate (fun cert tempFile -> task {
        let resource = installCertificate tempFile CertificateStores.LocalMachineRoot
        let fileName = Path.GetFileName(tempFile)
        Assert.Equal($"Certificate \"{fileName}\" in LocalMachine/Root", resource.PresentableName)
    })
}

[<Fact(Skip="Windows-only test - requires Windows certificate store")>]
let ``Works with custom store location``(): Task = task {
    do! withTempCertificate (fun cert tempFile -> task {
        let customStore = { Location = StoreLocation.CurrentUser; StoreName = StoreName.CertificateAuthority }
        let resource = installCertificate tempFile customStore
        let fileName = Path.GetFileName(tempFile)
        Assert.Equal($"Certificate \"{fileName}\" in CurrentUser/CertificateAuthority", resource.PresentableName)
    })
}
