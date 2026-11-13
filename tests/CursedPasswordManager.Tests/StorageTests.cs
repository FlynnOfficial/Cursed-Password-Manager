using System.Security.Cryptography;
using CursedPasswordManager.Security;
using CursedPasswordManager.Storage;

namespace CursedPasswordManager.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"CpmTests-"+Guid.NewGuid().ToString("N"));
    private string[] Paths=>Enumerable.Range(0,3).Select(i=>Path.Combine(_root,"copy"+i,"passwords.cpmvault")).ToArray();
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
    [Fact]public async Task ThreeCopiesIdenticalAndRecoverableAfterRestart()
    {using var s=await CipherTests.Session();var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);var store=new ReplicaStore(Paths);var result=await store.CommitAsync(bytes,s);Assert.True(result.Complete);foreach(var p in Paths)Assert.Equal(bytes,await File.ReadAllBytesAsync(p));var scan=await new ReplicaStore(Paths).ScanAsync();Assert.Equal(3,scan.Candidates.Count);var opened=await VaultCipher.OpenAsync(scan.Candidates[0].Bytes,CipherTests.Password);opened.Session.Dispose();}
    [Theory][InlineData(1)][InlineData(2)][InlineData(3)]
    public async Task MissingConfiguredCopiesRecoveredFromInternal(int mask)
    {using var s=await CipherTests.Session();var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);var store=new ReplicaStore(Paths);await store.CommitAsync(bytes,s);if((mask&1)!=0)Directory.Delete(Path.GetDirectoryName(Paths[1])!,true);if((mask&2)!=0)Directory.Delete(Path.GetDirectoryName(Paths[2])!,true);var restart=new ReplicaStore(Paths);var scan=await restart.ScanAsync();Assert.NotEmpty(scan.Candidates);Assert.True((await restart.CommitAsync(scan.Candidates[0].Bytes,s)).Complete);}
    [Fact]public async Task CorruptionIsDetectedAndCanBeRestoredAfterExplicitScan()
    {using var s=await CipherTests.Session();var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);var store=new ReplicaStore(Paths);await store.CommitAsync(bytes,s);var corrupt=bytes.ToArray();corrupt[^1]^=1;await File.WriteAllBytesAsync(Paths[1],corrupt);Assert.Contains(await store.VerifyAsync(s,VaultCipher.Hash(bytes)),v=>v.Status.Contains("não autenticada"));await Assert.ThrowsAsync<IOException>(()=>store.CommitAsync(bytes,s));await store.ScanAsync();Assert.True((await store.CommitAsync(bytes,s)).Complete);}
    [Fact]public async Task InterruptedTemporaryWriteLeavesOriginalIntact()
    {string path=Paths[0];await AtomicFile.WriteAsync(path,[1,2,3]);await Assert.ThrowsAsync<IOException>(()=>AtomicFile.WriteAsync(path,[4,5,6],()=>throw new IOException("Injected power loss before replacement")));Assert.Equal(new byte[]{1,2,3},await File.ReadAllBytesAsync(path));Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));}
    [Fact]public async Task PartialFailureReportsPendingButInternalSurvives()
    {using var s=await CipherTests.Session();var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);Directory.CreateDirectory(_root);string blocker=Path.Combine(_root,"not-a-directory");await File.WriteAllTextAsync(blocker,"fixture");var paths=new[]{Paths[0],Paths[1],Path.Combine(blocker,"vault")};var store=new ReplicaStore(paths);var result=await store.CommitAsync(bytes,s);Assert.False(result.Complete);Assert.Equal(bytes,await File.ReadAllBytesAsync(Paths[0]));}
    [Fact]public async Task InternalUnavailablePreventsAnyNewCommit()
    {using var s=await CipherTests.Session();var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);Directory.CreateDirectory(_root);string blocker=Path.Combine(_root,"blocker");await File.WriteAllTextAsync(blocker,"fixture");var store=new ReplicaStore([Path.Combine(blocker,"vault"),Paths[1]]);await Assert.ThrowsAsync<IOException>(()=>store.CommitAsync(bytes,s));Assert.False(File.Exists(Paths[1]));}
    [Fact]public async Task FileLockFailureIsVisible()
    {using var s=await CipherTests.Session();var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);Directory.CreateDirectory(Path.GetDirectoryName(Paths[2])!);using var locked=new FileStream(Paths[2]+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);var store=new ReplicaStore(Paths);Assert.False((await store.CommitAsync(bytes,s)).Complete);}
    [Fact]public async Task ConcurrentRevisionIsNotOverwritten()
    {using var s=await CipherTests.Session();var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);var a=new ReplicaStore(Paths);await a.CommitAsync(bytes,s);var b=new ReplicaStore(Paths);await b.ScanAsync();var next=VaultCipher.Seal(s,CipherTests.Data(),2,SHA256.HashData(bytes));await a.CommitAsync(next,s);await Assert.ThrowsAsync<IOException>(()=>b.CommitAsync(bytes,s));Assert.Equal(next,await File.ReadAllBytesAsync(Paths[0]));}
    [Fact]public async Task DivergentSameRevisionIsExposedNotAutomaticallySelected()
    {using var s=await CipherTests.Session();var a=VaultCipher.Seal(s,CipherTests.Data(),1);var b=VaultCipher.Seal(s,CipherTests.Data(),1);await AtomicFile.WriteAsync(Paths[0],a);await AtomicFile.WriteAsync(Paths[1],b);var scan=await new ReplicaStore(Paths).ScanAsync();Assert.Equal(2,scan.Candidates.Select(c=>c.Hash).Distinct().Count());}
    [Fact]public async Task SnapshotsAreBoundedAndPreviousCanBeOpened()
    {using var s=await CipherTests.Session();var store=new ReplicaStore(Paths);for(int i=1;i<=8;i++)await store.CommitAsync(VaultCipher.Seal(s,CipherTests.Data(),i),s);var files=Directory.GetFiles(Path.GetDirectoryName(Paths[0])!,"snapshot-*.cpmvault");Assert.Equal(5,files.Length);foreach(var p in files)Assert.NotEmpty(VaultCipher.Decrypt(await File.ReadAllBytesAsync(p),s).Credentials);}
    [Fact]public async Task MigrationKeepsOldCopyAndPopulatesNewDirectories()
    {using var s=await CipherTests.Session();var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);await new ReplicaStore(Paths).CommitAsync(bytes,s);var migrated=new ReplicaStore([Paths[0],Path.Combine(_root,"new","vault"),Paths[2]]);await migrated.ScanAsync();Assert.True((await migrated.CommitAsync(bytes,s)).Complete);Assert.Equal(bytes,await File.ReadAllBytesAsync(Paths[1]));}
    [Fact]public async Task UnreviewedDestinationIsNeverOverwritten()
    {using var s=await CipherTests.Session();var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);await AtomicFile.WriteAsync(Paths[1],bytes);await Assert.ThrowsAsync<IOException>(()=>new ReplicaStore(Paths).CommitAsync(bytes,s));Assert.False(File.Exists(Paths[0]));}
    [Fact]public async Task PortableSettingsAndRecoveryAreIndependentOfHostProfile()
    {var paths=new AppPaths(true,_root);var settings=new AppSettings{Theme="CursedModern"};await paths.SaveAsync(settings);Assert.Equal("CursedModern",(await paths.LoadAsync()).Theme);Assert.StartsWith(_root,paths.Recovery);Assert.All(paths.Replicas(settings),p=>Assert.StartsWith(_root,p));File.Delete(paths.SettingsPath);Assert.Equal("XpClassic",(await paths.LoadAsync()).Theme);Assert.Equal(Path.Combine(_root,"Recovery"),paths.Recovery);}
    [Fact]public async Task EncryptedExportWorksWithOwnPasswordAndPreservesHistory()
    {using var s=await VaultCipher.CreateSessionAsync("fictional export phrase",parameters:CipherTests.TestKdf);var bytes=VaultCipher.Seal(s,CipherTests.Data(),1);string path=Path.Combine(_root,"export.cpmbackup");await AtomicFile.WriteAsync(path,bytes);var opened=await VaultCipher.OpenAsync(await AtomicFile.ReadBoundedAsync(path),"fictional export phrase");using(opened.Session){Assert.Single(opened.Data.Credentials[0].History);Assert.Equal("Jogos",opened.Data.Credentials[0].Category);}await Assert.ThrowsAnyAsync<CryptographicException>(()=>VaultCipher.OpenAsync(bytes,CipherTests.Password));}
    [Fact]public async Task OversizedFileRejectedBeforeAllocation()
    {Directory.CreateDirectory(_root);string path=Path.Combine(_root,"huge");using(var file=File.Create(path))file.SetLength(VaultCipher.MaxFileBytes+1L);await Assert.ThrowsAsync<InvalidDataException>(()=>AtomicFile.ReadBoundedAsync(path));}
}
