using CursedPasswordManager.Core;

namespace CursedPasswordManager.Tests;

public sealed class CredentialTests
{
    [Fact]public void EditAndRestoreKeepBothPasswords()
    {var c=CipherTests.Data().Credentials[0];var original=c.Password;c.ChangePassword("new fictional value");Assert.Equal(original,c.History[0].Password);c.ChangePassword(original);Assert.Equal("new fictional value",c.History[0].Password);Assert.Equal(original,c.Password);}
    [Fact]public void SamePasswordDoesNotAddHistory(){var c=new Credential{Password="same"};c.ChangePassword("same");Assert.Empty(c.History);}
    [Fact]public void SearchIsCaseInsensitiveAndFiltersFavorites()
    {var d=CipherTests.Data();Assert.Single(d.Search("FICTION",true,"Jogos","Nome"));Assert.Empty(d.Search("missing",false,null,"Nome"));d.Credentials[0].Favorite=false;Assert.Empty(d.Search("",true,null,"Nome"));}
    [Fact]public void RenameAndDeleteCategoryPreserveCredentials()
    {var d=CipherTests.Data();d.RenameCategory("Jogos","Pessoal");Assert.Equal("Pessoal",d.Credentials[0].Category);d.RemoveCategory("Pessoal");Assert.Equal("Outros",d.Credentials[0].Category);Assert.Single(d.Credentials);Assert.Throws<InvalidOperationException>(()=>d.RemoveCategory("Outros"));}
    [Fact]public void MergeKeepsDivergentStableIdAndDeduplicatesIdentical()
    {var d=CipherTests.Data();Assert.Equal(0,d.Merge(d.Clone()));var other=d.Clone();other.Credentials[0].ChangePassword("divergent fictional");Assert.Equal(1,d.Merge(other));Assert.Equal(2,d.Credentials.Select(c=>c.Id).Distinct().Count());Assert.Contains(d.Credentials,c=>c.Password=="divergent fictional");}
    [Fact]public void SameNameDoesNotOverwriteDifferentAccount()
    {var d=CipherTests.Data();var other=CipherTests.Data();other.Credentials[0].Account="other@example.invalid";d.Merge(other);Assert.Equal(2,d.Credentials.Count);}
    [Fact]public void CloneAndHistoryAreIndependent(){var d=CipherTests.Data();var copy=d.Clone();copy.Credentials[0].History.Clear();copy.Credentials.Clear();Assert.Single(d.Credentials);Assert.Single(d.Credentials[0].History);}
    [Fact]public void InvalidFieldsRejected(){var d=CipherTests.Data();d.Credentials[0].Password=new string('a',4097);Assert.Throws<InvalidDataException>(d.Validate);}
    [Fact]public void AuditFindsReuseCommonSequencesAndRepetition()
    {
        var d=new VaultData{Credentials=[new(){Service="a",Password="123456"},new(){Service="b",Password="123456"},new(){Service="c",Password="abcabcabcabcabc"},new(){Service="d",Password="password"},new(){Service="e",Password="K7!mR2@zP9#vT4$x"}]};
        var report=PasswordAudit.Analyze(d);Assert.Equal(5,report.Total);Assert.Equal(2,report.Reused);Assert.Equal(4,report.Weak);Assert.Equal(4,report.Findings.Count);
    }
    [Theory][InlineData("Nome")][InlineData("Criação")][InlineData("Alteração")][InlineData("Favoritos")][InlineData("Categoria")]
    public void EverySortReturnsAllEntries(string sort){var d=CipherTests.Data();Assert.Single(d.Search("",false,null,sort));}
}
