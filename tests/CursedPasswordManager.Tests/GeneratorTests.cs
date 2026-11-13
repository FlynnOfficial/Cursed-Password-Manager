using CursedPasswordManager.Core;

namespace CursedPasswordManager.Tests;

public sealed class GeneratorTests
{
    [Theory][InlineData(10)][InlineData(30)][InlineData(60)]
    public void DefaultNumericPreservesLength(int length)
    { var result=PasswordGenerator.Generate(new(length));Assert.Equal(length,result.Value.Length);Assert.All(result.Value,c=>Assert.InRange(c,'0','9'));Assert.Equal(length*Math.Log2(10),result.EntropyBits,8); }
    [Theory][InlineData(0)][InlineData(9)][InlineData(61)][InlineData(int.MaxValue)]
    public void InvalidLengthsAreRejected(int length)=>Assert.Throws<ArgumentOutOfRangeException>(()=>PasswordGenerator.Generate(new(length)));
    [Fact]public void NoGroupsIsRejected()=>Assert.Throws<ArgumentException>(()=>PasswordGenerator.Generate(new(30,false)));
    [Fact]public void LeadingZeroIsPreserved()
    { Assert.Contains(Enumerable.Range(0,1000).Select(_=>PasswordGenerator.Generate(new()).Value),s=>s[0]=='0' && s.Length==30); }
    [Theory][InlineData(false,true,false,false)][InlineData(false,false,true,false)][InlineData(false,false,false,true)][InlineData(true,true,true,true)]
    public void EverySelectedGroupAppears(bool digits,bool lower,bool upper,bool symbols)
    {
        var options=new GeneratorOptions(10,digits,lower,upper,symbols);
        for(int i=0;i<300;i++) {var value=PasswordGenerator.Generate(options).Value;foreach(var group in PasswordGenerator.Groups(options))Assert.Contains(value,c=>group.Contains(c));Assert.All(value,c=>Assert.Contains(c,string.Concat(PasswordGenerator.Groups(options))));}
    }
    [Fact]public void ConditionalEntropyMatchesInclusionExclusion()
    {
        double accepted=Math.Pow(36,10)-Math.Pow(26,10)-Math.Pow(10,10);
        Assert.Equal(Math.Log2(accepted),PasswordGenerator.Entropy(new(10,true,true)),8);
        Assert.Equal(10*Math.Log2(36),PasswordGenerator.Entropy(new(10,true,true,false,false,false)),8);
    }
    [Fact]public void DistributionSmokeCheckHasNoGrossDigitBias()
    {
        int[] counts=new int[10];for(int i=0;i<1000;i++)foreach(char c in PasswordGenerator.Generate(new(60)).Value)counts[c-'0']++;
        Assert.All(counts,n=>Assert.InRange(n,5000,7000)); // Broad regression bound, not a proof of randomness.
    }
}
