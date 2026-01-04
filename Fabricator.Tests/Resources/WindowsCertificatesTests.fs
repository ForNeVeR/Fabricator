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
open TruePath
open TruePath.SystemIo

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
    let tempFile = Temporary.CreateTempFile()
    let certBytes = cert.Export(X509ContentType.Cert)
    File.WriteAllBytes(tempFile.Value, certBytes)
    tempFile

// Helper to remove certificate from store if it exists
let private removeCertificateFromStore (cert: X509Certificate2) (storeLocation: CertificateStoreLocation) =
    use store = new X509Store(storeLocation.StoreName, storeLocation.Location)
    store.Open(OpenFlags.ReadWrite)
    let existing = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false)
    if existing.Count > 0 then
        store.Remove(existing.[0])

// Helper function to ensure certificate cleanup after test
let private withCertificateCleanup (cert: X509Certificate2) (storeLocation: CertificateStoreLocation) (action: unit -> Task<'a>): Task<'a> = task {
    try
        return! action()
    finally
        removeCertificateFromStore cert storeLocation
}

// Helper function to run a test with a temporary certificate file
let private withTempCertificate (action: X509Certificate2 -> AbsolutePath -> Task<'a>): Task<'a> = task {
    use cert = createTestCertificate()
    let tempFile = saveCertificateToTempFile cert
    try
        return! action cert tempFile
    finally
        tempFile.Delete()
}

[<Fact>]
let ``PresentableName returns correct format``(): Task = task {
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        () // Skip on non-Windows
    else
        do! withTempCertificate (fun cert tempFile -> task {
            let resource = trustedCertificate tempFile CertificateStores.CurrentUserPersonal
            Assert.Equal($"Certificate \"{tempFile.FileName}\" in CurrentUser/My", resource.PresentableName)
        })
}

[<Fact>]
let ``AlreadyApplied returns false when certificate not in store``(): Task = task {
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        () // Skip on non-Windows
    else
        do! withTempCertificate (fun cert tempFile -> task {
            // Ensure certificate is not in store
            removeCertificateFromStore cert CertificateStores.CurrentUserPersonal
            
            do! withCertificateCleanup cert CertificateStores.CurrentUserPersonal (fun () -> task {
                let resource = trustedCertificate tempFile CertificateStores.CurrentUserPersonal
                let! result = resource.AlreadyApplied()
                Assert.False result
            })
        })
}

[<Fact>]
let ``AlreadyApplied returns true when certificate already in store``(): Task = task {
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        () // Skip on non-Windows
    else
        do! withTempCertificate (fun cert tempFile -> task {
            // Install certificate first
            use store = new X509Store(StoreName.My, StoreLocation.CurrentUser)
            store.Open(OpenFlags.ReadWrite)
            store.Add(cert)
            
            do! withCertificateCleanup cert CertificateStores.CurrentUserPersonal (fun () -> task {
                let resource = trustedCertificate tempFile CertificateStores.CurrentUserPersonal
                let! result = resource.AlreadyApplied()
                Assert.True result
            })
        })
}

[<Fact>]
let ``Apply installs certificate to store``(): Task = task {
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        () // Skip on non-Windows
    else
        do! withTempCertificate (fun cert tempFile -> task {
            // Ensure certificate is not in store
            removeCertificateFromStore cert CertificateStores.CurrentUserPersonal
            
            do! withCertificateCleanup cert CertificateStores.CurrentUserPersonal (fun () -> task {
                let resource = trustedCertificate tempFile CertificateStores.CurrentUserPersonal
                do! resource.Apply()
                
                // Verify certificate is now in store
                use store = new X509Store(StoreName.My, StoreLocation.CurrentUser)
                store.Open(OpenFlags.ReadOnly)
                let found = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false)
                Assert.Equal(1, found.Count)
            })
        })
}

[<Fact>]
let ``Apply is idempotent``(): Task = task {
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        () // Skip on non-Windows
    else
        do! withTempCertificate (fun cert tempFile -> task {
            // Ensure certificate is not in store
            removeCertificateFromStore cert CertificateStores.CurrentUserPersonal
            
            do! withCertificateCleanup cert CertificateStores.CurrentUserPersonal (fun () -> task {
                let resource = trustedCertificate tempFile CertificateStores.CurrentUserPersonal
                do! resource.Apply()
                do! resource.Apply() // Apply twice
                
                // Verify certificate is in store only once
                use store = new X509Store(StoreName.My, StoreLocation.CurrentUser)
                store.Open(OpenFlags.ReadOnly)
                let found = store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, false)
                Assert.Equal(1, found.Count)
            })
        })
}

[<Fact>]
let ``Apply and AlreadyApplied work together``(): Task = task {
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        () // Skip on non-Windows
    else
        do! withTempCertificate (fun cert tempFile -> task {
            // Ensure certificate is not in store
            removeCertificateFromStore cert CertificateStores.CurrentUserPersonal
            
            do! withCertificateCleanup cert CertificateStores.CurrentUserPersonal (fun () -> task {
                let resource = trustedCertificate tempFile CertificateStores.CurrentUserPersonal
                let! beforeApply = resource.AlreadyApplied()
                Assert.False beforeApply
                
                do! resource.Apply()
                
                let! afterApply = resource.AlreadyApplied()
                Assert.True afterApply
            })
        })
}

[<Fact>]
let ``trustedCertificate fails with non-existent file``(): Task = task {
    let tempDir = Temporary.SystemTempDirectory()
    let nonExistentFile = tempDir / (Guid.NewGuid().ToString() + ".cer")
    let resource = trustedCertificate nonExistentFile CertificateStores.CurrentUserPersonal
    
    let! ex = Assert.ThrowsAsync<Exception>(fun () -> Async.StartAsTask(resource.AlreadyApplied()) :> Task)
    Assert.Contains("Certificate file does not exist", ex.Message)
}

[<Fact>]
let ``Works with LocalMachineTrustedRootCertificationAuthorities store location``(): Task = task {
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        () // Skip on non-Windows
    else
        do! withTempCertificate (fun cert tempFile -> task {
            let resource = trustedCertificate tempFile CertificateStores.LocalMachineTrustedRootCertificationAuthorities
            Assert.Equal($"Certificate \"{tempFile.FileName}\" in LocalMachine/Root", resource.PresentableName)
        })
}

[<Fact>]
let ``Works with custom store location``(): Task = task {
    if not (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) then
        () // Skip on non-Windows
    else
        do! withTempCertificate (fun cert tempFile -> task {
            let customStore = { Location = StoreLocation.CurrentUser; StoreName = StoreName.CertificateAuthority }
            let resource = trustedCertificate tempFile customStore
            Assert.Equal($"Certificate \"{tempFile.FileName}\" in CurrentUser/CertificateAuthority", resource.PresentableName)
        })
}
